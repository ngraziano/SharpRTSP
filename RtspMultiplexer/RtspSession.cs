namespace RtspMulticaster
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Threading;
    using Rtsp.Messages;

    public class RtspSession
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();


        public RtspSession()
        {
            State = SessionState.Init;
        }

        public string Name { get; set; }

        private Thread _timeoutThread;
        private AutoResetEvent _dataReceive = new AutoResetEvent(false);
        private bool _stoping = false;

        /// <summary>
        /// Server State
        /// </summary>
        internal enum SessionState
        {
            /// <summary>
            /// The initial state, no valid SETUP has been received yet.
            /// </summary>
            Init,
            /// <summary>
            /// Last SETUP received was successful, reply sent or after
            /// playing, last PAUSE received was successful, reply sent.
            /// </summary>
            Ready,
            /// <summary>
            /// Last PLAY received was successful, reply sent. Data is being
            /// sent.
            /// </summary>
            Playing,
            /// <summary>
            /// The server is recording media data.
            /// </summary>
            Recording,
        }

        internal SessionState State
        {
            get;
            set;
        }
        

        private Dictionary<Uri, Forwarder> _listOfForwarder = new Dictionary<Uri, Forwarder>();

        public Dictionary<Uri, Forwarder> ListOfForwader
        {
            get { return _listOfForwarder; }
        }

        public int Timeout { get; set; }

        private List<string> _clientList = new List<string>();

        public bool IsNeeded
        {
            get
            {
                if (_clientList.Count > 0)
                    return true;
                return false;
            }
        }

        public void Start(string clientAddress)
        {
            _logger.Info("Starting session: {0} ", Name);

            if (!_clientList.Contains(clientAddress))
                _clientList.Add(clientAddress);

            if (_timeoutThread != null && _timeoutThread.IsAlive)
            {
                _logger.Debug("Session: {0} was already running", Name);
                return;
            }

            foreach (var item in _listOfForwarder)
            {
                item.Value.CommandReceive += new EventHandler(CommandReceive);
                item.Value.Start();
            }
            _timeoutThread = new System.Threading.Thread(new System.Threading.ThreadStart(this.TimeoutDetecter));
            _stoping = false;
            _timeoutThread.Start();

        }

        /// <summary>
        /// Detect Timeouts .
        /// </summary>
        private void TimeoutDetecter()
        {
            _logger.Debug("Start waiting for timeout of {0}s", Timeout);
            // wait until timeout, set of _dataReceive will reset timeout
            while (_dataReceive.WaitOne(Timeout * 1000))
            {
                if (_stoping)
                    break;
            }
            if (!_stoping)
            {
                // if we are here we timeOut
                _logger.Warn("Session {0} timeout", Name);
                TearDown();

            }
        }

        internal void TearDown()
        {
            //TODO vérifier ce bout de code....
            // Je suis vraiement pas sur là.
            foreach (var destinationUri in _listOfForwarder.Keys)
            {
                RtspRequest tearDownMessage = new RtspRequest();
                tearDownMessage.RequestTyped = RtspRequest.RequestType.TEARDOWN;
                tearDownMessage.RtspUri = destinationUri;
                RTSPDispatcher.Instance.Enqueue(tearDownMessage);
            }        
            Stop();
        }

        internal void Stop(string clientAdress)
        {
            if (_clientList.Contains(clientAdress))
                _clientList.Remove(clientAdress);

            if (!IsNeeded)
                Stop();
        }


        private void Stop()
        {
            _logger.Info("Stopping session: {0} ", Name);

            foreach (var item in _listOfForwarder)
            {
                item.Value.CommandReceive -= new EventHandler(CommandReceive);
                item.Value.Stop();
            }

            // stop the timeout detect
            _stoping = true;
            _dataReceive.Set();
        }

        private void CommandReceive(object sender, EventArgs e)
        {
            _dataReceive.Set();
        }

        internal void Handle(RtspRequest request)
        {
            CommandReceive(this, new EventArgs());
        }

        /// <summary>
        /// Gets the key name of the session.
        /// <remarks>This value is contruct with the destination name and the session header name.</remarks>
        /// </summary>
        /// <param name="uri">The original asked URI.</param>
        /// <param name="aSessionHeaderValue">Session header value.</param>
        /// <returns></returns>
        internal static string GetSessionName(Uri uri, string aSessionHeaderValue)
        {
            Contract.Requires(uri != null);
            Contract.Requires(aSessionHeaderValue != null);

            return GetSessionName(uri.Authority,aSessionHeaderValue);
        }

        /// <summary>
        /// Gets the name of the session.
        /// </summary>
        /// <param name="aDestination">A destination.</param>
        /// <param name="aSessionHeaderValue">A session header value.</param>
        /// <returns></returns>
        internal static string GetSessionName(string aDestination, string aSessionHeaderValue)
        {
            Contract.Requires(aDestination != null);
            Contract.Requires(aSessionHeaderValue != null);

            return aDestination + "|Session:" + aSessionHeaderValue;
        }


        internal void AddForwarder(Uri uri, Forwarder forwarder)
        {
            Contract.Requires(uri != null);

            // Configruation change, remove the old forwarder
            if (_listOfForwarder.ContainsKey(uri))
            {
                _listOfForwarder[uri].Stop();
                _listOfForwarder.Remove(uri);
            }


            _listOfForwarder.Add(uri, forwarder);
        }



        /// <summary>
        /// Gets or sets the destination name of the current session..
        /// </summary>
        /// <value>The destination.</value>
        public string Destination { get; set; }
    }
}
