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
            Debug.WriteLine("WM, BloomReaderUDPListener, creating thread"); // WM, temporary
            _listeningThread = new Thread(ListenForUDPPackages);
            _listeningThread.IsBackground = true;
            Debug.WriteLine("WM, BloomReaderUDPListener, starting thread"); // WM, temporary
            _listeningThread.Start();
            _listening = true;
        }

        /// <summary>
        /// Run on a background thread; returns only when done listening.
        /// </summary>
        public void ListenForUDPPackages()
        {
            try
            {
                _listener = new UdpClient(_portToListen);
            }
            catch (SocketException e)
            {
                //log then do nothing
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
            }

            if (_listener != null)
            {
                IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 0);

                while (_listening)
                {
                    try
                    {
                        Debug.WriteLine("WM, UDP-listener, waiting for packet..."); // WM, temporary
                        byte[] bytes = _listener.Receive(ref groupEP); // waits for packet from Android.

                        //raise event
                        Debug.WriteLine("WM, UDP-listener, got {0} bytes from Reader, raising \'NewMessageReceived\'", bytes.Length); // WM, temporary
                        NewMessageReceived?.Invoke(this, new AndroidMessageArgs(bytes));
                    }
                    catch (SocketException se)
                    {
                        if (!_listening || se.SocketErrorCode == SocketError.Interrupted)
                            return; // no problem, we're just closing up shop
                        throw se;
                    }
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
                Debug.WriteLine("WM, UDP-listener, connection closed"); // WM, temporary
            }

            if (_listeningThread == null) {
                Debug.WriteLine("WM, UDP-StopListener, _listeningThread null, bail"); // WM, temporary
                return;
            }

            // Since we told the listener to close already this shouldn't have to do much (nor be dangerous)
            Debug.WriteLine("WM, UDP-StopListener, stopping and deleting _listeningThread"); // WM, temporary
            _listeningThread.Abort();
            _listeningThread.Join(2 * 1000);
            _listeningThread = null;
        }
    }
}
