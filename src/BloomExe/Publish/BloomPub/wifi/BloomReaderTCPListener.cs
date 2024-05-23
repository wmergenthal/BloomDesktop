using System;
using System.Diagnostics;
using System.Drawing.Text;
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
        // WM: we should be able to use same port here for TCP as UDP uses.
        //
        // ListenForTCPMessages() is based on example TCP *server* code from:
        //     https://www.geeksforgeeks.org/socket-programming-in-c-sharp/
        // This page also has an example TCP *client*, which benefits Bloom Reader.

        private int _portToListen = 5915;
        Thread _listeningThread;
        public event EventHandler<AndroidMessageArgs> NewMessageReceived;
        private bool _listening;
        private int _advertMaxLengthExpected = 512;  // WM, more than enough, yes?

        // constructor: starts listening.
        public BloomReaderTCPListener()
        {
            Debug.WriteLine("WM, BloomReaderTCPListener, creating thread"); // WM, temporary
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
            // Creating local endpoint requires the IP address of *this* (the local) machine.
            // Since a machine running BloomDesktop can have multiple IP addresses (mine has 9,
            // a mix of both IPv4 and IPv6), we must be judicious in selecting the one that will
            // actually get used by network interface. A post at
            // https://stackoverflow.com/questions/6803073/get-local-ip-address/27376368#27376368
            // shows a way to do that:
            //   "Connect a UDP socket and read its local endpoint.
            //    Connect on a UDP socket has the following effect: it sets the destination for
            //    Send/Recv, discards all packets from other addresses, and - which is what we use -
            //    transfers the socket into "connected" state, settings its appropriate fields. This
            //    includes checking the existence of the route to the destination according to the
            //    system's routing table and setting the local endpoint accordingly. The last part
            //    seems to be undocumented officially but it looks like an integral trait of Berkeley
            //    sockets API (a side effect of UDP "connected" state) that works reliably in both
            //    Windows and Linux across versions and distributions.
            //    So, this method will give the local address that would be used to connect to the
            //    specified remote host. There is no real connection established, hence the specified
            //    remote ip can be unreachable."
            IPEndPoint endpoint;
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);  // Google's public DNS service
            endpoint = socket.LocalEndPoint as IPEndPoint;

            // Now can create the local endpoint and socket, using the tested IP address.
            Debug.WriteLine("WM, TCP-listener, creating listener on " + endpoint.Address.ToString()); // WM, temporary
            IPEndPoint localEndPoint = new IPEndPoint(endpoint.Address, _portToListen);
            Socket listener = new Socket(endpoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                // Bind the network address to the socket.
                listener.Bind(localEndPoint);

                // Put socket in listening mode. Max number of pending connections = 10 (too much?).
                // This is a non-blocking call.
                listener.Listen(10);

                // DEBUG ONLY:: maintain an incrementing ID of received messages so they can be
                // distinguished from one another (otherwise scrolling console text looks identical).
                int incomingMsgId = 1;

                // Create buffer to receive message.
                byte[] inBuf = new Byte[_advertMaxLengthExpected];

                while (_listening)
                {
                    Debug.WriteLine("WM, TCP-listener, waiting for connection..."); // WM, temporary

                    // Create socket and wait for new connection. Bind() and Listen()
                    // must have previously been called. This is a blocking call.
                    Socket clientSocket = listener.Accept();

                    Debug.WriteLine("WM, TCP-listener, connection started"); // WM, temporary
                    // Got connection. Receive incoming message. 'inLen' tells how long it is.
                    int inLen = clientSocket.Receive(inBuf);

                    // DEBUG ONLY: convert incoming raw bytes to ASCII, then display.
                    var inBufString = Encoding.ASCII.GetString(inBuf, 0, inLen);
                    Debug.WriteLine("WM, TCP-listener, message {0} from Reader:", incomingMsgId++);
                    Debug.WriteLine("   msg = " + inBufString.Substring(0, inLen));

                    // Raise event for WiFiPublisher to notice and act on -- this is what
                    // actually gets the book sent to Reader.
                    Debug.WriteLine("WM, TCP-listener, got {0} bytes from Reader, raising \'NewMessageReceived\'", inLen); // WM, temporary
                    NewMessageReceived?.Invoke(this, new AndroidMessageArgs(inBuf));

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
