namespace RtspMulticaster
{
    using System;
    using System.Net.Sockets;
    using System.Threading;
    using System.Net;

    /// <summary>
    /// This class is the base class for video and control packet fowarder
    /// </summary>
    public abstract class Forwarder
    {
        /// <summary>
        /// Logger object
        /// </summary>
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// First port number to use
        /// </summary>
        private const int FIRST_PORT = 6300;
        /// <summary>
        /// Last port number to use
        /// </summary>
        private const int LAST_PORT = 65534;
        /// <summary>
        /// Last used port for a forwarder
        /// </summary>
        private static int _lastOpenPort = FIRST_PORT;

        /// <summary>
        /// Gets the next port in range.
        /// </summary>
        /// <returns>The next port number</returns>
        protected static int GetNextPort()
        {
            // Get next port
            Interlocked.Add(ref _lastOpenPort, 2);
            // Check if we got to the end port
            return Interlocked.CompareExchange(ref _lastOpenPort, FIRST_PORT, LAST_PORT);
        }

        /// <summary>
        /// Forward UDP port (send data from this port)
        /// </summary>
        protected UdpClient ForwardVUdpPort
            { get; private set; }
        /// <summary>
        /// Listen UDP port (receive data on this port)
        /// </summary>
        protected UdpClient ListenCUdpPort
            { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Forwarder"/> class.
        /// </summary>
        protected Forwarder()
        {
            bool ok = false;
            while (!ok)
            {
                // Video port must be odd and command even (next one)
                // Try until we get the the good port couple.
                try
                {
                    int testPort = GetNextPort();
                    ForwardVUdpPort = new UdpClient(testPort);
                    ListenCUdpPort = new UdpClient(testPort + 1);
                    ok = true;
                }
                catch (SocketException)
                {
                    _logger.Debug("Fail to allocate port, try again");

                    if (ForwardVUdpPort != null)
                        ForwardVUdpPort.Close();

                    if (ListenCUdpPort != null)
                        ListenCUdpPort.Close();
                }
            }
            // Not sure it is usefull
            ForwardVUdpPort.DontFragment = false;
            ForwardVUdpPort.MulticastLoopback = true;
            ForwardVUdpPort.Client.SendBufferSize = 100 * 1024;
            ListenCUdpPort.Client.ReceiveBufferSize = 8 * 1024;
        }

        /// <summary>
        /// Gets or sets the forward host for video.
        /// </summary>
        /// <value>The forward host for video.</value>
        public string ForwardHostVideo { get; set; }
        /// <summary>
        /// Gets or sets the forward port for video.
        /// </summary>
        /// <value>The forward port for video.</value>
        public int ForwardPortVideo { get; set; }
        /// <summary>
        /// Gets or sets the source port for commands.
        /// </summary>
        /// <value>The source port for commands.</value>
        public int SourcePortCommand { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the output must be multicasted.
        /// </summary>
        /// <value><c>true</c> if the output must be multicasted; otherwise, <c>false</c>.</value>
        public bool ToMulticast { get; set; }

        /// <summary>
        /// Gets video port from which the system forward .
        /// </summary>
        /// <value>Video port.</value>
        public int FromForwardVideoPort
        {
            get
            {
                return ((IPEndPoint)ForwardVUdpPort.Client.LocalEndPoint).Port;
            }
        }

        /// <summary>
        /// Gets the listen command port.
        /// </summary>
        /// <value>The listen command port.</value>
        public int ListenCommandPort
        {
            get
            {
                return ((IPEndPoint)ListenCUdpPort.Client.LocalEndPoint).Port;
            }
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public abstract void Start();

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public abstract void Stop();

        /// <summary>
        /// Number of byte sent
        /// </summary>
        private int _byteVideoCounter;
        /// <summary>
        /// Is it the first time we forward a packet
        /// </summary>
        private bool _firstTime = true;
        /// <summary>
        /// Table containing forwarded packet
        /// </summary>
        private bool[] _receiveRtspFrameIndex;

        /// <summary>
        /// Inits the received frame table.
        /// </summary>
        /// <param name="aFirstIndex">First index received.</param>
        private void InitReceivedFrame(ushort aFirstIndex)
        {
            // init all to false;
            _receiveRtspFrameIndex = new bool[ushort.MaxValue + 1];
            int i = (ushort)(aFirstIndex - 10);
            while (i != aFirstIndex)
            {
                _receiveRtspFrameIndex[i] = true;
                i++;
            }
        }

        /// <summary>
        /// Video frame sended. It is use to print statistic
        /// </summary>
        /// <param name="nbOfByteSend">The nb of byte send.</param>
        /// <param name="frame">The video frame.</param>
        protected void VideoFrameSended(int nbOfByteSend, byte[] frame)
        {
            lock (ForwardVUdpPort)
            {
                if (_logger.IsDebugEnabled)
                {
                    _byteVideoCounter += nbOfByteSend;
                    if (_byteVideoCounter > 1024 * 1024 * 10)
                    {
                        _logger.Debug("10Mo forwarded from {0} => {1}:{2}", FromForwardVideoPort, ForwardHostVideo, ForwardPortVideo);
                        _byteVideoCounter = 0;
                    }
                }

                if (_logger.IsWarnEnabled)
                {
                    short newRTSPFrameIndex = BitConverter.ToInt16(frame, 2);
                    newRTSPFrameIndex = IPAddress.NetworkToHostOrder(newRTSPFrameIndex);
                    if (_firstTime)
                    {
                        InitReceivedFrame((ushort)newRTSPFrameIndex);
                        _firstTime = false;
                    }
                    _receiveRtspFrameIndex[(ushort)newRTSPFrameIndex] = true;
                    ushort oldIndex = (ushort)(newRTSPFrameIndex - 10);
                    if (!_receiveRtspFrameIndex[oldIndex])
                    {
                        _logger.Warn("Missing packet {0}", oldIndex);
                    }
                    // supress the old packet
                    _receiveRtspFrameIndex[oldIndex] = false;
                }
            }
        }

        private enum RTCPType : byte
        {
            SR = 200,
            RR = 201,
            SDES = 202,
            BYE = 203,
            APP = 204,
            XR = 207,

        }

        /// <summary>
        /// Occurs when a command is receive.
        /// </summary>
        public event EventHandler CommandReceive;

        /// <summary>
        /// Raises the <see cref="E:CommandReceive"/> event.
        /// </summary>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected void OnCommandReceive(EventArgs e)
        {
            EventHandler handler = CommandReceive;
            if (handler != null)
                handler(this, e);
        }

        /// <summary>
        /// Commands frame sended.
        /// use to print statistic
        /// </summary>
        /// <param name="frame">The command frame.</param>
        protected void CommandFrameSended(byte[] frame)
        {
            OnCommandReceive(new EventArgs());

            // this job is only usefull if it log something
            if (_logger.IsDebugEnabled)
            {
                lock (ListenCUdpPort)
                {
                    // decode the RTCP sended command
                    int packetIndex = 0;
                    short length;
                    while (frame.Length > packetIndex + 4)
                    {
                        length = BitConverter.ToInt16(frame, 2 + packetIndex);
                        length = IPAddress.NetworkToHostOrder(length);
                        _logger.Debug("Forward command {0} , length : {1},index {2}", (RTCPType)frame[1 + packetIndex], length, packetIndex);
                        packetIndex += (length + 1) * 4;
                    }
                }
            }
        }
    }
}
