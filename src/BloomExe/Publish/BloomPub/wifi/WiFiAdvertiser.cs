using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.web;
using System.Drawing.Imaging;
using System.Net.NetworkInformation;
using System.Management;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;
using System.Security.Cryptography;
using SIL.Windows.Forms.Progress;
using NuGet;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// This class broadcasts a message over the network offering to supply a book to any Android that wants it.
    /// </summary>
    public class WiFiAdvertiser : IDisposable
    {
        // The information that BloomDesktop will advertise, via both
        // UDP broadcast (this class) and QR code (a different class).
        public string BookTitle;
        private string _bookVersion;
        dynamic advertisement = new DynamicJson();
        private Boolean advertStringIsReady = false;

        public string BookVersion
        {
            get { return _bookVersion; }
            set
            {
                _bookVersion = value;
                // In case this gets modified after we start advertising, we need to recompute the advertisement
                // next time we send it. Clearing this makes sure it happens.
                _cachedIpAddress = "";
            }
        }
        public string TitleLanguage;

        private UdpClient _client;
        private Thread _thread;
        private IPEndPoint _endPoint;

        // The port on which we advertise.
        // ChorusHub uses 5911 to advertise. Bloom looks for a port for its server at 8089 and 10 following ports.
        // https://en.wikipedia.org/wiki/List_of_TCP_and_UDP_port_numbers shows a lot of ports in use around 8089,
        // but nothing between 5900 and 5931. Decided to use a number similar to ChorusHub.
        private const int Port = 5913; // must match port in BloomReader NewBookListenerService.startListenForUDPBroadcast
        private string _currentIpAddress;
        private string _cachedIpAddress = "";
        private byte[] _sendBytes; // Data we send in each advertisement packet
        private readonly WebSocketProgress _progress;

        internal WiFiAdvertiser(WebSocketProgress progress)
        {
            _progress = progress;
        }

        // The QR advertising thread calls here to get a copy of the current advertising
        // string. This string will be displayed on the Bloom Desktop screen as a QR code.
        // The UDP advert and QR code must contain the exact same data.
        public string ShareAdvertString()
        {
            if (advertStringIsReady == true)
            {
                return advertisement.ToString();
            }
            else
            {
                Debug.WriteLine("WM, WiFiAdvertiser::ShareAdvertString, advert not ready"); // WM, temporary
                return "{}";
            }
        }

        public void Start()
        {
            // The doc seems to indicate that EnableBroadcast is required for doing broadcasts.
            // In practice it seems to be required on Mono but not on Windows.
            // This may be fixed in a later version of one platform or the other, but please
            // test both if tempted to remove it.
            _client = new UdpClient { EnableBroadcast = true };
            _endPoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), Port);
            _thread = new Thread(Work);
            Debug.WriteLine("WM, WiFiAdvertiser::Start, work thread start"); // WM, temporary
            _thread.Start();
        }

        public bool Paused { get; set; }

        private void Work()
        {
            _progress.Message(
                idSuffix: "beginAdvertising",
                message: "Advertising book to Bloom Readers on local network..."
            );

            Debug.WriteLine("WM, WiFiAdvertiser::Work, begin UDP advertising loop"); // WM, temporary
            try
            {
                while (true)
                {
                    if (!Paused)
                    {
                        UpdateAdvertisementBasedOnCurrentIpAddress();
                        Debug.WriteLine(
                            "WM, WiFiAdvertiser::Work, broadcasting UDP advert ({0} bytes) on port {1}",
                            _sendBytes.Length,
                            Port
                        ); // WM, temporary
                        _client.BeginSend(
                            _sendBytes,
                            _sendBytes.Length,
                            _endPoint,
                            SendCallback,
                            _client
                        );

                        // WM, VERY TEMPORARY, to test arbitration provided by the mutex.
                        // Ensure that multiple Reader tablets respond to the same UDP advert by
                        // guaranteeing that only one is ever broadcast.
                        //Debug.WriteLine("WM, WiFiAdvertiser::Work, DID ONE UDP BROADCAST ADVERT, NO MORE");
                        //break;
                    }
                    Debug.WriteLine("WM, WiFiAdvertiser::Work, 1-second sleep"); // WM, temporary
                    Thread.Sleep(1000);
                }
            }
            catch (ThreadAbortException)
            {
                _progress.Message(idSuffix: "Stopped", message: "Stopped Advertising.");
                Debug.WriteLine("WM, WiFiAdvertiser::Work, shutting down client"); // WM, temporary
                _client.Close();
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
            _currentIpAddress = GetIpAddressOfNetworkIface();
            if (_cachedIpAddress != _currentIpAddress)
            {
                // Race condition: QR advertising thread might grab the advert string before
                // it's complete. If that happens this flag will ensure that the string it gets,
                // via ShareAdvertString(), is recognizably invalid.
                advertStringIsReady = false;

                Debug.WriteLine(
                    "WM, WiFiAdvertiser::UABOCIA, update cached IP addr from "
                        + _cachedIpAddress
                        + " to "
                        + _currentIpAddress
                ); // WM, temporary
                _cachedIpAddress = _currentIpAddress; // save snapshot of our new IP address
                advertisement.title = BookTitle;
                advertisement.version = BookVersion;
                advertisement.language = TitleLanguage;
                advertisement.protocolVersion = WiFiPublisher.ProtocolVersion;
                advertisement.sender = System.Environment.MachineName;
                advertisement.senderIP = _currentIpAddress;
                advertisement.ssid = getSSID();

                // Advert is complete.
                advertStringIsReady = true;

                _sendBytes = Encoding.UTF8.GetBytes(advertisement.ToString());
                //EventLog.WriteEntry("Application", "Serving at http://" + _currentIpAddress + ":" + ChorusHubOptions.MercurialPort, EventLogEntryType.Information);
            }
        }

        /// <summary>
        /// The intent here is to get an IP address by which this computer can be found on the local subnet.
        /// This is ambiguous if the computer has more than one IP address (typically for an Ethernet and WiFi adapter).
        /// Early experiments indicate that things work whichever one is used, assuming the networks are connected.
        /// Eventually we may want to prefer WiFi if available (see code in HearThis), or even broadcast on all of them.
        ///
        /// </summary>
        /// <returns></returns>
        //private string GetLocalIpAddress()
        //{
        //    string localIp = null;
        //    var host = Dns.GetHostEntry(Dns.GetHostName());
        //
        //    foreach (
        //        var ipAddress in host.AddressList.Where(
        //            ipAddress => ipAddress.AddressFamily == AddressFamily.InterNetwork
        //        )
        //    )
        //    {
        //        if (localIp != null)
        //        {
        //            if (host.AddressList.Length > 1)
        //            {
        //                //EventLog.WriteEntry("Application", "Warning: this machine has more than one IP address", EventLogEntryType.Warning);
        //            }
        //        }
        //        localIp = ipAddress.ToString();
        //    }
        //    Debug.WriteLine("WM, WiFiAdvertiser::GetLocalIpAddress, returning localIp = " + localIp); // WM, temporary
        //    return localIp ?? "Could not determine IP Address!";
        //}

        // We need the IP address of *this* (the local) machine. Since a machine running BloomDesktop can have
        // multiple IP addresses (mine has 9, a mix of both IPv4 and IPv6), we must be judicious in selecting the
        // one that will actually be used by network interface. Unfortunately, GetLocalIpAddress() does not always
        // return the correct address.
        // BloomReaderTCPListener.ListenForTCPMessages() implements a mechanism that does. That code provides the
        // basis for this function, and also describes how the mechanism works.
        // This function is static so that other code can use it too (I thought at first that ListenForTCPMessages()
        // could call it, but not so - it needs a more complex return type). Future code, which would be in namespace
        // 'Bloom.Publish.BloomPub.wifi', could call into here.
        public static string GetIpAddressOfNetworkIface()
        {
            IPEndPoint endpoint;
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530); // Google's public DNS service
            endpoint = socket.LocalEndPoint as IPEndPoint;

            Debug.WriteLine(
                "WM, WiFiAdvertiser::GetIpAddressOfNetworkIface, IPv4 address = "
                    + endpoint.Address.ToString()
            ); // WM, temporary
            return endpoint.Address.ToString();
        }

        // Find and return the name of the Wi-Fi network -- i.e., the SSID -- that we are currently
        // connected to. Based on code from
        // https://www.iditect.com/program-example/how-to-get-currently-connected-wifi-ssid-in-c-using-wmi-or-systemnetnetworkinformation-windows-10.html
        // Note that if we are on a wired ethernet connection there won't be an SSID, and we will
        // return an empty string.
        private string getSSID()
        {
            string ssid = "";

            //try
            //{
            //    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            //    {
            //        if (
            //            nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
            //            && nic.OperationalStatus == OperationalStatus.Up
            //        )
            //        {
            //            //IPInterfaceProperties ipProps = nic.GetIPProperties();
            //            //ipProps.
            //            ssid = nic.GetIPProperties()
            //                .UnicastAddresses.FirstOrDefault()
            //                ?.Address.ToString(); // no: this returns the IPv4 address, not the SSID
            //            //nic.Name = "Wi-Fi"
            //            //nic.Description = "Intel(R) Dual Band Wireless-AC 8265"
            //            Debug.WriteLine("WM, WiFiAdvertiser::getSSID, ssid = " + ssid);
            //            Debug.WriteLine("WM, WiFiAdvertiser::getSSID, nic.Name = " + nic.Name);
            //            Debug.WriteLine(
            //                "WM, WiFiAdvertiser::getSSID, nic.Description = " + nic.Description
            //            );
            //
            //            break;
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine("WiFiAdvertiser::getSSID, Error: " + ex.Message);
            //}
            //int i = 1;
            //try
            //{
            //    ManagementObjectSearcher searcher = new ManagementObjectSearcher(
            //        "root\\WMI",
            //        "SELECT * FROM MSNdis_80211_ServiceSetIdentifier"
            //    );
            //    ManagementObjectCollection objCollection = searcher.Get();
            //
            //    foreach (ManagementObject obj in objCollection)
            //    //foreach (ManagementObject obj in searcher.Get())
            //    {
            //        //    byte[] ssidBytes = (byte[])obj["Ndis80211SsId"];
            //        //    //-- no, something in this try block raises exception "Not supported"
            //        //
            //        //    // Convert byte array to string
            //        //    ssid = System.Text.Encoding.ASCII.GetString(ssidBytes).Trim('\0');
            //        //    Debug.WriteLine("WM, WiFiAdvertiser::getSSID, SSID = " + ssid); // WM, temporary
            //        //    break; // Get the first SSID found (usually the currently connected one)
            //
            //        Debug.WriteLine("WM, WiFiAdvertiser::getSSID, i = " + i++); // WM, temporary
            //    }
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine("WiFiAdvertiser::getSSID, Error: " + ex.Message);
            //}
            //
            //Debug.WriteLine("WM, WiFiAdvertiser::getSSID, done: i = " + i); // WM, temporary

            //wlan = new WlanClient();

            // Based on code from:
            // https: //stackoverflow.com/questions/39346232/how-to-get-currently-connected-wifi-ssid-in-c-sharp-using-wmi-or-system-net-netw
            Debug.WriteLine("WM, WiFiAdvertiser::getSSID, creating netsh process");
            // TODO: want to start this process without also opening a UI dialog, if possible...
            System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo.FileName = "netsh.exe";
            p.StartInfo.Arguments = "wlan show interfaces";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.Start();
            Debug.WriteLine("WM, WiFiAdvertiser::getSSID, started netsh process");

            string s = p.StandardOutput.ReadToEnd();
            Debug.WriteLine("WM, WiFiAdvertiser::getSSID, did ReadToEnd()");
            if (s != "")
            {
                Debug.WriteLine("WM, WiFiAdvertiser::getSSID, indexing for SSID");
                if (s.IndexOf("SSID") != -1)
                {
                    ssid = s.Substring(s.IndexOf("SSID"));
                    ssid = ssid.Substring(ssid.IndexOf(":"));
                    ssid = ssid.Substring(2, ssid.IndexOf("\n")).Trim();
                    Debug.WriteLine("WM, WiFiAdvertiser::getSSID, ssid = " + ssid);
                }
                else
                {
                    Debug.WriteLine("WM, WiFiAdvertiser::getSSID, ssid is empty");
                }

                Debug.WriteLine("WM, WiFiAdvertiser::getSSID, indexing for Signal");
                if (s.IndexOf("Signal") != -1)
                {
                    string sig = s.Substring(s.IndexOf("Signal"));
                    sig = sig.Substring(sig.IndexOf(":"));
                    sig = sig.Substring(2, sig.IndexOf("\n")).Trim();
                    Debug.WriteLine("WM, WiFiAdvertiser::getSSID, signal = " + sig);
                }
                else
                {
                    Debug.WriteLine("WM, WiFiAdvertiser::getSSID, signal is empty");
                }
            }
            else
            {
                Debug.WriteLine("WM, WiFiAdvertiser::getSSID, no wlan info available");
            }
            p.WaitForExit();
            Debug.WriteLine("WM, WiFiAdvertiser::getSSID, netsh process exited");

            return ssid;
        }

        public void Stop()
        {
            if (_thread == null)
                return;

            //EventLog.WriteEntry("Application", "Advertiser Stopping...", EventLogEntryType.Information);
            Debug.WriteLine("WM, WiFiAdvertiser::Stop, work thread stop"); // WM, temporary
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
