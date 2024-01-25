namespace RtspMulticaster
{
    using Rtsp;
    using System;
    using System.Diagnostics.Contracts;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;

    public class RtspServer : IDisposable
    {

        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();


        private TcpListener _RTSPServerListener;
        private ManualResetEvent _Stopping;
        private Thread _ListenTread;


        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPServer"/> class.
        /// </summary>
        /// <param name="aPortNumber">A numero port.</param>
        public RtspServer(int portNumber)
        {
            if (portNumber < System.Net.IPEndPoint.MinPort || portNumber > System.Net.IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException("aPortNumber", portNumber, "Port number must be between System.Net.IPEndPoint.MinPort and System.Net.IPEndPoint.MaxPort");
            Contract.EndContractBlock();

            RtspUtils.RegisterUri();
            _RTSPServerListener = new TcpListener(IPAddress.Any, portNumber);
        }

        /// <summary>
        /// Starts the listen.
        /// </summary>
        public void StartListen()
        {
            _RTSPServerListener.Start();

            _Stopping = new ManualResetEvent(false);
            _ListenTread = new Thread(new ThreadStart(AcceptConnection));
            _ListenTread.Start();
        }

        /// <summary>
        /// Accepts the connection.
        /// </summary>
        private void AcceptConnection()
        {
            try
            {
                while (!_Stopping.WaitOne(0))
                {
                    TcpClient oneClient = _RTSPServerListener.AcceptTcpClient();
                    RtspListener newListener = new RtspListener(
                        new RtspTcpTransport(oneClient, new()));
                    RTSPDispatcher.Instance.AddListener(newListener);
                    newListener.Start();
                }
            }
            catch (SocketException error)
            {
                _logger.Warn(error, "Got an error listening, I have to handle the stopping which also throw an error");
            }
            catch (Exception error)
            {
                _logger.Error(error, "Got an error listening...");
                throw;
            }


        }

        public void StopListen()
        {
            _RTSPServerListener.Stop();
            _Stopping.Set();
            _ListenTread.Join();
        }

        #region IDisposable Membres

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopListen();
                _Stopping.Dispose();
            }
        }

        #endregion
    }
}
