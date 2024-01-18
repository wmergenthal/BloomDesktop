using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Bloom.Publish.BloomPUB.wifi;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// Helper class to listen for a TCP reply from the Android. Construct an instance to start
    /// listening (on another thread). When a reply is detected, raise event NewMessageReceived
    /// to process it.
    /// </summary>
    class BloomReaderTCPListener
    {
        // must match BloomReader.NewBookListenerService.desktopPort
        // and be different from WiFiAdvertiser.Port and port in BloomReaderPublisher.SendBookToWiFi
        //
        // WM: use same port (5915) here for TCP as UDP uses. Can't think of a reason not to
        // since that UDP channel is not used...
        // ListenForTCPMessages() is based on example TCP *server* code from:
        //     https://www.geeksforgeeks.org/socket-programming-in-c-sharp/
        // This same page also has an example TCP *client*, which will benefit Bloom Reader.

        private int _portToListen = 5915;
        Thread _listeningThread;
        public event EventHandler<AndroidMessageArgs> NewMessageReceived;
        private bool _listening;
        private int _advertMaxLengthExpected = 512;  // WM, more than enough, yes?

        // constructor: starts listening.
        public BloomReaderTCPListener()
        {
            Debug.WriteLine("WM, BloomReaderTCPListener, creating thread"); // WM, temporary
            //_listeningThread = new Thread(ListenForUDPPackages);
            _listeningThread = new Thread(ListenForTCPMessages);
            _listeningThread.IsBackground = true;
            Debug.WriteLine("WM, BloomReaderTCPListener, starting thread"); // WM, temporary
            _listeningThread.Start();
            _listening = true;
        }

        /// <summary>
        /// Run on a background thread; returns only when done listening.
        /// </summary>
        public void ListenForTCPMessages()
        {
            // Establish the local endpoint for the socket.
            //
            // Question: should some of this stuff be in a try/catch?
            string hostName = Dns.GetHostName();  // get name of host running this app
            Debug.WriteLine("WM, TCP-listener, hostname = " + hostName);

            //string ip = Dns.GetHostByName(hostName).AddressList[0].ToString(); -- deprecated per VS 2022,
            // replace per https://www.tutorialspoint.com/How-to-display-the-IP-Address-of-the-Machine-using-Chash
            // to list ALL a host's addresses:
            IPHostEntry myHost = Dns.GetHostEntry(hostName);
            IPAddress[] myIpAddresses = myHost.AddressList;

            // For debug only.
            Debug.WriteLine("WM, TCP-listener, IP address list:");
            for (int i = 0; i < myIpAddresses.Length; i++) {
                Debug.WriteLine("   IP address[" + i + "] = " + myIpAddresses[i].ToString());
            }

            // On the Lenovo T480s, addresses [0]-[5] are IPv6. I want to use ethernet
            // interface's IPv4 address, which happens to be [6] (refer to the generated
            // console output).
            // Yes, of course a hardcoded index like this is not acceptable for real code.
            // Will implement something better.
            IPAddress ipAddr = myHost.AddressList[6];

            // Create the endpoint and socket.
            Debug.WriteLine("WM, TCP-listener, creating listener on " + myIpAddresses[6]); // WM, temporary
            IPEndPoint localEndPoint = new IPEndPoint(ipAddr, _portToListen);
            Socket listener = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                // Associate a network address with the socket.
                listener.Bind(localEndPoint);

                // Put socket in listening mode. Max number of pending connections = 10 (too much?).
                // This is a non-blocking call.
                listener.Listen(10);

                // Maintain an incrementing ID of received messages so they can be distinguished
                // from one another (otherwise scrolling console text looks identical).
                int incomingMsgId = 1;

                while (true)
                {
                    Debug.WriteLine("WM, TCP-listener, waiting for connection..."); // WM, temporary

                    // Create socket for newly created connection. Bind() and Listen()
                    // must have previously been called. This is a blocking call.
                    Socket clientSocket = listener.Accept();

                    // Got connection. Create buffer to receive message from the client.
                    Debug.WriteLine("WM, TCP-listener, connection started"); // WM, temporary
                    byte[] inBuf = new Byte[_advertMaxLengthExpected];

                    // Receive incoming message. 'inLen' tells how long it is.
                    int inLen = clientSocket.Receive(inBuf);

                    // For debug only: convert incoming raw bytes to ASCII.
                    //string inBufString = Encoding.ASCII.GetString(inBuf, 0, inLen);
                    var inBufString = Encoding.ASCII.GetString(inBuf, 0, inLen);

                    // Raise event for WiFiPublisher to notice and act on -- this is what
                    // actually gets the book sent to Reader.
                    Debug.WriteLine("WM, TCP-listener, got {0} bytes from Reader, raising NewMessageReceived", inLen); // WM, temporary
                    NewMessageReceived?.Invoke(this, new AndroidMessageArgs(inBuf));

                    // Debug only: show the request from Reader (which is 'inLen' bytes long).
                    Debug.WriteLine("WM, TCP-listener, message {0} from Reader:", incomingMsgId++);
                    Debug.WriteLine("   msg=" + inBufString.Substring(0, inLen));

                    // Close connection (actual book transfer is done elsewhere, by SyncServer).
                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                    Debug.WriteLine("WM, TCP-listener, connection closed"); // WM, temporary
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("TCP-listener, Exception : {0}", e.ToString());
            }
        }

        public void StopListener()
        {
            Debug.WriteLine("WM, StopListener, called"); // WM, temporary
            if (_listening) {
                _listening = false;
            }

            if (_listeningThread == null) {
                Debug.WriteLine("WM, StopListener, _listeningThread null, bail"); // WM, temporary
                return;
            }

            Debug.WriteLine("WM, StopListener, stopping and deleting _listeningThread"); // WM, temporary
            _listeningThread.Abort();
            _listeningThread.Join(2 * 1000);
            _listeningThread = null;
        }
    }
}
