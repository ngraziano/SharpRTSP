namespace RtspMulticaster
{
    using System;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    
    public class UDPForwarder : Forwarder
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();


        private UdpClient _listenVUdpPort;
        private UdpClient _forwarCUdpPort;
        private Thread _forwardVThread;
        private Thread _forwardCThread;

        /// <summary>
        /// Initializes a new instance of the <see cref="UDPForwarder"/> class.
        /// </summary>
        public UDPForwarder()
            : base()
        {
            bool ok = false;
            while (!ok)
            {
                // Video port must be odd and command even (next one)
                try
                {
                    int testPort = GetNextPort();
                    _listenVUdpPort = new UdpClient(testPort);
                    _forwarCUdpPort = new UdpClient(testPort + 1);
                    ok = true;
                }
                catch (SocketException)
                {
                    _logger.Debug("Fail to allocate port, try again");
                    if (_listenVUdpPort != null)
                        _listenVUdpPort.Close();
                    if (_forwarCUdpPort != null)
                        _forwarCUdpPort.Close();
                }
            }
            _listenVUdpPort.Client.ReceiveBufferSize = 100 * 1024;
            _forwarCUdpPort.DontFragment = false;
            _forwarCUdpPort.Client.SendBufferSize = 8 * 1024;
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public override void Start()
        {
            if (ForwardHostVideo == null)
            {
                throw new InvalidOperationException("UDP Forwader host was not initialized, can't continue");
            }
            if (ForwardPortVideo <= 0)
            {
                throw new InvalidOperationException("UDP Forwader port was not initialized, can't continue");
            }

            Contract.EndContractBlock();

            if (_forwardVThread != null)
            {
                throw new InvalidOperationException("Forwarder was stopped, can't restart it");
            }


            _forwardVThread = new Thread(new ThreadStart(DoVideoJob));
            _forwardVThread.Start();
            if (ForwardPortCommand > 0 && !string.IsNullOrEmpty(ForwardHostCommand))
            {
                _forwardCThread = new Thread(new ThreadStart(DoCommandJob));
                _forwardCThread.Start();
            }
            else
            {
                _logger.Debug("Command forward not initialized so it is not started.");
            }
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public override void Stop()
        {
            if (this.ToMulticast && ForwardPortCommand > 0)
            {
                IPAddress multicastAdress;
                if (IPAddress.TryParse(this.ForwardHostVideo, out multicastAdress))
                    ListenCUdpPort.DropMulticastGroup(multicastAdress);
            }

            _listenVUdpPort.Close();
            ListenCUdpPort.Close();
            ForwardVUdpPort.Close();
            _forwarCUdpPort.Close();
        }



        /// Gets or sets the forward host for command.
        /// </summary>
        /// <value>The forward host for command.</value>
        public string ForwardHostCommand { get; set; }
        /// <summary>
        /// Gets or sets the source port for video.
        /// </summary>
        /// <value>The source port for video.</value>
        public int SourcePortVideo { get; set; }
        /// <summary>
        /// Gets or sets the forward port for command.
        /// </summary>
        /// <value>The forward port for command.</value>
        public int ForwardPortCommand { get; set; }


        /// <summary>
        /// Gets the listen video port.
        /// </summary>
        /// <value>The listen video port.</value>
        public int ListenVideoPort
        {
            get
            {
                return ((IPEndPoint)_listenVUdpPort.Client.LocalEndPoint).Port;
            }
        }

        /// <summary>
        /// Gets command port from which it forward .
        /// </summary>
        /// <value>From forward command port.</value>
        public int FromForwardCommandPort
        {
            get
            {
                return ((IPEndPoint)_forwarCUdpPort.Client.LocalEndPoint).Port;
            }
        }

        /// <summary>
        /// Does the video job.
        /// </summary>
        private void DoVideoJob()
        {

            // TODO think if we must set ip address to something else than Any
            IPEndPoint orginalIPEndPoint = new IPEndPoint(IPAddress.Any, SourcePortVideo);
            _logger.Debug("Forward from {0} => {1}:{2}", ListenVideoPort, ForwardHostVideo, ForwardPortVideo);
            ForwardVUdpPort.Connect(ForwardHostVideo, ForwardPortVideo);
            byte[] frame;
            try
            {
                do
                {
                    IPEndPoint ipEndPoint = orginalIPEndPoint;
                    frame = _listenVUdpPort.Receive(ref ipEndPoint);
                    ForwardVUdpPort.BeginSend(frame, frame.Length, new AsyncCallback(EndSendVideo), frame);
                }
                while (true);
            }
            catch (ObjectDisposedException)
            {
                _logger.Debug("Forward video closed");
            }
            catch (SocketException)
            {
                _logger.Debug("Forward video closed");
            }
        }


        /// <summary>
        /// Ends the send video.
        /// </summary>
        /// <param name="result">The result.</param>
        private void EndSendVideo(IAsyncResult result)
        {
            try
            {
                int nbOfByteSend = ForwardVUdpPort.EndSend(result);
                byte[] frame = (byte[])result.AsyncState;
                VideoFrameSended(nbOfByteSend, frame);
            }
            catch (Exception error)
            {
                _logger.Error("Error during video forwarding", error);
            }
        }



        /// <summary>
        /// Does the command job.
        /// </summary>
        private void DoCommandJob()
        {
            IPEndPoint originalUdpEndPoint = new IPEndPoint(IPAddress.Any, ListenCommandPort);

            _forwarCUdpPort.Connect(ForwardHostCommand, ForwardPortCommand);
            if (this.ToMulticast)
            {
                IPAddress multicastAdress = IPAddress.Parse(this.ForwardHostVideo);
                ListenCUdpPort.JoinMulticastGroup(multicastAdress);
                _logger.Debug("Forward Command from multicast  {0}:{1} => {2}:{3}", this.ForwardHostVideo, ListenCommandPort, ForwardHostCommand, ForwardPortCommand);

            }
            else
            {
                _logger.Debug(CultureInfo.InvariantCulture,"Forward Command from {0} => {1}:{2}", ListenCommandPort, ForwardHostCommand, ForwardPortCommand);
            }

            byte[] frame;
            try
            {
                do
                {
                    IPEndPoint udpEndPoint = originalUdpEndPoint;
                    frame = ListenCUdpPort.Receive(ref udpEndPoint);
                    _forwarCUdpPort.BeginSend(frame, frame.Length, new AsyncCallback(EndSendCommand), frame);
                }
                while (true);
                //The break of the loop is made by close wich raise an exception
                //TODO  check if we can avoid this exception
            }
            catch (ObjectDisposedException)
            {
                _logger.Debug("Forward command closed");
            }
            catch (SocketException error)
            {
                _logger.Debug("The exception", error);
                _logger.Debug("Forward command closed");
            }
        }


        /// <summary>
        /// Ends the send command.
        /// </summary>
        /// <param name="result">The result.</param>
        private void EndSendCommand(IAsyncResult result)
        {
            try
            {
                _forwarCUdpPort.EndSend(result);
                byte[] frame = (byte[])result.AsyncState;
                CommandFrameSended(frame);
            }
            catch (Exception error)
            {
                _logger.Error("Error during command forwarding", error);
            }
        }



    }
}
