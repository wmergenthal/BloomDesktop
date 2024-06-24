using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Bloom.Api;
using Bloom.web;
//using SIL.Progress;

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
        private string _cachedIpAddress;
        private byte[] _sendBytes; // Data we send in each advertisement packet
        private readonly WebSocketProgress _progress;

        internal WiFiAdvertiser(WebSocketProgress progress)
        {
            _progress = progress;
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
                        Debug.WriteLine("WM, WiFiAdvertiser::Work, broadcasting advert ({0} bytes) on port {1}", _sendBytes.Length, Port); // WM, temporary
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
            Debug.WriteLine("WM, WiFiAdvertiser::UABOCIA, begin, _cachedIpAddress = " + _cachedIpAddress); // WM, temporary
            _currentIpAddress = GetIpAddressOfNetworkIface();
            if (_cachedIpAddress != _currentIpAddress)
            {
                _cachedIpAddress = _currentIpAddress;   // save snapshot of our new IP address
                dynamic advertisement = new DynamicJson();
                advertisement.title = BookTitle;
                advertisement.version = BookVersion;
                advertisement.language = TitleLanguage;
                advertisement.protocolVersion = WiFiPublisher.ProtocolVersion;
                advertisement.sender = System.Environment.MachineName;

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
        private string GetLocalIpAddress()
        {
            string localIp = null;
            var host = Dns.GetHostEntry(Dns.GetHostName());

            foreach (
                var ipAddress in host.AddressList.Where(
                    ipAddress => ipAddress.AddressFamily == AddressFamily.InterNetwork
                )
            )
            {
                if (localIp != null)
                {
                    if (host.AddressList.Length > 1)
                    {
                        //EventLog.WriteEntry("Application", "Warning: this machine has more than one IP address", EventLogEntryType.Warning);
                    }
                }
                localIp = ipAddress.ToString();
            }
            Debug.WriteLine("WM, WiFiAdvertiser::GetLocalIpAddress, returning localIp = " + localIp); // WM, temporary
            return localIp ?? "Could not determine IP Address!";
        }

        // We need the IP address of *this* (the local) machine. Since a machine running BloomDesktop can have
        // multiple IP addresses (mine has 9, a mix of both IPv4 and IPv6), we must be judicious in selecting the
        // one that will actually be used by network interface. Unfortunately, GetLocalIpAddress() does not always
        // return the correct address.
        // BloomReaderTCPListener.ListenForTCPMessages() implements a mechanims that does. That code provides the
        // basis for this function, and also describes how the mechanism works.
        // This function is static so that other code can use it too (I thought at first that ListenForTCPMessages()
        // could call it, but not so - it needs a more complex return type). Future code, which must be in namespace
        // 'Bloom.Publish.BloomPub.wifi', could call into here like this:
        //      string ipAddr = WiFiAdvertiser.GetIpAddressOfNetworkIface();
        public static string GetIpAddressOfNetworkIface()
        {
            IPEndPoint endpoint;
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);  // Google's public DNS service
            endpoint = socket.LocalEndPoint as IPEndPoint;

            Debug.WriteLine("WM, WiFiAdvertiser::GetIpAddressOfNetworkIface, IPv4 address = " + endpoint.Address.ToString()); // WM, temporary
            return endpoint.Address.ToString();
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
