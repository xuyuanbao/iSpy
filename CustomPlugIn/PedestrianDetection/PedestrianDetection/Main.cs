
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

using OpenCvSharp.Extensions;

using OpenCvSharp;


namespace Plugins
{
    public class Main : IDisposable
    {
        private string _config = "";
        private bool _disposed;
        public string Alert;        
        internal Color MyColor =Color.Red;
        /// <summary>
        /// 颜色
        /// </summary>
        internal Scalar _scalar = Scalar.Red;

        public Bitmap ProcessFrame(Bitmap frame)
        {
            
            Mat mat = BitmapConverter.ToMat(frame);
            var hog = new HOGDescriptor();
            hog.SetSVMDetector(HOGDescriptor.GetDefaultPeopleDetector());

            bool b = hog.CheckDetectorSize();
            Console.WriteLine("CheckDetectorSize: {0}", b);

            // run the detector with default parameters. to get a higher hit-rate
            // (and more false alarms, respectively), decrease the hitThreshold and
            // groupThreshold (set groupThreshold to 0 to turn off the grouping completely).
            Rect[] found = hog.DetectMultiScale(mat, 0, new OpenCvSharp.Size(8, 8), new OpenCvSharp.Size(24, 16), 1.05, 2);


            foreach (Rect rect in found)
            {
                // the HOG detector returns slightly larger rectangles than the real objects.
                // so we slightly shrink the rectangles to get a nicer output.
                var r = new Rect
                {
                    X = rect.X + (int)Math.Round(rect.Width * 0.1),
                    Y = rect.Y + (int)Math.Round(rect.Height * 0.1),
                    Width = (int)Math.Round(rect.Width * 0.8),
                    Height = (int)Math.Round(rect.Height * 0.8)
                };
                mat.Rectangle(r.TopLeft, r.BottomRight,_scalar, 3);
            }
            Bitmap bmp= mat.ToBitmap();
            return bmp;
        }



        #region 接口配置
        public string Configuration
        {
            get { return _config; }
            set
            {
                _config = value;
                InitConfig();
            }
        }

        private void InitConfig()
        {
            if (_config != "")
            {
                string[] cfg = _config.Split('|');
                int iArgb = Convert.ToInt32(cfg[0]);
                MyColor = Color.FromArgb(iArgb);
                _scalar=Scalar.FromRgb(MyColor.R, MyColor.G, MyColor.B);
            }
        }

        public string Configure()
        {
            var cfg = new Configure(this);
            if (cfg.ShowDialog() == DialogResult.OK)
            {
                _config = MyColor.ToArgb().ToString();
                InitConfig();
            }
            return Configuration;
        }

        //Implement IDisposable.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Free other state (managed objects).
                }
                // Free your own state (unmanaged objects).
                // Set large fields to null.
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~Main()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion
    }
}
