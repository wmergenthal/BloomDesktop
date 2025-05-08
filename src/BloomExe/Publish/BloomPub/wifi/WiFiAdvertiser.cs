using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.web;
using L10NSharp;
using SIL.Progress;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// This class broadcasts a message over the network offering to supply a book to any Android that wants it.
    /// </summary>
    public class WiFiAdvertiser : IDisposable
    {
        // The information we will advertise.
        public string BookTitle;
        private string _bookVersion;

        public string BookVersion
        {
            get { return _bookVersion; }
            set
            {
                _bookVersion = value;
                // In case this gets modified after we start advertising, we need to recompute the advertisement
                // next time we send it. Clearing this makes sure it happens.
                _currentIpAddress = "";
            }
        }
        public string TitleLanguage;

        private Thread _thread;
        private IPEndPoint _localEP;
        private IPEndPoint _remoteEP;
        private Socket _sock;
        private string _localIp = "";
        private string _remoteIp = "";  // UDP broadcast address, will *not* be 255.255.255.255
        private string _subnetMask = "";
        private string _currentIpAddress = "";
        private string _cachedIpAddress = "";

        // Layout of a row in the IPv4 routing table.
        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_IPFORWARDROW
        {
            public uint dwForwardDest;
            public uint dwForwardMask;
            public uint dwForwardPolicy;
            public uint dwForwardNextHop;
            public int dwForwardIfIndex;
            public int dwForwardType;
            public int dwForwardProto;
            public int dwForwardAge;
            public int dwForwardNextHopAS;
            public int dwForwardMetric1;  // the "interface metric" we need
            public int dwForwardMetric2;
            public int dwForwardMetric3;
            public int dwForwardMetric4;
            public int dwForwardMetric5;
        }

        // Holds a copy of the IPv4 routing table, which we will examine
        // to find which row/route has the lowest "interface metric".
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_IPFORWARDTABLE
        {
            private int dwNumEntries;
            private MIB_IPFORWARDROW table;
        }

        // We use an unmanaged function in the C/C++ DLL "iphlpapi.dll".
        //   - "true": calling this function *can* set an error code,
        //     which will be retrieveable via Marshal.GetLastWin32Error()
        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern int GetIpForwardTable(IntPtr pIpForwardTable, ref int pdwSize, bool bOrder);

        // Holds relevant network interface attributes.
        private class InterfaceInfo
        {
            public string IpAddr      { get; set; }
            public string Description { get; set; }
            public string NetMask     { get; set; }
            public int Metric         { get; set; }
        }

        // Hold the current network interface candidates, one for Wi-Fi and one
        // for Ethernet.
        private InterfaceInfo IfaceWifi = new InterfaceInfo();
        private InterfaceInfo IfaceEthernet = new InterfaceInfo();

        // Possible results from network interface assessment.
        private enum CommTypeToExpect
        {
            None = 0,
            WiFi = 1,
            Ethernet = 2
        }

        // The port on which we advertise.
        // ChorusHub uses 5911 to advertise. Bloom looks for a port for its server at 8089 and 10 following ports.
        // https://en.wikipedia.org/wiki/List_of_TCP_and_UDP_port_numbers shows a lot of ports in use around 8089,
        // but nothing between 5900 and 5931. Decided to use a number similar to ChorusHub.
        private const int Port = 5913; // must match port in BloomReader NewBookListenerService.startListenForUDPBroadcast
        private byte[] _sendBytes; // Data we send in each advertisement packet
        private readonly WebSocketProgress _progress;

        internal WiFiAdvertiser(WebSocketProgress progress)
        {
            _progress = progress;
        }

        public void Start()
        {
            _thread = new Thread(Work);
            _thread.Start();
        }

        public bool Paused { get; set; }

        private void Work()
        {
            _progress.Message(
                idSuffix: "beginAdvertising",
                message: "Advertising book to Bloom Readers on local network..."
            );

            // We must be confident that the local IP address we advertise in the UDP broadcast
            // packet is the same one the network stack will use for the broadcast. Gleaning the
            // local IP address from a UdpClient usually yields the correct one, but unfortunately it
            // can be different on some machines. When that happens the remote Android gets the
            // wrong address from the advert, and Desktop never hears the Android book request.
            // 
            // To mitigate: instead of a UdpClient, use a Socket and assign it the IP address
            // of the network interface having the lowest "interface metric," the interface the
            // stack will use.
            //
            // The PC on which this runs likely has both WiFi and Ethernet. They can both work,
            // but preference is given to WiFi. The reason: although this PC can likely go either
            // way, the Android device only has WiFi. For the book transfer to work both the PC
            // and the Android must be on the same subnet. If the PC is using Ethernet it may not
            // be on the same subnet as WiFi, especially on larger networks. The chances that both
            // PC and Android are on the same subnet are greatest if both are using WiFi.

            // Determine which interface the network stack will use.
            CommTypeToExpect ifcResult = GetInterfaceStackWillUse();

            if (ifcResult == CommTypeToExpect.None)
            {
                Debug.WriteLine("WiFiAdvertiser, local IP not found");
                return;
            }

            string ifaceDesc = "";

            if (ifcResult == CommTypeToExpect.WiFi)
            {
                _localIp = IfaceWifi.IpAddr;
                _subnetMask = IfaceWifi.NetMask;
                ifaceDesc = IfaceWifi.Description;
            }
            else
            {
                _localIp = IfaceEthernet.IpAddr;
                _subnetMask = IfaceEthernet.NetMask;
                ifaceDesc = IfaceEthernet.Description;
            }

            // The typical broadcast address (255.255.255.255) doesn't work with a raw socket:
            //      "System.Net.Sockets.SocketException (0x80004005): An attempt was made
            //      to access a socket in a way forbidden by its access permissions"
            // Rather, the broadcast address must be calculated from the local IP address and
            // subnet mask.
            _remoteIp = CalculateBroadcastAddress(_localIp, _subnetMask);
            if (_remoteIp.Length == 0)
            {
                Debug.WriteLine("WiFiAdvertiser, ERROR: can't make broadcast address, exiting");
                return;
            }

            try
            {
                // Set up destination endpoint.
                _remoteEP = new IPEndPoint(IPAddress.Parse(_remoteIp), Port);

                // Set up local socket and assign its IP address.
                _localEP = new IPEndPoint(IPAddress.Parse(_localIp), Port);
                _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _sock.Bind(_localEP);

                // Log key values for tech support.
                Debug.WriteLine("UDP advertising will use: _localIp    = {0} ({1})", _localIp, ifaceDesc);
                Debug.WriteLine("                          _subnetMask = " + _subnetMask);
                Debug.WriteLine("                          _remoteIp   = {0}:{1}", _remoteEP.Address, _remoteEP.Port);

                // Local and remote are ready. Advertise once per second, indefinitely.
                while (true)
                {
                    if (!Paused)
                    {
                        UpdateAdvertisementBasedOnCurrentIpAddress();

                        // No need to transmit on a separate thread. Just use this one -- it spends
                        // most of its time sleeping, and we are sending only a few hundred bytes.
                        Debug.WriteLine("WiFiAdvertiser, broadcasting advert to: {0}:{1}", _remoteEP.Address, _remoteEP.Port); // TEMPORARY!
                        _sock.SendTo(_sendBytes, 0, _sendBytes.Length, SocketFlags.None, _remoteEP);
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (SocketException e)
            {
                Debug.WriteLine("WiFiAdvertiser::Work, SocketException: " + e);
                // Don't know what _progress.Message() is desired here, add as appropriate.
            }
            catch (ThreadAbortException)
            {
                _progress.Message(idSuffix: "Stopped", message: "Stopped Advertising.");
            }
            catch (Exception error)
            {
                // not worth localizing
                _progress.MessageWithoutLocalizing(
                    $"Error in Advertiser: {error.Message}",
                    ProgressKind.Error
                );
            }
        }

        public static void SendCallback(IAsyncResult args) { }

        /// <summary>
        /// Since this is typically not a real "server", its ipaddress could be assigned dynamically,
        /// and could change each time someone wakes it up.
        /// </summary>
        private void UpdateAdvertisementBasedOnCurrentIpAddress()
        {
            _currentIpAddress = _localIp;

            if (_cachedIpAddress != _currentIpAddress)
            {
                _cachedIpAddress = _currentIpAddress;
                dynamic advertisement = new DynamicJson();
                advertisement.title = BookTitle;
                advertisement.version = BookVersion;
                advertisement.language = TitleLanguage;
                advertisement.protocolVersion = WiFiPublisher.ProtocolVersion;
                advertisement.sender = System.Environment.MachineName;

                // If we do eventually add capability to display the advert as a QR code,
                // the local IP address will need to be included. Might as well add it now.
                advertisement.senderIP = _currentIpAddress;

                _sendBytes = Encoding.UTF8.GetBytes(advertisement.ToString());
                //EventLog.WriteEntry("Application", "Serving at http://" + _currentIpAddress + ":" + ChorusHubOptions.MercurialPort, EventLogEntryType.Information);
            }
        }

        // Survey the network interfaces and determine which one, if any, the network
        // stack will use for network traffic.
        //   - During the assessment the current leading WiFi candidate will be held in
        //     'IfaceWifi', and similarly the current best candidate for Ethernet will
        //     be in 'IfaceEthernet'.
        //   - After assessment inform calling code of the winner by returning an enum
        //     indicating which of the candidate structs to draw from: WiFi, Ethernet,
        //     or neither.
        //
        private CommTypeToExpect GetInterfaceStackWillUse()
        {
            int currentIfaceMetric;

            // Initialize result structs metric field to the highest possible value
            // so the first interface metric value seen will always replace it.
            IfaceWifi.Metric = int.MaxValue;
            IfaceEthernet.Metric = int.MaxValue;

            // Retrieve all network interfaces that are *active*.
            var allOperationalNetworks = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up).ToArray();

            if (!allOperationalNetworks.Any())
            {
                Debug.WriteLine("WiFiAdvertiser, ERROR, no network interfaces are operational");
                return CommTypeToExpect.None;
            }

            // Get key attributes of active network interfaces.
            foreach (NetworkInterface ni in allOperationalNetworks)
            {
                // If we can't get IP or IPv4 properties for this interface, skip it.
                var ipProps = ni.GetIPProperties();
                if (ipProps == null)
                {
                    continue;
                }
                var ipv4Props = ipProps.GetIPv4Properties();
                if (ipv4Props == null)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation ip in ipProps.UnicastAddresses)
                {
                    // We don't consider IPv6 so filter for IPv4 ('InterNetwork')...
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        // ...And of these we care only about WiFi and Ethernet.
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        {
                            currentIfaceMetric = GetMetricForInterface(ipv4Props.Index);

                            // Save this interface if its metric is lowest we've seen so far.
                            if (currentIfaceMetric < IfaceWifi.Metric)
                            {
                                IfaceWifi.IpAddr = ip.Address.ToString();
                                IfaceWifi.NetMask = ip.IPv4Mask.ToString();
                                IfaceWifi.Description = ni.Description;
                                IfaceWifi.Metric = currentIfaceMetric;
                            }
                        }
                        else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                        {
                            currentIfaceMetric = GetMetricForInterface(ipv4Props.Index);

                            // Save this interface if its metric is lowest we've seen so far.
                            if (currentIfaceMetric < IfaceEthernet.Metric)
                            {
                                IfaceEthernet.IpAddr = ip.Address.ToString();
                                IfaceEthernet.NetMask = ip.IPv4Mask.ToString();
                                IfaceEthernet.Description = ni.Description;
                                IfaceEthernet.Metric = currentIfaceMetric;
                            }
                        }
                    }
                }
            }

            // Active network interfaces have all been assessed.
            //   - The WiFi interface having the lowest metric has been saved in the
            //     WiFi result struct. Note: if no active WiFi interface was seen then
            //     the result struct's metric field will still have its initial value.
            //   - Likewise for Ethernet.
            // Now choose the winner, if there is one:
            //   - If we saw an active WiFi interface, return that
            //   - Else if we saw an active Ethernet interface, return that
            //   - Else there is no winner so return none
            if (IfaceWifi.Metric < int.MaxValue)
            {
                return CommTypeToExpect.WiFi;
            }
            if (IfaceEthernet.Metric < int.MaxValue)
            {
                return CommTypeToExpect.Ethernet;
            }

            Debug.WriteLine("UDP advertising: no suitable network interface found");
            return CommTypeToExpect.None;
        }

        // Get a key piece of info ("metric") from the specified network interface.
        // https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getipforwardtable
        //
        // Retrieving the metric is not as simple as grabbing one of the fields in
        // the network interface. The metric resides in the network stack routing
        // table. One of the interface fields ("Index") is also in the routing table
        // and is how we correlate the two.
        //   - Calling code (walking the interface collection) passes in the index
        //     of the interface whose "best" metric it wants.
        //   - This function walks the routing table looking for all rows (each of
        //     which is a route) containing that index. It notes the metric in each
        //     route and returns the lowest among all routes/rows for the interface.
        //
        int GetMetricForInterface(int interfaceIndex)
        {
            int bestMetric;

            // Preliminary: call with a null buffer ('size') to learn how large a
            // buffer is needed to hold a copy of the routing table.
            int size = 0;
            GetIpForwardTable(IntPtr.Zero, ref size, false);

            // 'size' now shows how large a buffer is needed, so allocate it.
            IntPtr tableBuf = Marshal.AllocHGlobal(size);

            try
            {
                // Copy the routing table into buffer for examination.
                // If the result code is not 0 then something went wrong.
                int error = GetIpForwardTable(tableBuf, ref size, false);
                if (error != 0)
                {
                    // It is tempting to add a dealloc call here before bailing, but
                    // don't. The dealloc in the finally-block *will* be done (I checked).
                    Console.WriteLine("  GetMetricForInterface, ERROR, GetIpForwardTable() = {0}, returning {1}", error, int.MaxValue);
                    return int.MaxValue;
                }

                // Get number of routing table entries.
                int numEntries = Marshal.ReadInt32(tableBuf);

                // Advance pointer past the integer to point at 1st row.
                IntPtr rowPtr = IntPtr.Add(tableBuf, 4);

                // Initialize to "worst" possible metric (Win10 Pro: 2^31 - 1).
                // It can only get better from there!
                bestMetric = int.MaxValue;

                // Walk the routing table looking for rows involving the the network
                // interface passed in. For each such row/route, check the metric.
                // If it is lower than the lowest we've yet seen, save it to become
                // the new benchmark.
                for (int i = 0; i < numEntries; i++)
                {
                    MIB_IPFORWARDROW row = Marshal.PtrToStructure<MIB_IPFORWARDROW>(rowPtr);
                    if (row.dwForwardIfIndex == interfaceIndex)
                    {
                        bestMetric = Math.Min(bestMetric, row.dwForwardMetric1);
                    }
                    rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf<MIB_IPFORWARDROW>());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tableBuf);
            }

            return bestMetric;
        }

        // For a given local IP address and subnet mask, construct the appropriate
        // broadcast address.
        //
        // Rationale:
        //       The subnet mask indicates how to handle the IP address bits:
        //     - '1' mask bits indicate the "network address" portion of the IP address.
        //       The broadcast address will aim at the same network so just copy the IP
        //       address bits into the same positions of the broadcast address. 'XORing'
        //       these '1' mask bits with '1' flips them to '0'. Then 'ORing' those '0'
        //       bits with the IP bits keeps the IP bits unchanged.
        //     - '0' mask bits indicate the "host ID" portion of the IP address. We want
        //       to fill this portion of the broadcast address with '1's so all hosts on
        //       the network will see the transmission. The same operations work here
        //       too: 'XORing' the '0' mask bits with '1' flips them to '1', which when
        //       'ORed' with anything gives '1', which is what we want for the host bits.
        //
        // Algorithm:
        //     convert IP address and corresponding subnet mask to byte arrays
        //     create byte array to hold broadcast address result
        //     for each IP address octet and corresponding subnet mask octet, starting with most significant
        //         compute broadcast address octet per "Rationale" above
        //     end
        //     convert broadcast address byte array to IP address string and return it
        //
        // The local IP and mask inputs are not explicitly checked. Any issues they may
        // have will become apparent by the catch block firing. 
        //
        private string CalculateBroadcastAddress(string ipIn, string maskIn)
        {
            try
            {
                IPAddress ipAddress = IPAddress.Parse(ipIn);
                IPAddress subnetMask = IPAddress.Parse(maskIn);
                byte[] ipBytes = ipAddress.GetAddressBytes();
                byte[] subnetBytes = subnetMask.GetAddressBytes();

                if (ipBytes.Length != subnetBytes.Length)
                {
                    Console.WriteLine("CalculateBroadcastAddress, ERROR, length mismatch, IP vs mask: {0}, {1}",
                        ipBytes.Length, subnetBytes.Length);
                }

                byte[] bcastBytes = new byte[ipBytes.Length];
                for (int i = 0; i < ipBytes.Length; i++)
                {
                    bcastBytes[i] = (byte)(ipBytes[i] | (subnetBytes[i] ^ 255));
                }
                return new IPAddress(bcastBytes).ToString();
            }
            catch (Exception)
            {
                // Invalid IP address or subnet mask.
                return "";
            }
        }

        public void Stop()
        {
            if (_thread == null)
                return;

            //EventLog.WriteEntry("Application", "Advertiser Stopping...", EventLogEntryType.Information);
            _thread.Abort();
            _thread.Join(2 * 1000);
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
