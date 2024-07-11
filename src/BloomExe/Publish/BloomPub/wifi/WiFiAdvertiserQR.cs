using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using ZXing;
//using System.Drawing.Imaging;
//using WiFiAdvertiser;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// This class generates and displays a QR code offering to supply a book to any Android that wants it.
    /// </summary>
    public class WiFiAdvertiserQR : IDisposable
    {
        // The information that BloomDesktop will advertise, via both
        // UDP broadcast (a different class) and QR code (this class).
        // Initialize it with nonsense to prevent the ZXing lib from
        // falling over trying to create a QR code from an empty string.
        public string AdvertToShowAsQrCode = "initial-string-so-ZXing-doesnt-see-null";

        private Thread _thread;

        internal WiFiAdvertiserQR()
        {
            //_progress = progress;
        }

        public void Start()
        {
            _thread = new Thread(Work);
            Debug.WriteLine("WM, WiFiAdvertiserQR::Start, work thread start"); // WM, temporary
            _thread.Start();
        }

        //public bool Paused { get; set; }

        public void SetAdvertString(string input)
        {
            Debug.WriteLine("WM, WiFiAdvertiserQR::SetAdvertString, called with " + input); // WM, temporary
            AdvertToShowAsQrCode = input;
        }

        private void generateAndDisplayQR()
        {
            // Generate QR code with the same data sent out in the UDP broadcast.
            // 
            // This code based on AndroidSyncDialog.cs in HearThis.
            // HearThis uses 'ZXing' so we do too, which requires a 'using'
            // statement above and adding package 'ZXing.Net' to BloomExe.csproj.
            var qrBox = new PictureBox();
            qrBox.Height = 225;  // tweak as desired
            qrBox.Width = 225;   // tweak as desired
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options =
                {
                    Height = qrBox.Height,
                    Width = qrBox.Width
                }
            };

            Debug.WriteLine("WM, WiFiAdvertiserQR::generateAndDisplayQR, generating QR with " + AdvertToShowAsQrCode); // WM, temporary
            var matrix = writer.Write(AdvertToShowAsQrCode);
            var qrBitmap = new Bitmap(matrix);
            qrBox.Image = qrBitmap;
            qrBox.Dock = DockStyle.Fill;
            Debug.WriteLine("WM, WiFiAdvertiserQR::generateAndDisplayQR, QR code ready, now display it"); // WM, temporary

            // QR code is created; now display it.
            Form form = new Form();
            form.Text = "Scan this QR code with BloomReader";
            form.Controls.Add(qrBox);
            Application.Run(form);
        }

        private void Work()
        {
            Debug.WriteLine("WM, WiFiAdvertiserQR::Work, begin QR advertising loop"); // WM, temporary
            try
            {
                while (true)
                {
                    generateAndDisplayQR();
                    Debug.WriteLine("WM, WiFiAdvertiserQR::Work, 1-second sleep"); // WM, temporary
                    Thread.Sleep(1000);
                }
            }
            catch (ThreadAbortException)
            {
                Debug.WriteLine("WM, WiFiAdvertiserQR::Work, ThreadAbortException");
            }
            catch (Exception e)
            {
                //Debug.WriteLine("WM, WiFiAdvertiserQR::Work, exception: {0}", e.ToString());
                Debug.WriteLine("WM, WiFiAdvertiserQR::Work, exception: " + e.ToString());
            }

            Debug.WriteLine("WM, WiFiAdvertiserQR::Work, exited QR advertising loop, NOT GOOD"); // WM, temporary
        }

        public static void SendCallback(IAsyncResult args) { }   // WHAT IS THIS FOR? REMOVE?

        public void Stop()
        {
            if (_thread == null)
                return;

            Debug.WriteLine("WM, WiFiAdvertiserQR::Stop, work thread stop"); // WM, temporary
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
