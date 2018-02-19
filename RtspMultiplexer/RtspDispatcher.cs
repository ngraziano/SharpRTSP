namespace RtspMulticaster
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using Rtsp;
    using Rtsp.Messages;

    /// <summary>
    /// This class handle the rewrite and dispatchin of RTSP messages
    /// </summary>
    public class RTSPDispatcher
    {
        private RTSPDispatcher()
        { }
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private static RTSPDispatcher _instance = new RTSPDispatcher();

        private Dictionary<string, RtspListener> _serverListener = new Dictionary<string, RtspListener>(StringComparer.OrdinalIgnoreCase);
        private ConcurrentQueue<RtspMessage> _queue = new ConcurrentQueue<RtspMessage>();

        private Dictionary<string, UDPForwarder> _setupForwarder = new Dictionary<string, UDPForwarder>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// List of the active sessions
        /// </summary>
        private Dictionary<string, RtspSession> _activesSession = new Dictionary<string, RtspSession>(StringComparer.OrdinalIgnoreCase);


        private Thread _jobQueue;
        private ManualResetEvent _stopping = new ManualResetEvent(false);
        private AutoResetEvent _newMessage = new AutoResetEvent(false);

        private RtspPushManager pushManager = new RtspPushManager();

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        /// <value>The instance.</value>
        public static RTSPDispatcher Instance
        {
            get
            {
                return _instance;
            }
        }

        /// <summary>
        /// Enqueues the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Enqueue(RtspMessage message)
        {
            _logger.Debug("One message enqueued");
            _queue.Enqueue(message);
            _newMessage.Set();

        }

        /// <summary>
        /// Starts the queue processing.
        /// </summary>
        public void StartQueue()
        {
            _jobQueue = new Thread(new ThreadStart(ManageQueue));
            _jobQueue.Start();
        }

        /// <summary>
        /// Stops the queue processing.
        /// </summary>
        public void StopQueue()
        {
            _stopping.Set();
            // fake a new message to unblock thread
            _newMessage.Set();

            // Stop the different active session
            foreach (var oneSession in _activesSession.Values)
            {
                oneSession.TearDown();
            }
            _activesSession.Clear();

            foreach (var oneListener in _serverListener.Values)
            {
                oneListener.Stop();
            }
            _serverListener.Clear();
        }

        /// <summary>
        /// The queue processing.
        /// </summary>
        private void ManageQueue()
        {
            while (!_stopping.WaitOne(0))
            {
                _newMessage.WaitOne();
                RtspMessage message;

                while (_queue.TryDequeue(out message))
                {
                    //try
                    //{
                    HandleOneMessage(message);
                    //}
                    //catch(Exception error)
                    //{
                    //    _logger.Warn("Error during processing {0}", error, error.Message);
                    //}

                }
            }
        }

        /// <summary>
        /// Add a listener.
        /// </summary>
        /// <param name="listener">A listener.</param>
        public void AddListener(RtspListener listener)
        {
            if (listener == null)
                throw new ArgumentNullException("listener");
            Contract.EndContractBlock();

            listener.MessageReceived += new EventHandler<RtspChunkEventArgs>(Listener_MessageReceived);
            _serverListener.Add(listener.RemoteAdress, listener);

        }

        /// <summary>
        /// Handles the MessageReceived event of the Listener control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="RTSP.RTSPChunkEventArgs"/> instance containing the event data.</param>
        private void Listener_MessageReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            Contract.Requires(e.Message != null);
            this.Enqueue(e.Message as RtspMessage);
        }

        /// <summary>
        /// Handles one message.
        /// </summary>
        /// <param name="message">The message.</param>
        private void HandleOneMessage(RtspMessage message)
        {
            Contract.Requires(message != null);

            RtspListener destination = null;

            if (message is RtspRequest)
            {
                destination = HandleRequest(ref message);
                _logger.Debug("Dispatch message from {0} to {1}",
                    message.SourcePort != null ? message.SourcePort.RemoteAdress : "UNKNOWN",destination != null ? destination.RemoteAdress : "UNKNOWN" );

                // HandleRequest can change message type.
                if(message is RtspRequest)
                {
                    var context = new OriginContext();
                    context.OriginCSeq = message.CSeq;
                    context.OriginSourcePort = message.SourcePort;
                    (message as RtspRequest).ContextData = context;
                }


            }
            else if (message is RtspResponse)
            {
                RtspResponse response = message as RtspResponse;

                if (response.OriginalRequest != null)
                {
                    var context = response.OriginalRequest.ContextData as OriginContext;
                    if (context != null)
                    {
                        destination = context.OriginSourcePort;
                        response.CSeq = context.OriginCSeq;
                        _logger.Debug("Dispatch response back to {0}", destination.RemoteAdress);
                    }

                }

                HandleResponse(response);
                
            }

            if (destination != null)
            {
                bool isGood = destination.SendMessage(message);

                if (!isGood)
                {

                    destination.Stop();
                    _serverListener.Remove(destination.RemoteAdress);

                    // send back a message because we can't forward.
                    if (message is RtspRequest && message.SourcePort != null)
                    {
                        RtspRequest request = message as RtspRequest;
                        RtspResponse theDirectResponse = request.CreateResponse();
                        _logger.Warn("Error during forward : {0}. So sending back a direct error response", message.Command);
                        theDirectResponse.ReturnCode = 500;
                        request.SourcePort.SendMessage(theDirectResponse);

                    }
                }
            }
        }

        /// <summary>
        /// Gets the RTSP listener for destination.
        /// </summary>
        /// <param name="destinationUri">The destination URI.</param>
        /// <returns>An RTSP listener</returns>
        /// <remarks>
        /// This method try to get one of openned TCP listener and
        /// if it does not find it, it create it. 
        /// </remarks>
        private RtspListener GetRtspListenerForDestination(Uri destinationUri)
        {
            Contract.Requires(destinationUri != null);

            RtspListener destination;
            string destinationName = destinationUri.Authority;
            if (_serverListener.ContainsKey(destinationName))
                destination = _serverListener[destinationName];
            else
            {
                destination = new RtspListener(
                    new RtspTcpTransport(destinationUri.Host, destinationUri.Port)
                    );

                // un peu pourri mais pas d'autre idée...
                // pour avoir vraiment des clef avec IP....
                if (_serverListener.ContainsKey(destination.RemoteAdress))
                    destination = _serverListener[destination.RemoteAdress];
                else
                {
                    AddListener(destination);
                    destination.Start();
                }
            }
            return destination;
        }


        /// <summary>
        /// Handles request message.
        /// </summary>
        /// <param name="message">A message, can be rewriten.</param>
        /// <returns>The destination</returns>
        private RtspListener HandleRequest(ref RtspMessage message)
        {
            Contract.Requires(message != null);
            Contract.Requires(message is RtspRequest);
            Contract.Ensures(Contract.Result<RtspListener>() != null);
            Contract.Ensures(Contract.ValueAtReturn(out message) != null);


            RtspRequest request = message as RtspRequest;

            RtspListener destination;
            // Do not forward, direct respond because we do not know where to send.
            if (request.RtspUri == null || request.RtspUri.AbsolutePath.Split(new char[] { '/' }, 3).Length < 3)
            {
                destination = HandleRequestWithoutUrl(ref message);
            }
            else if (request.RtspUri.AbsolutePath.StartsWith("/PUSH/"))
            {
                destination = HandleRequestPush(ref message);
            }
            else if (request.RtspUri.AbsolutePath.StartsWith("/PULL/"))
            {
                destination = HandleRequestPull(ref message);
            }
            else
            {
                try
                {
                    // get the real destination
                    request.RtspUri = RewriteUri(request.RtspUri);
                    destination = GetRtspListenerForDestination(request.RtspUri);

                    // Handle setup
                    RtspRequestSetup requestSetup = request as RtspRequestSetup;
                    if (requestSetup != null)
                    {
                        message = HandleRequestSetup(ref destination, requestSetup);
                    }
                    
                    //Handle Play Reques
                    RtspRequestPlay requestPlay = request as RtspRequestPlay;
                    if (requestPlay != null)
                    {
                        message = HandleRequestPlay(ref destination, requestPlay);
                    }
                    
                    

                    //Update session state and handle special message
                    if (request.Session != null && request.RtspUri != null)
                    {
                        string sessionKey = RtspSession.GetSessionName(request.RtspUri, request.Session);
                        if (_activesSession.ContainsKey(sessionKey))
                        {

                            _activesSession[sessionKey].Handle(request);
                            switch (request.RequestTyped)
                            {
                                // start here to start early
                                //case RtspRequest.RequestType.PLAY:
                                  // _activesSession[sessionKey].Start(request.SourcePort.RemoteAdress); 
                                 //   break;
                                case RtspRequest.RequestType.TEARDOWN:
                                    _activesSession[sessionKey].Stop(request.SourcePort.RemoteAdress);
                                    if (!_activesSession[sessionKey].IsNeeded)
                                        _activesSession.Remove(sessionKey);
                                    else
                                    {
                                        // system still need the server to send data do not send him the message.
                                        // reponds to client directly.
                                        destination = request.SourcePort;
                                        message = request.CreateResponse();
                                    }
                                    break;

                            }
                        }
                        else
                        {
                            _logger.Warn("Command {0} for session {1} which was not found", request.RequestTyped, sessionKey);
                        }
                    }

                }
                catch (Exception error)
                {
                    _logger.Error("Error during handle of request", error);
                    destination = request.SourcePort;
                    RtspResponse theDirectResponse = request.CreateResponse();
                    theDirectResponse.ReturnCode = 500;
                    message = theDirectResponse;
                }

            }

            return destination;
        }

        private RtspListener HandleRequestPush(ref RtspMessage message)
        {
            Contract.Requires(message != null);
            Contract.Requires(message is RtspRequest);
            Contract.Ensures(Contract.Result<RtspListener>() != null);
            Contract.Ensures(Contract.ValueAtReturn(out message) != null);


            RtspListener destination;
            destination = message.SourcePort;
            RtspRequest request = message as RtspRequest;
            RtspResponse theDirectResponse;

   

            switch (request.RequestTyped)
            {
                case RtspRequest.RequestType.OPTIONS:
                    theDirectResponse = pushManager.HandleOptions(message as RtspRequestOptions);
                    break;
                case RtspRequest.RequestType.ANNOUNCE:
                    theDirectResponse = pushManager.HandleAnnounce(message as RtspRequestAnnounce);
                    break;
                case RtspRequest.RequestType.SETUP:
                    theDirectResponse = pushManager.HandleSetup(message as RtspRequestSetup);
                    break;
                case RtspRequest.RequestType.RECORD:
                    theDirectResponse = pushManager.HandleRecord(message as RtspRequestRecord);
                    break;
                case RtspRequest.RequestType.TEARDOWN:
                    theDirectResponse = pushManager.HandleTeardown(message as RtspRequestTeardown);
                    break;
                default:
                    _logger.Warn("Do not know how to handle : {0}", message.Command);
                    theDirectResponse = request.CreateResponse();
                    theDirectResponse.ReturnCode = 400;
                    break;
            }
            
            message = theDirectResponse;
            return destination;
        }

        private RtspListener HandleRequestPull(ref RtspMessage message)
        {
            Contract.Requires(message != null);
            Contract.Requires(message is RtspRequest);
            Contract.Ensures(Contract.Result<RtspListener>() != null);
            Contract.Ensures(Contract.ValueAtReturn(out message) != null);


            RtspListener destination;
            destination = message.SourcePort;
            RtspRequest request = message as RtspRequest;
            RtspResponse theDirectResponse;



            switch (request.RequestTyped)
            {
                case RtspRequest.RequestType.OPTIONS:
                    theDirectResponse = pushManager.HandleOptions(message as RtspRequestOptions);
                    break;
                case RtspRequest.RequestType.DESCRIBE:
                    theDirectResponse = pushManager.HandlePullDescribe(message as RtspRequestDescribe);
                    break;
                case RtspRequest.RequestType.SETUP:
                    theDirectResponse = pushManager.HandlePullSetup(message as RtspRequestSetup);
                    break;
                case RtspRequest.RequestType.PLAY:
                    theDirectResponse = pushManager.HandlePullPlay(message as RtspRequestPlay);
                    break;
                case RtspRequest.RequestType.GET_PARAMETER:
                    theDirectResponse = pushManager.HandlePullGetParameter(message as RtspRequestGetParameter);
                    break;
                default:
                    _logger.Warn("Do not know how to handle : {0}", message.Command);
                    theDirectResponse = request.CreateResponse();
                    theDirectResponse.ReturnCode = 400;
                    break;
            }

            message = theDirectResponse;
            return destination;
        }

        private static RtspListener HandleRequestWithoutUrl(ref RtspMessage message)
        {
            Contract.Requires(message != null);
            Contract.Requires(message is RtspRequest);
            Contract.Ensures(Contract.Result<RtspListener>() != null);
            Contract.Ensures(Contract.ValueAtReturn(out message) != null);


            RtspListener destination;
            destination = message.SourcePort;
            RtspRequest request = message as RtspRequest;
            RtspResponse theDirectResponse = request.CreateResponse();
            if (request.RequestTyped == RtspRequest.RequestType.OPTIONS)
            {
                // We know what to do...
                theDirectResponse.ReturnCode = 200;
                // But perhaps it is to prevent timeout !!
                // ARG .....
                _logger.Info("I got an OPTION * message, I reply but I do not forward! The end session may timeout.");
                request.LogMessage();
            }
            else
            {
                _logger.Warn("Do not know how to handle : {0}", message.Command);
                theDirectResponse.ReturnCode = 400;
            }
            message = theDirectResponse;
            return destination;
        }

        private static Uri RewriteUri(Uri originalUri)
        {
            Contract.Requires(originalUri != null);

            string[] pathPart = originalUri.AbsolutePath.Split(new char[] { '/' }, 3);

            if (pathPart.Length < 3)
                throw new ArgumentException(String.Format(CultureInfo.InvariantCulture, "The url {0} do not contain forward part ", originalUri), "originalUri");

            string destination = Uri.UnescapeDataString(pathPart[1]);

            string[] destinationPart = destination.Split(':');
            int port;
            // if no usable port number set it to default
            if (destinationPart.Length < 2 || !int.TryParse(destinationPart[1], out port))
            {
                port = -1;
            }

            UriBuilder url = new UriBuilder(originalUri);
            url.Host = destinationPart[0];
            url.Port = port;
            url.Path = pathPart[2];

            _logger.Debug("Rewrite Url {0} to {1}", originalUri, url);
            return url.Uri;
        }


        /// <summary>
        /// Handles a request setup.
        /// </summary>
        /// <param name="destination">The destination.</param>
        /// <param name="requestSetup">The request setup.</param>
        /// <returns>The rewritten message</returns>
        /// <remarks>
        /// The destination can be modified.
        /// </remarks>
        private RtspMessage HandleRequestSetup(ref RtspListener destination, RtspRequestSetup requestSetup)
        {
            Contract.Requires(requestSetup != null);
            Contract.Requires(destination != null);
            Contract.Ensures(Contract.Result<RtspMessage>() != null);
            Contract.Ensures(Contract.ValueAtReturn(out destination) != null);


            // look if we already have a multicast streaming playing for this URI.
            foreach (var session in _activesSession.Values)
            {
                if (session.State == RtspSession.SessionState.Playing && session.ListOfForwader.ContainsKey(requestSetup.RtspUri))
                {
                    Forwarder existingForwarder = session.ListOfForwader[requestSetup.RtspUri];
                    if (existingForwarder != null && existingForwarder.ToMulticast)
                    {
                        RtspResponse returnValue = requestSetup.CreateResponse();
                        returnValue.Headers[RtspHeaderNames.Transport] = new RtspTransport()
                        {
                            IsMulticast = true,
                            Destination = existingForwarder.ForwardHostVideo,
                            Port = new PortCouple(existingForwarder.ForwardPortVideo, existingForwarder.ListenCommandPort),

                        }.ToString();
                        returnValue.Session = session.Name;
                        destination = requestSetup.SourcePort;
                        return returnValue;
                    }
                }
            }


            string setupKey = requestSetup.SourcePort.RemoteAdress + "SEQ" + requestSetup.CSeq.ToString(CultureInfo.InvariantCulture);

            RtspTransport selectedTransport = SelectTransport(requestSetup);

            // We do not handle asked transport so return directly.
            if (selectedTransport == null)
            {
                _logger.Info("No transport asked are supported, sorry");
                RtspResponse returnValue = requestSetup.CreateResponse();
                // Unsupported transport;
                returnValue.ReturnCode = 461;
                destination = requestSetup.SourcePort;
                return returnValue;
            }

            UDPForwarder forwarder = new UDPForwarder();
            forwarder.ToMulticast = selectedTransport.IsMulticast;

            // this part of config is only valid in unicast.
            if (!selectedTransport.IsMulticast)
            {
                forwarder.ForwardPortVideo = selectedTransport.ClientPort.First;
                forwarder.SourcePortCommand = selectedTransport.ClientPort.Second;

                // If the client did not set the destination.. get it from TCP source
                if (!string.IsNullOrEmpty(selectedTransport.Destination))
                {
                    forwarder.ForwardHostVideo = selectedTransport.Destination;
                }
                else
                {
                    forwarder.ForwardHostVideo = requestSetup.SourcePort.RemoteAdress.Split(':')[0];
                    _logger.Debug("Destination get from TCP port {0}", forwarder.ForwardHostVideo);
                }
            }

            // Configured the transport asked.
            forwarder.ForwardHostCommand = destination.RemoteAdress.Split(':')[0];
            RtspTransport firstNewTransport = new RtspTransport()
                {
                    IsMulticast = false,
                    ClientPort = new PortCouple(forwarder.ListenVideoPort, forwarder.FromForwardCommandPort),
                };

            RtspTransport secondTransport = new RtspTransport()
                {
                    IsMulticast = false,
                    LowerTransport = RtspTransport.LowerTransportType.TCP,
                };

            requestSetup.Headers[RtspHeaderNames.Transport] = firstNewTransport.ToString() + ", " + secondTransport.ToString();
            requestSetup.Headers[RtspHeaderNames.Transport] = firstNewTransport.ToString();// +", " + secondTransport.ToString();
            _setupForwarder.Add(setupKey, forwarder);

            return requestSetup;

        }
        /// <summary>
        /// Handles the request play.
        /// Do not forward message if already playing
        /// </summary>
        /// <param name="destination">The destination.</param>
        /// <param name="requestPlay">The request play.</param>
        /// <returns>The message to transmit</returns>
        private RtspMessage HandleRequestPlay(ref RtspListener destination, RtspRequestPlay requestPlay)
        {
            Contract.Requires(requestPlay != null);
            Contract.Requires(destination != null);
            Contract.Ensures(Contract.Result<RtspMessage>() != null);
            Contract.Ensures(Contract.ValueAtReturn(out destination) != null);


            string sessionKey = RtspSession.GetSessionName(requestPlay.RtspUri, requestPlay.Session);
            if (_activesSession.ContainsKey(sessionKey))
            {

                RtspSession session = _activesSession[sessionKey];
                
                // si on est dèjà en play on n'envoie pas la commande a la source.
                if (session.State == RtspSession.SessionState.Playing)
                {
                    session.Start(requestPlay.SourcePort.RemoteAdress);
                    RtspResponse returnValue = requestPlay.CreateResponse();
                    destination = requestPlay.SourcePort;
                    return returnValue;
                }

                // ajoute un client
                session.Start(requestPlay.SourcePort.RemoteAdress);
            }
            return requestPlay;

        }
        /// <summary>
        /// Selects the transport based on the configuration of the system..
        /// </summary>
        /// <param name="requestSetup">The request setup message.</param>
        /// <returns>The selected transport</returns>
        /// <remarks>
        /// The transport is selected by taking the first supported transport
        /// in order of appearence.
        /// </remarks>
        private static RtspTransport SelectTransport(RtspRequestSetup requestSetup)
        {
            RtspTransport selectedTransport = null;
            const bool acceptTCP = false;
            const bool acceptUDPUnicast = true;
            const bool acceptUDPMulticast = true;

            foreach (RtspTransport proposedTransport in requestSetup.GetTransports())
            {
                if (acceptTCP && proposedTransport.LowerTransport == RtspTransport.LowerTransportType.TCP)
                {
                    selectedTransport = proposedTransport;
                    break;
                }
                if (acceptUDPMulticast && proposedTransport.LowerTransport == RtspTransport.LowerTransportType.UDP
                    && proposedTransport.IsMulticast)
                {
                    selectedTransport = proposedTransport;
                    break;
                }
                if (acceptUDPUnicast && proposedTransport.LowerTransport == RtspTransport.LowerTransportType.UDP
                    && !proposedTransport.IsMulticast)
                {
                    selectedTransport = proposedTransport;
                    break;
                }
            }
            return selectedTransport;
        }



        /// <summary>
        /// Handles the response.
        /// </summary>
        /// <param name="message">A message.</param>
        private void HandleResponse(RtspResponse message)
        {
            Contract.Requires(message != null);

            if (message.OriginalRequest != null && message.OriginalRequest is RtspRequestSetup)
            {
                HandleResponseToSetup(message);
            }

            UpdateSessionState(message);


            //TODO rewrite instead of remove
            if (message.Headers.ContainsKey(RtspHeaderNames.ContentBase))
                message.Headers.Remove(RtspHeaderNames.ContentBase);

            if (message.Headers.ContainsKey(RtspHeaderNames.ContentType) &&
                message.Headers[RtspHeaderNames.ContentType] == "application/sdp")
            {
                RewriteSDPMessage(message);
            }


        }

        /// <summary>
        /// Updates the state of the session.
        /// </summary>
        /// <param name="message">A response message.</param>
        private void UpdateSessionState(RtspResponse message)
        {
            // if no session can be found
            if (message.OriginalRequest == null ||
                (message.Session == null && message.OriginalRequest.Session == null) ||
                message.OriginalRequest.RtspUri == null)
                return;

            // voir a mettre dans la message....
            string curentSession = message.Session != null ? message.Session : message.OriginalRequest.Session;
            
                

            //Update session state and handle special message
            string sessionKey = RtspSession.GetSessionName(message.OriginalRequest.RtspUri, curentSession);
            if (_activesSession.ContainsKey(sessionKey))
            {
                if (message.ReturnCode >= 300 && message.ReturnCode < 400)
                    _activesSession[sessionKey].State = RtspSession.SessionState.Init;
                else if (message.ReturnCode < 300)
                {
                    switch (message.OriginalRequest.RequestTyped)
                    {
                        case RtspRequest.RequestType.SETUP:
                            if (_activesSession[sessionKey].State == RtspSession.SessionState.Init)
                                _activesSession[sessionKey].State = RtspSession.SessionState.Ready;
                            break;
                        case RtspRequest.RequestType.PLAY:
                            if (_activesSession[sessionKey].State == RtspSession.SessionState.Ready)
                                _activesSession[sessionKey].State = RtspSession.SessionState.Playing;
                            break;
                        case RtspRequest.RequestType.RECORD:
                            if (_activesSession[sessionKey].State == RtspSession.SessionState.Ready)
                                _activesSession[sessionKey].State = RtspSession.SessionState.Recording;
                            break;
                        case RtspRequest.RequestType.PAUSE:
                            if (_activesSession[sessionKey].State == RtspSession.SessionState.Playing ||
                                _activesSession[sessionKey].State == RtspSession.SessionState.Recording)
                                _activesSession[sessionKey].State = RtspSession.SessionState.Ready;
                            break;
                        case RtspRequest.RequestType.TEARDOWN:
                            _activesSession[sessionKey].State = RtspSession.SessionState.Init;

                            break;
                    }
                }
            }
            else
            {
                _logger.Warn("Command {0} for session {1} which was not found", message.OriginalRequest.RequestTyped, sessionKey);
            }
        }

        /// <summary>
        /// Handles the response to a setup message.
        /// </summary>
        /// <param name="message">A response message.</param>
        private void HandleResponseToSetup(RtspResponse message)
        {
            RtspRequest original = message.OriginalRequest;
            string setupKey = original.SourcePort.RemoteAdress + "SEQ" + message.CSeq.ToString(CultureInfo.InvariantCulture);

            if (message.IsOk)
            {
                Forwarder forwarder = ConfigureTransportAndForwarder(message, _setupForwarder[setupKey]);

                RtspSession newSession;
                string sessionKey = RtspSession.GetSessionName(original.RtspUri, message.Session);
                if (_activesSession.ContainsKey(sessionKey))
                {
                    newSession = _activesSession[sessionKey];
                    _logger.Info("There was an already a session with ths ID {0}", newSession.Name);
                }
                else
                {
                    _logger.Info("Create a new session with the ID {0}", sessionKey);
                    newSession = new RtspSession();
                    newSession.Name = message.Session;
                    newSession.Destination = original.RtspUri.Authority;
                    _activesSession.Add(sessionKey, newSession);
                }

                newSession.AddForwarder(original.RtspUri, forwarder);
                newSession.Timeout = message.Timeout;
            }
            // No needed here anymore.
            _setupForwarder.Remove(setupKey);
        }

        /// <summary>
        /// Configures the transport and forwarder.
        /// </summary>
        /// <param name="aMessage">A message.</param>
        /// <param name="forwarder">The preset forwarder.</param>
        /// <returns>The configured forwarder.</returns>
        private static Forwarder ConfigureTransportAndForwarder(RtspMessage aMessage, UDPForwarder forwarder)
        {
            RtspTransport transport = RtspTransport.Parse(aMessage.Headers[RtspHeaderNames.Transport]);

            Forwarder resultForwarder;
            if (transport.LowerTransport == RtspTransport.LowerTransportType.UDP)
            {
                if (transport.ServerPort != null)
                {
                    forwarder.SourcePortVideo = transport.ServerPort.First;
                    forwarder.ForwardPortCommand = transport.ServerPort.Second;
                }
                resultForwarder = forwarder;
            }
            else
            {
                TCPtoUDPForwader TCPForwarder = new TCPtoUDPForwader();
                TCPForwarder.ForwardCommand = aMessage.SourcePort;
                TCPForwarder.SourceInterleavedVideo = transport.Interleaved.First;
                TCPForwarder.ForwardInterleavedCommand = transport.Interleaved.Second;
                // we need to transfer already getted values
                TCPForwarder.ForwardHostVideo = forwarder.ForwardHostVideo;
                TCPForwarder.ForwardPortVideo = forwarder.ForwardPortVideo;
                TCPForwarder.SourcePortCommand = forwarder.SourcePortCommand;
                TCPForwarder.ToMulticast = forwarder.ToMulticast;

                resultForwarder = TCPForwarder;
            }

            if (resultForwarder.ToMulticast)
            {
                // Setup port and destination multicast.
                resultForwarder.ForwardHostVideo = CreateNextMulticastAddress();
                resultForwarder.ForwardPortVideo = forwarder.FromForwardVideoPort;

                RtspTransport newTransport = new RtspTransport()
                {
                    IsMulticast = true,
                    Destination = resultForwarder.ForwardHostVideo,
                    Port = new PortCouple(resultForwarder.ForwardPortVideo, resultForwarder.ListenCommandPort)
                };
                if ((resultForwarder is UDPForwarder && forwarder.ForwardPortCommand == 0)
                  || (resultForwarder is TCPtoUDPForwader && (resultForwarder as TCPtoUDPForwader).ForwardInterleavedCommand == 0))
                {
                    newTransport.Port = null;
                }
                aMessage.Headers[RtspHeaderNames.Transport] = newTransport.ToString();
            }
            else
            {
                RtspTransport newTransport = new RtspTransport()
                {
                    IsMulticast = false,
                    Destination = forwarder.ForwardHostVideo,
                    ClientPort = new PortCouple(resultForwarder.ForwardPortVideo, resultForwarder.SourcePortCommand),
                    ServerPort = new PortCouple(resultForwarder.FromForwardVideoPort, resultForwarder.ListenCommandPort)
                };
                if ((resultForwarder is UDPForwarder && forwarder.ForwardPortCommand == 0)
                  || (resultForwarder is TCPtoUDPForwader && (resultForwarder as TCPtoUDPForwader).ForwardInterleavedCommand == 0))
                {
                    newTransport.ServerPort = null;
                }
                aMessage.Headers[RtspHeaderNames.Transport] = newTransport.ToString();
            }

            return resultForwarder;
        }


        private static uint _multicastAddress = ((uint)225 << 24) + 10;
        /// <summary>
        /// Create next available multicast address.
        /// </summary>
        /// <returns>a string representing a multicast address.</returns>
        private static string CreateNextMulticastAddress()
        {
            _multicastAddress++;
            return String.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}",
                (_multicastAddress >> 24) & 0xFF,
                (_multicastAddress >> 16) & 0xFF,
                (_multicastAddress >> 8) & 0xFF,
                (_multicastAddress >> 0) & 0xFF);
        }

        /// <summary>
        /// Rewrites the SDP in the message.
        /// </summary>
        /// <param name="aMessage">A message.</param>
        private static void RewriteSDPMessage(RtspMessage aMessage)
        {
            //System.Text.Encoding.

            Encoding sdpEncoding = null;

            try
            {
                if (aMessage.Headers.ContainsKey(RtspHeaderNames.ContentEncoding))
                {
                    Encoding.GetEncoding(aMessage.Headers[RtspHeaderNames.ContentEncoding]);
                }
            }
            catch (ArgumentException)
            {
            }

            //fall back to UTF-8
            if (sdpEncoding == null)
                sdpEncoding = Encoding.UTF8;


            string sdpFile = sdpEncoding.GetString(aMessage.Data);

            using (StringReader readsdp = new StringReader(sdpFile))
            {
                StringBuilder newsdp = new StringBuilder();

                string line = readsdp.ReadLine();
                while (line != null)
                {

                    if (line.Contains("a=control:rtsp://"))
                    {
                        string[] lineElement = line.Split(new char[] { ':' }, 2);
                        UriBuilder temp = new UriBuilder(lineElement[1]);
                        temp.Path = temp.Host + ":" + temp.Port.ToString(CultureInfo.InvariantCulture) + temp.Path;

                        string domainName = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName;
                        string hostName = Dns.GetHostName();
                        string fqdn = String.Empty;
                        if (!hostName.Contains(domainName))
                            fqdn = hostName + "." + domainName;
                        else
                            fqdn = hostName;

                        temp.Host = fqdn;
                        temp.Port = 8554;
                        line = lineElement[0] + ":" + temp.ToString();
                    }
                    if (line.Contains("c=IN IP4 "))
                    {
                        line = string.Format(CultureInfo.InvariantCulture, "c=IN IP4 {0}", CreateNextMulticastAddress());
                    }
                    newsdp.Append(line);
                    newsdp.Append("\r\n");
                    line = readsdp.ReadLine();
                }

                aMessage.Data = sdpEncoding.GetBytes(newsdp.ToString());
            }
            aMessage.AdjustContentLength();
        }


    }
}
