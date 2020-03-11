using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Plugins
{
    public class Main: IDisposable
    {
        private string _config="";
        private bool _disposed;
        internal int LineWidth = 1;
        public string Alert;

        public Bitmap ProcessFrame(Bitmap frame)
        {
            var g = Graphics.FromImage(frame);
            var p = new Pen(Color.Green) { Width = LineWidth };
            if (LineWidth == 2 && Alert != null)
                p.Color = Color.Red;
            g.DrawLine(p,0,0,frame.Width,frame.Height);
            
            p.Dispose();
            g.Dispose();
            Alert = LineWidth == 2 ? "Line is 2px!" : "";

            return frame;
        }

        public string Configuration
        {
            get { return _config; }
            set { _config = value;
                InitConfig();
            }
        }

        private void InitConfig()
        {
            if (_config!="")
            {
                string[] cfg = _config.Split('|');
                LineWidth = Convert.ToInt32(cfg[0]);
            }
        }

        public string Configure()
        {
            var cfg = new Configure(this);
            if (cfg.ShowDialog()==DialogResult.OK)
            {
                _config = LineWidth.ToString();

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
            Dispose (false);
        }

    }
}
