using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Bloom.Api;
using Bloom.web;
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
        public struct MIB_IPFORWARDROW              // MAKE EVERYTHING PRIVATE?
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
        public struct MIB_IPFORWARDTABLE            // MAKE EVERYTHING PRIVATE?
        {
            public int dwNumEntries;
            public MIB_IPFORWARDROW table;
        }

        // We use an unmanaged function in the C/C++ DLL "iphlpapi.dll".
        //   - "true": calling this function *can* set an error code,
        //     which will be retrieveable via Marshal.GetLastWin32Error()
        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern int GetIpForwardTable(IntPtr pIpForwardTable, ref int pdwSize, bool bOrder);

        // Holds relevant network interface attributes.
        public class InterfaceInfo                  // MAKE EVERYTHING PRIVATE?
        {
            public string IpAddr    { get; set; }
            public string NetMask   { get; set; }
            public string Type      { get; set; }
            public int Metric       { get; set; }
        }

        // Holds the current network interface candidate. After all interfaces are
        // checked this will hold the one with the lowest "interface metric", which
        // is the one that Windows will choose for UDP advertising.
        InterfaceInfo IfaceWinner = new InterfaceInfo();

        // Lists to hold useful information pulled from all network interfaces.
        //private List<string> IfIpAddresses = new List<string>();    // NEED THIS?
        //private List<string> IfNetmasks = new List<string>();       // NEED THIS?

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

            // Don't let the network interface get chosen by default per a UdpClient -- the wrong
            // one is sometimes chosen. Instead of a UdpClient use a Socket, and assign it the IP
            // address of the network interface having the lowest "interface metric."
            //
            //   NOTE: only a multi-homed machine will have more than one network interface
            //         supporting internet connectivity. This will not be common -- the typical
            //         BloomDesktop user is on Windows and has just one active network interface,
            //         likely Wi-Fi but could also be Ethernet.
            // ASSUME: the machine is single-homed. If this assumption proves to be wrong we can
            //         revisit and augment the interface selection process.

            // Do a survey of all network interfaces. Skip the inactive ones. For those that are
            // active find the one with the lowest interface metric. The "winner" will be noted in
            // the 'IfaceWinner' object along with the pieces of information needed for advertising. 
            GetInterfaceStackWillUse();
            _localIp = IfaceWinner.IpAddr;
            if (_localIp.Length == 0)
            {
                Debug.WriteLine("WiFiAdvertiser, ERROR: can't get local IP address, exiting");
                return;
            }

            // The local IP address is associated with one of the network interfaces. From that
            // same interface get the associated subnet mask (this will be needed to generate the
            // appropriate broadcast address for UDP advertising).
            _subnetMask = IfaceWinner.NetMask;
            if (_subnetMask.Length == 0)
            {
                Debug.WriteLine("WiFiAdvertiser, ERROR: can't get subnet mask, exiting");
                return;
            }

            // The typical broadcast address (255.255.255.255) doesn't work with a raw socket:
            //      "System.Net.Sockets.SocketException (0x80004005): An attempt was made
            //      to access a socket in a way forbidden by its access permissions"
            // Rather, the broadcast address must be calculated from the local IP address and
            // the subnet mask.
            _remoteIp = CalculateBroadcastAddress(_localIp, _subnetMask);
            if (_remoteIp.Length == 0)
            {
                Debug.WriteLine("WiFiAdvertiser, ERROR: can't make broadcast address, exiting");
                return;
            }

            Debug.WriteLine("WiFiAdvertiser, UDP advertising will use: _localIp    = " + _localIp);
            Debug.WriteLine("                                          _subnetMask = " + _subnetMask);
            Debug.WriteLine("                                          _remoteIp   = " + _remoteIp);

            try
            {
                _localEP = new IPEndPoint(IPAddress.Parse(_localIp), Port);

                _remoteEP = new IPEndPoint(IPAddress.Parse(_remoteIp), Port);

                _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                _sock.Bind(_localEP);

                // Socket is ready. Begin advertising once per second, indefinitely.
                while (true)
                {
                    if (!Paused)
                    {
                        UpdateAdvertisementBasedOnCurrentIpAddress();

                        // No need to transmit on a separate thread; this thread spends most of its time
                        // sleeping. This is a simple socket and we are sending only a few hundred bytes.
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
                //_client.Close();
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

                // If we do eventually add the capability to display the advert as a QR code,
                // the local IP address will need to be included. Might as well add it now.
                advertisement.senderIP = _currentIpAddress;

                _sendBytes = Encoding.UTF8.GetBytes(advertisement.ToString());
                Debug.WriteLine("UDP advertising will have: source addr    = " + _currentIpAddress); // TEMPORARY
                //EventLog.WriteEntry("Application", "Serving at http://" + _currentIpAddress + ":" + ChorusHubOptions.MercurialPort, EventLogEntryType.Information);
            }
        }

        // Examine all network interfaces. For each *active* one, check each of its
        // IP addresses. Ignore the IPv6 addresses and record interface information
        // for each IPv4 address:
        //   a. type (wired or WiFi) -- not essential but log it for tech support
        //   b. IP address
        //   c. subnet mask
        //   d. interface metric
        // The goal is to find the address having the lowest metric. For each
        // address compare its metric to the lowest so far (saved in the result
        // struct 'IfaceWinner'). If the one being checked has a new lowest-so-far
        // metric, save it (and its associated IP address, subnet mask, and type)
        // in the result struct. After all interfaces have been checked the lowest
        // metric, along with IP addr/mask/type, will be in 'IfaceWinner'.
        //
        void GetInterfaceStackWillUse()
        {
            int currentIfaceMetric;

            // Initialize result struct's metric field to the highest possible value
            // so the first interface metric value seen will always replace it.
            IfaceWinner.Metric = int.MaxValue;

            // Get key attributes of *active* network interfaces.
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                // If this interface is not active, skip it.
                if (ni.OperationalStatus == OperationalStatus.Down)
                {
                    continue;
                }

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
                    // We don't consider IPv6 so filter for IPv4 ('InterNetwork')
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        currentIfaceMetric = GetMetricForInterface(ipv4Props.Index);

                        // If this interface's metric is lower than what we've seen
                        // so far, save it and its relevant associated values.
                        if (currentIfaceMetric < IfaceWinner.Metric)
                        {
                            IfaceWinner.IpAddr = ip.Address.ToString();
                            IfaceWinner.NetMask = ip.IPv4Mask.ToString();
                            IfaceWinner.Type = ni.NetworkInterfaceType.ToString();
                            IfaceWinner.Metric = currentIfaceMetric;
                        }
                    }
                }
            }
        }

        // Get a key piece of info ("metric") from the specified network interface.
        // Based on ChatGPT and:
        // https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getipforwardtable
        //
        // Retrieving the metric is, unfortunately, not as simple as grabbing one of
        // the fields in the network interface. The metric resides in the network
        // stack routing table. One of the interface fields ("Index") is also in the
        // routing table and is how we correlate the two.
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
                // Copy the routing table into buffer for examination. If the result
                // is not 0 then something went wrong.
                int error = GetIpForwardTable(tableBuf, ref size, false);
                if (error != 0)
                {
                    // Note: it is tempting to add a dealloc call here, but don't.
                    // The dealloc in the finally-block *will* be done (I tried it).
                    Console.WriteLine("  GetMetricForInterface, ERROR, GetIpForwardTable() = {0}, returning {1}", error, int.MaxValue);
                    return int.MaxValue;
                }

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

        // Function: for a given local IP address and subnet mask, construct the
        //     appropriate broadcast address.
        //
        // Philosophy: the subnet mask indicates how to handle the IP address bits --
        //     - '1' mask bits indicate the "network address" portion of the IP address.
        //       The broadcast address will aim at the same network so just copy the IP
        //       address bits into the same positions of the broadcast address. 'XORing'
        //       these '1' mask bits with '1' flips them to '0'. Then 'ORing' those '0'
        //       bits with the IP bits keeps the IP bits unchanged.
        //     - '0' mask bits indicate the "host ID" portion of the IP address. We want
        //       to fill this portion of the broadcast address with '1's so all hosts on
        //       the network will see the transmission. The same operations work here
        //       too: 'XORing' the '0' mask bits with '1' flips them to '1', which when
        //       'ORed' with anything gives '1' which is what we want for the host bits.
        //
        // Algorithm:
        //     convert IP address and corresponding subnet mask to byte arrays
        //     create byte array to hold broadcast address result
        //     for each IP address octet and corresponding subnet mask octet, starting with most significant
        //         compute broadcast address octet per "Philosophy" above
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
                    throw new ArgumentException("IP address and subnet mask length mismatch.");
                }

                byte[] bcastBytes = new byte[ipBytes.Length];
                for (int i = 0; i < ipBytes.Length; i++)
                {
                    //Console.WriteLine("  ipBytes[{0}]={1}, subnetBytes[{2}]={3}, subnetBytes[{4}]^255={5}",
                    //                     i, ipBytes[i], i, subnetBytes[i], i, subnetBytes[i]^255);

                    bcastBytes[i] = (byte)(ipBytes[i] | (subnetBytes[i] ^ 255));

                    // TEMPORARY DEBUG; either way works
                    //Console.WriteLine("  result = {0}.{1}.{2}.{3}", (byte)bcastBytes[0], (byte)bcastBytes[1],
                    //                                                (byte)bcastBytes[2], (byte)bcastBytes[3]);
                    //Console.WriteLine("  result = " + String.Join(" ", bcastBytes));
                }
                return new IPAddress(bcastBytes).ToString();
            }
            catch (Exception)
            {
                return "Invalid IP address or subnet mask";
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
