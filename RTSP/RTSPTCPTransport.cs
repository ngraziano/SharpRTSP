using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics.Contracts;

namespace RTSP
{
    /// <summary>
    /// TCP Connection for RTSP
    /// </summary>
    public class RTSPTCPTransport : IRTSPTransport
    {
        private IPEndPoint _currentEndPoint;
        private TcpClient _RTSPServerClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPTCPTransport"/> class.
        /// </summary>
        /// <param name="TCPConnection">The underlying TCP connection.</param>
        public RTSPTCPTransport(TcpClient TCPConnection)
        {
            if (TCPConnection == null)
                throw new ArgumentNullException("TCPConnection");
            Contract.EndContractBlock();

            _currentEndPoint = (IPEndPoint)TCPConnection.Client.RemoteEndPoint;
            _RTSPServerClient = TCPConnection;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPTCPTransport"/> class.
        /// </summary>
        /// <param name="aHost">A host.</param>
        /// <param name="aPortNumber">A port number.</param>
        public RTSPTCPTransport(string aHost, int aPortNumber)
            : this(new TcpClient(aHost, aPortNumber))
        {
        }


        #region IRTSPTransport Membres

        /// <summary>
        /// Gets the stream of the transport.
        /// </summary>
        /// <returns>A stream</returns>
        public Stream GetStream()
        {
            return _RTSPServerClient.GetStream();
        }

        /// <summary>
        /// Gets the remote address.
        /// </summary>
        /// <value>The remote address.</value>
        public string RemoteAddress
        {
            get
            {
                return string.Format("{0}:{1}", _currentEndPoint.Address, _currentEndPoint.Port);
            }
        }

        /// <summary>
        /// Closes this instance.
        /// </summary>
        public void Close()
        {
            _RTSPServerClient.Close();
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="IRTSPTransport"/> is connected.
        /// </summary>
        /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
        public bool Connected
        {
            get { return _RTSPServerClient.Connected; }
        }

        /// <summary>
        /// Reconnect this instance.
        /// <remarks>Must do nothing if already connected.</remarks>
        /// </summary>
        public void ReConnect()
        {
            if (_RTSPServerClient.Connected)
                return;
            _RTSPServerClient = new TcpClient();
            _RTSPServerClient.Connect(_currentEndPoint);
        }

        #endregion
    }
}
