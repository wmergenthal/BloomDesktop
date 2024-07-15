using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using ZXing;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// This class generates and displays a QR code offering to supply a book to any Android that wants it.
    /// </summary>
    public class WiFiAdvertiserQR : IDisposable
    {
        private Thread _thread;

        // The information that BloomDesktop will advertise, via both
        // UDP broadcast (a different class) and QR code (this class).
        private string AdvertToShowAsQrCode;

        // Need to call into the other advertiser to get a copy of the data
        // it is advertising. This object reference enables that, and is set
        // at instantiation time.
        private readonly WiFiAdvertiser _wiFiAdvertiserUdp;

        internal WiFiAdvertiserQR(WiFiAdvertiser advertiserUdpObject)
        {
            _wiFiAdvertiserUdp = advertiserUdpObject;
        }

        public void Start()
        {
            _thread = new Thread(Work);
            Debug.WriteLine("WM, WiFiAdvertiserQR::Start, work thread start"); // WM, temporary
            _thread.Start();
        }

        // Generate QR code with the *same data* sent out in the UDP broadcast.
        // 
        // This code based on AndroidSyncDialog.cs in HearThis.
        // HearThis uses 'ZXing' so we do too, which requires a 'using'
        // statement above and adding package 'ZXing.Net' to BloomExe.csproj.
        private void generateAndDisplayQR()
        {
            // Update our local copy of the UDP advert from UDP advertiser.
            // Note: it is possible that UDP advertiser has not yet generated the
            // UDP advert. If so then abort -- we don't want to confuse the user by
            // showing a QR code that contains no data. We're in a loop here so we
            // will get the advert data when UDP advertiser eventually produces it.
            // 
            // QUESTION: instead of determining that an advert is empty by looking for
            // "{}", should we look for a string length that is too small to be valid?
            AdvertToShowAsQrCode = _wiFiAdvertiserUdp.GetAdvertString();
            if (AdvertToShowAsQrCode == "{}")
            {
                Debug.WriteLine("WM, WiFiAdvertiserQR::generateAndDisplayQR, no advert yet, bail");
                return;
            }

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
                Debug.WriteLine("WM, WiFiAdvertiserQR::Work, exception: " + e.ToString());
            }

            Debug.WriteLine("WM, WiFiAdvertiserQR::Work, exited QR advertising loop, NOT GOOD"); // WM, temporary
        }

        public static void SendCallback(IAsyncResult args) { }   // WHAT IS THIS FOR? CAN REMOVE?

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
