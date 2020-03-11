using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using iSpyApplication.Controls;
using iSpyApplication.Utilities;
using System.Text;


namespace iSpyApplication.Sources.Video
{
    internal class ESP32CamStream : VideoBase, IVideoSource
    {
        // buffer size used to download JPEG image
        private const int BufferSize = 1024 * 1024;
        // size of portion to read at once
        private const int ReadSize = 1024;
        private ManualResetEvent _abort;

        /// <summary>
        ///     Login value.
        /// </summary>
        /// <remarks>Login required to access video source.</remarks>
        private readonly string _login, _password, _cookies, _httpUserAgent, _headers;

        private readonly int _requestTimeout;

        // URL for JPEG files
        private readonly objectsCamera _source;

        private readonly bool _useHttp10;

        private bool _disposed;

        private ReasonToFinishPlaying _res = ReasonToFinishPlaying.DeviceLost;
        private Thread _thread;

               
        // magic 2 byte header for JPEG images
        private byte[] JpegHeader = new byte[] { 0xff, 0xd8 };
        

        /// <summary>
        ///     Initializes a new instance of the <see cref="JpegStream" /> class.
        /// </summary>
        /// <param name="source">URL, which provides JPEG files.</param>
        public ESP32CamStream(CameraWindow source) : base(source)
        {
            _source = source.Camobject;
            var ckies = _source.settings.cookies ?? "";
            ckies = ckies.Replace("[USERNAME]", _source.settings.login);
            ckies = ckies.Replace("[PASSWORD]", _source.settings.password);
            ckies = ckies.Replace("[CHANNEL]", _source.settings.ptzchannel);

            var hdrs = _source.settings.headers ?? "";
            hdrs = hdrs.Replace("[USERNAME]", _source.settings.login);
            hdrs = hdrs.Replace("[PASSWORD]", _source.settings.password);
            hdrs = hdrs.Replace("[CHANNEL]", _source.settings.ptzchannel);

            _login = _source.settings.login;
            _password = _source.settings.password;
            _requestTimeout = _source.settings.timeout;
            _useHttp10 = _source.settings.usehttp10;
            _httpUserAgent = _source.settings.useragent;
            _cookies = ckies;
            _headers = hdrs;
        }

        /// <summary>
        ///     Use or not separate connection group.
        /// </summary>
        /// <remarks>The property indicates to open web request in separate connection group.</remarks>
        public bool SeparateConnectionGroup { get; set; }

        /// <summary>
        ///     Gets or sets proxy information for the request.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The local computer or application config file may specify that a default
        ///         proxy to be used. If the Proxy property is specified, then the proxy settings from the Proxy
        ///         property overridea the local computer or application config file and the instance will use
        ///         the proxy settings specified. If no proxy is specified in a config file
        ///         and the Proxy property is unspecified, the request uses the proxy settings
        ///         inherited from Internet Explorer on the local computer. If there are no proxy settings
        ///         in Internet Explorer, the request is sent directly to the server.
        ///     </para>
        /// </remarks>
        public IWebProxy Proxy { get; set; }

        // Public implementation of Dispose pattern callable by consumers. 
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        ///     New frame event.
        /// </summary>
        /// <remarks>
        ///     <para>Notifies clients about new available frame from video source.</para>
        ///     <para>
        ///         <note>
        ///             Since video source may have multiple clients, each client is responsible for
        ///             making a copy (cloning) of the passed video frame, because the video source disposes its
        ///             own original copy after notifying of clients.
        ///         </note>
        ///     </para>
        /// </remarks>
        public event NewFrameEventHandler NewFrame;

        /// <summary>
        ///     Video playing finished event.
        /// </summary>
        /// <remarks>
        ///     <para>This event is used to notify clients that the video playing has finished.</para>
        /// </remarks>
        public event PlayingFinishedEventHandler PlayingFinished;


        /// <summary>
        ///     Video source.
        /// </summary>
        /// <remarks>URL, which provides JPEG files.</remarks>
        public virtual string Source
        {
            get { return _source.settings.videosourcestring; }
            set { _source.settings.videosourcestring = value; }
        }

        /// <summary>
        ///     State of the video source.
        /// </summary>
        /// <remarks>Current state of video source object - running or not.</remarks>
        public bool IsRunning
        {
            get
            {
                if (_thread == null)
                    return false;

                try
                {
                    return !_thread.Join(TimeSpan.Zero);
                }
                catch
                {
                    return true;
                }
            }
        }

        /// <summary>
        ///     Start video source.
        /// </summary>
        /// <remarks>
        ///     Starts video source and return execution to caller. Video source
        ///     object creates background thread and notifies about new frames with the
        ///     help of <see cref="NewFrame" /> event.
        /// </remarks>
        /// <exception cref="ArgumentException">Video source is not specified.</exception>
        public void Start()
        {
            if (!IsRunning)
            {
                // check source
                if (string.IsNullOrEmpty(_source.settings.videosourcestring))
                    throw new ArgumentException("Video source is not specified.");

                _res = ReasonToFinishPlaying.DeviceLost;

                // create and start new thread
                _thread = new Thread(WorkerThread2) { Name = _source.settings.videosourcestring, IsBackground = true };
                _thread.Start();
            }
        }

        public void Restart()
        {
            if (!IsRunning) return;
            _res = ReasonToFinishPlaying.Restart;
            _abort?.Set();
        }


        public void Stop()
        {
            if (IsRunning)
            {
                _res = ReasonToFinishPlaying.StoppedByUser;
                _abort?.Set();
            }
            else
            {
                _res = ReasonToFinishPlaying.StoppedByUser;
                PlayingFinished?.Invoke(this, new PlayingFinishedEventArgs(_res));
                _abort?.Set();
            }
        }

        // Worker thread
        private void WorkerThread()
        {
            _abort = new ManualResetEvent(false);
            // buffer to read stream
            var buffer = new byte[BufferSize];
            // HTTP web request
            HttpWebRequest request = null;
            // web responce
            WebResponse response = null;
            // stream for JPEG downloading
            Stream stream = null;
            // random generator to add fake parameter for cache preventing
            var rand = new Random((int)DateTime.UtcNow.Ticks);
            // download start time and duration
            var err = 0;
            var connectionFactory = new ConnectionFactory();
            while (!_abort.WaitOne(10) && !MainForm.ShuttingDown)
            {
                var total = 0;
                if (ShouldEmitFrame)
                {
                    try
                    {
                        // set download start time
                        var start = DateTime.UtcNow;
                        var vss = Tokenise(_source.settings.videosourcestring);
                        var url = vss + (vss.IndexOf('?') == -1 ? '?' : '&') + "fake=" + rand.Next();

                        response = connectionFactory.GetResponse(url, _cookies, _headers, _httpUserAgent, _login,
                            _password,
                            "GET", "", "", _useHttp10, out request);

                        // get response stream
                        if (response == null)
                            throw new Exception("Connection failed");

                        stream = response.GetResponseStream();
                        stream.ReadTimeout = _requestTimeout;


                        bool frameComplete = false;
                        // loop
                        while (!_abort.WaitOne(0))
                        {
                            // check total read
                            if (total > BufferSize - ReadSize)
                            {
                                total = 0;
                            }

                            // read next portion from stream
                            int read;
                            if ((read = stream.Read(buffer, total, ReadSize)) == 0)
                            {
                                frameComplete = true;
                                break;
                            }

                            total += read;
                        }

                        // provide new image to clients
                        if (frameComplete && NewFrame != null)
                        {
                            using (var ms = new MemoryStream(buffer, 0, total))
                            {
                                using (var bitmap = (Bitmap)Image.FromStream(ms))
                                {
                                    NewFrame(this, new NewFrameEventArgs(bitmap));
                                }
                            }
                        }

                        err = 0;
                    }
                    catch (ThreadAbortException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "JPEG");
                        err++;
                        if (err > 3)
                        {
                            _res = ReasonToFinishPlaying.DeviceLost;
                            break;
                        }

                        _abort.WaitOne(250);
                    }
                    finally
                    {
                        request?.Abort();
                        stream?.Flush();
                        stream?.Close();
                        response?.Close();
                    }
                }
            }

            PlayingFinished?.Invoke(this, new PlayingFinishedEventArgs(_res));
            _abort.Close();
        }

        private void WorkerThread2()
        {
            _abort = new ManualResetEvent(false);
            
            try
            {
                HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(_source.settings.videosourcestring);
                if (!string.IsNullOrEmpty(_login) || !string.IsNullOrEmpty(_password))
                    request.Credentials = new NetworkCredential(_login, _password);

                // asynchronously get a response
                request.BeginGetResponse(OnGetResponse, request);
            }
            catch
            {
                _abort?.Set();
            }
            
        }
           
        private void OnGetResponse(IAsyncResult asyncResult)
        {
            //_abort = new ManualResetEvent(false);
            HttpWebResponse response;
            HttpWebRequest request;
            byte[] buff;
            byte[] imageBuffer = new byte[BufferSize];  //1024 * 1024
            Stream stream;
            var err = 0;
            // get the response  
            request = (HttpWebRequest)asyncResult.AsyncState;
            response = (HttpWebResponse)request.EndGetResponse(asyncResult);

            while (!_abort.WaitOne(10) && !MainForm.ShuttingDown)
            {              
                // find our magic boundary value
                string contentType = response.Headers["Content-Type"];
                if (!string.IsNullOrEmpty(contentType) && !contentType.Contains("="))
                    throw new Exception("Invalid content-type header.  The camera is likely not returning a proper MJPEG stream.");
                string boundary = response.Headers["Content-Type"].Split('=')[1].Replace("\"", "");
                byte[] boundaryBytes = Encoding.UTF8.GetBytes(boundary.StartsWith("--") ? boundary : "--" + boundary);

                stream = response.GetResponseStream();
                BinaryReader br = new BinaryReader(stream);
                
                buff = br.ReadBytes(ReadSize);

            
                int size=0;
                if (ShouldEmitFrame)
                {
                    try
                    {
                        // find the JPEG header
                        int imageStart = GetArrayPosition(buff, JpegHeader);

                        if (imageStart != -1)
                        {
                            // copy the start of the JPEG image to the imageBuffer
                            size = buff.Length - imageStart;
                           Buffer.BlockCopy(buff, imageStart, imageBuffer, 0, size);  //Array.Copy

                            while (!_abort.WaitOne(0))
                            {
                                buff = br.ReadBytes(ReadSize);

                                // find the boundary text
                                int imageEnd = GetArrayPosition(buff, boundaryBytes);
                                if (imageEnd != -1)
                                {
                                    // copy the remainder of the JPEG to the imageBuffer
                                    Buffer.BlockCopy(buff, 0, imageBuffer, size, imageEnd);


                                    size += imageEnd;

                                    // create a single JPEG frame
                                    byte[] CurrentFrame = new byte[size];
                                    Buffer.BlockCopy(imageBuffer, 0, CurrentFrame, 0, size);

                                   // ProcessFrame(CurrentFrame);

                                    Bitmap bitmap = new Bitmap(new MemoryStream(CurrentFrame));

                                    // tell whoever's listening that we have a frame to draw
                                    if (NewFrame != null)
                                    {
                                        NewFrame(this, new NewFrameEventArgs(bitmap));
                                    }

                                    // copy the leftover data to the start
                                    Buffer.BlockCopy(buff, imageEnd, buff, 0, buff.Length - imageEnd);

                                    // fill the remainder of the buffer with new data and start over
                                    byte[] temp = br.ReadBytes(imageEnd);

                                    Buffer.BlockCopy(temp, 0, buff, buff.Length - imageEnd, temp.Length);
                                    break;
                                }

                                // copy all of the data to the imageBuffer
                                Buffer.BlockCopy(buff, 0, imageBuffer, size, buff.Length);
                                size += buff.Length;
                            }
                        }
                        err = 0;
                    }
                    catch (ThreadAbortException)
                    {
                        //break;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "JPEG");
                        err++;
                        if (err > 3)
                        {
                            _res = ReasonToFinishPlaying.DeviceLost;
                        // break;
                        throw ex;
                        }
                      _abort.WaitOne(250);
                    }
                    
                }

            }
            PlayingFinished?.Invoke(this, new PlayingFinishedEventArgs(_res));
            request?.Abort();           
            response?.Close();
            _abort.Close();
        }
              
        private int GetArrayPosition(byte[] mbyte, byte[] content)
        {
            int position = -1;

            for (int i = 0; i <= mbyte.Length - content.Length; i++)
            {
                if (mbyte[i] == content[0])
                {
                    bool isRight = true;
                    for (int j = 1; j < content.Length; j++)
                    {
                        if (mbyte[i + j] != content[j])
                        {
                            isRight = false;
                            break;
                        }
                    }
                    if (isRight) return i;
                }
            }

            return position;
        }

        // Protected implementation of Dispose pattern. 
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            // Free any unmanaged objects here. 
            //
            if (disposing)
            {

            }
            _disposed = true;
        }
    }
}
