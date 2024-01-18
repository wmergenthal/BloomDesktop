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
    /// listening (on another thread);
    /// ??? hook NewMessageReceived to receive a packet each time a client sends it. ???
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
        //private int _portToListen = 80;  // WM, temporarily try standard HTTP port
        Thread _listeningThread;
        public event EventHandler<AndroidMessageArgs> NewMessageReceived;
        //UdpClient _listener = null;
        //TcpClient _listener = null;  // WM, try sockets first? may end up using this...
        private bool _listening;

        // constructor: starts listening.
        public BloomReaderTCPListener()
        {
            Debug.WriteLine("WM, BloomReaderTCPListener, creating thread");
            //_listeningThread = new Thread(ListenForUDPPackages);
            _listeningThread = new Thread(ListenForTCPMessages);
            _listeningThread.IsBackground = true;
            Debug.WriteLine("WM, BloomReaderTCPListener, starting thread");
            _listeningThread.Start();
            _listening = true;
        }

        /// <summary>
        /// Run on a background thread; returns only when done listening.
        /// </summary>
        //public void ListenForUDPPackages()
        public void ListenForTCPMessages()
        {
            //try
            //{
            //    _listener = new UdpClient(_portToListen);  // *** HOW TO DO THIS FOR TCP?
            //}
            //catch (SocketException e)
            //{
            //    //log then do nothing
            //    Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
            //}
            //
            //if (_listener == null) {
            //    Debug.WriteLine("ListenForTCPMessages, error (null _listener), returning");
            //    return;
            //}

            // Establish the local endpoint for the socket.
            // Dns.GetHostName returns the name of the host running the application.
            //
            // Question: like in tcp_client.cs, shouldn't this stuff be in a try/catch?
            string hostName = Dns.GetHostName();
            Debug.WriteLine("WM, TCP-listener, hostname = " + hostName);

            //string ip = Dns.GetHostByName(hostName).AddressList[0].ToString(); -- deprecated per VS 2022,
            // replace per https://www.tutorialspoint.com/How-to-display-the-IP-Address-of-the-Machine-using-Chash
            // to list ALL a host's addresses:
            IPHostEntry myHost = Dns.GetHostEntry(hostName);
            IPAddress[] myIpAddresses = myHost.AddressList;
            Debug.WriteLine("WM, TCP-listener, IP address list:");
            for (int i = 0; i < myIpAddresses.Length; i++) {
                Debug.WriteLine("   IP address[" + i + "] = " + myIpAddresses[i].ToString());
            }

            // On the Lenovo T480s, addresses [0]-[5] are IPv6. I want to use ethernet
            // interface's IPv4 address, which happens to be [6] (refer to the generated
            // console output).
            // Yes, of course a hardcoded index like this is not acceptable for real code.
            IPAddress ipAddr = myHost.AddressList[6];

            // Create the endpoint and socket.
            Debug.WriteLine("WM, TCP-listener, creating listener on " + myIpAddresses[6]);
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
                    Debug.WriteLine("WM, TCP-listener, waiting for connection...");

                    // Create socket for newly created connection. Bind() and Listen()
                    // must have previously been called. This is a blocking call.
                    Socket clientSocket = listener.Accept();

                    // Got connection. Create buffer to receive message from the client,
                    // and a string into which the message will be converted.
                    Debug.WriteLine("WM, TCP-listener, connection started");
                    byte[] inBuf = new Byte[1024];

                    // Receive incoming message. 'inLen' tells how long it is.
                    int inLen = clientSocket.Receive(inBuf);

                    // Raise event for WiFiPublisher to notice and act on.
                    // Not sure if this should come before or after ASCII conversion...
                    Debug.WriteLine("WM, TCP-listener, got {0} bytes from Reader, raising NewMessageReceived", inLen);
                    NewMessageReceived?.Invoke(this, new AndroidMessageArgs(inBuf));

                    // Convert incoming raw bytes to ASCII.
                    string inBufString = Encoding.ASCII.GetString(inBuf, 0, inLen);
                    Debug.WriteLine("WM, TCP-listener, message {0} from Reader:", incomingMsgId++);
                    //Debug.WriteLine("  {0} ", inBufString);
                    // ********* following line throws exception,
                    // System.ArgumentOutOfRangeException: startIndex must be less than length of string
                    Debug.WriteLine("  {0}", inBufString.Remove(inLen)); // only show 'inLen' num of chars

                    // Close connection (actual book transfer is done elsewhere, by SyncServer).
                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                    Debug.WriteLine("WM, TCP-listener, connection closed");
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("TCP-listener, Exception : {0}", e.ToString());
            }
        }

        public void StopListener()
        {
            Debug.WriteLine("WM, StopListener, called");
            if (_listening) {
                _listening = false;
                //_listener?.Close(); // forcibly end communication
                //_listener = null;
            }

            if (_listeningThread == null) {
                return;
            }

            // Since we told the listener to close already this shouldn't have to do much (nor be dangerous)
            Debug.WriteLine("WM, StopListener, stopping and deleting thread");
            _listeningThread.Abort();
            _listeningThread.Join(2 * 1000);
            _listeningThread = null;
        }
    }
}
