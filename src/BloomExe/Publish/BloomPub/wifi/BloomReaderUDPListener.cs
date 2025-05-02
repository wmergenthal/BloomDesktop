using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Bloom.Publish.BloomPUB.wifi;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// Helper class to listen for a single packet from the Android. Construct an instance to start
    /// listening (on another thread); hook NewMessageReceived to receive a packet each time a client sends it.
    /// </summary>
    class BloomReaderUDPListener
    {
        // must match BloomReader.NewBookListenerService.desktopPort
        // and be different from WiFiAdvertiser.Port and port in BloomReaderPublisher.SendBookToWiFi
        private int _portToListen = 5915;
        Thread _listeningThread;
        public event EventHandler<AndroidMessageArgs> NewMessageReceived;
        UdpClient _listener = null;
        private bool _listening;

        //constructor: starts listening.
        public BloomReaderUDPListener()
        {
            Debug.WriteLine("WM, UDPListener, creating thread"); // WM, temporary
            _listeningThread = new Thread(ListenForUDPPackages);
            _listeningThread.IsBackground = true;
            Debug.WriteLine("WM, UDPListener, starting thread"); // WM, temporary
            _listeningThread.Start();
            _listening = true;
        }

        /// <summary>
        /// Run on a background thread; returns only when done listening.
        /// </summary>
        public void ListenForUDPPackages()
        {
            // WM, since this is receive only we can use UdpClient, but I think we are using the
            // wrong constructor:
            // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.-ctor?view=net-9.0
            // "This constructor creates an underlying Socket and binds it to the port number from
            // which you intend to communicate. Use this constructor if you are only interested in
            // setting the local port number. The underlying service provider will assign the local
            // IP address." -- And, as I have seen, the underlying service provider sometimes assigns
            // the wrong one.
            //
            // This seems better:  _listener = new UdpClient(new IPEndPoint(IPAddress.Any, somePort));

            IPEndPoint groupEP = null;

            try
            {
                Debug.WriteLine("WM, UDPListener, creating IPEndPoint with IPAddress.Any, port " + _portToListen); // WM, temporary
                groupEP = new IPEndPoint(IPAddress.Any, _portToListen);

                if (groupEP == null)
                {
                    Debug.WriteLine("UDPListener, ERROR creating IPEndPoint, bail");
                    return;
                }

                // The local endpoint has been created on the port that BloomReader will
                // respond to. And the endpoint's address IPAddress.Any means that *all*
                // network interfaces on this machine will be monitored for UDP packets
                // sent to the designated port.
                Debug.WriteLine("WM, UDPListener, creating UdpClient"); // WM, temporary
                _listener = new UdpClient(groupEP);

                if (_listener == null)
                {
                    Debug.WriteLine("UDPListener, ERROR creating UdpClient, bail");
                    return;
                }
            }
            catch (SocketException e)
            {
                //log then do nothing
                Debug.WriteLine("UDPListener, SocketException-1 = " + e);
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
            }

            while (_listening)
            {
                try
                {
                    Debug.WriteLine("WM, UDPListener, waiting for packet"); // WM, temporary
                    byte[] bytes = _listener.Receive(ref groupEP); // waits for packet from Android.

                    // WM, debug only, temporary
                    if (_listener?.Client?.LocalEndPoint is IPEndPoint localEP)
                    {
                        Debug.WriteLine("WM, UDPListener, listening on {0}, port {1}", localEP.Address, localEP.Port);
                    }
                    // WM, end debug

                    Debug.WriteLine("WM, UDPListener, got {0} bytes, raising \'NewMessageReceived\'", bytes.Length); // WM, temporary
                    //raise event
                    NewMessageReceived?.Invoke(this, new AndroidMessageArgs(bytes));
                }
                catch (SocketException se)
                {
                    Debug.WriteLine("UDPListener, SocketException-2 = " + se);
                    if (!_listening || se.SocketErrorCode == SocketError.Interrupted)
                    {
                        return; // no problem, we're just closing up shop
                    }
                    throw se;
                }
            }
        }

        public void StopListener()
        {
            Debug.WriteLine("WM, UDP-StopListener, called"); // WM, temporary
            if (_listening)
            {
                _listening = false;
                _listener?.Close(); // forcibly end communication
                _listener = null;
                Debug.WriteLine("WM, UDP-StopListener, closed"); // WM, temporary
            }

            if (_listeningThread == null)
            {
                Debug.WriteLine("WM, UDP-StopListener, null thread, return"); // WM, temporary
                return;
            }

            // Since we told the listener to close already this shouldn't have to do much (nor be dangerous)
            Debug.WriteLine("WM, UDP-StopListener, stopping and deleting thread"); // WM, temporary
            _listeningThread.Abort();
            _listeningThread.Join(2 * 1000);
            _listeningThread = null;
        }
    }
}
