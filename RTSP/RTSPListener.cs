using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Net;
using RTSP.Messages;
using System.Diagnostics.Contracts;

namespace RTSP
{
    /// <summary>
    /// RTSP lister
    /// </summary>
    public class RTSPListener
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        IRTSPTransport _transport;

        private Thread _ListenTread;
        Stream _stream;

        private int _sequenceNumber = 0;

        private Dictionary<int, RTSPRequest> _sentMessage = new Dictionary<int, RTSPRequest>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPListener"/> class from a TCP connection.
        /// </summary>
        /// <param name="connection">The connection.</param>
        public RTSPListener(IRTSPTransport connection)
        {
            if (connection == null)
                throw new ArgumentNullException("connection");
            Contract.EndContractBlock();

            _transport = connection;
            _stream = connection.GetStream();
        }

        /// <summary>
        /// Gets the remote address.
        /// </summary>
        /// <value>The remote adress.</value>
        public string RemoteAdress
        {
            get
            {
                return _transport.RemoteAddress;
            }
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            _ListenTread = new Thread(new ThreadStart(DoJob));
            _ListenTread.Start();
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public void Stop()
        {
            // brutally  close the TCP socket....
            // I hope the teardown was sent elsewhere
            _transport.Close();

        }

        public delegate void RTSPMessageEvent(object sender, RTSPChunkEventArgs e);

        /// <summary>
        /// Occurs when message is received.
        /// </summary>
        public event RTSPMessageEvent MessageReceived;

        /// <summary>
        /// Raises the <see cref="E:MessageReceived"/> event.
        /// </summary>
        /// <param name="e">The <see cref="RTSP.RTSPChunkEventArgs"/> instance containing the event data.</param>
        protected void OnMessageReceived(RTSPChunkEventArgs e)
        {
            RTSPMessageEvent handler = MessageReceived;

            if (handler != null)
                handler(this, e);
        }

        /// <summary>
        /// Occurs when message is received.
        /// </summary>
        public event RTSPMessageEvent DataReceived;

        /// <summary>
        /// Raises the <see cref="E:DataReceived"/> event.
        /// </summary>
        /// <param name="aRTSPChunkEventArgs">The <see cref="RTSP.RTSPChunkEventArgs"/> instance containing the event data.</param>
        protected void OnDataReceived(RTSPChunkEventArgs aRTSPChunkEventArgs)
        {
            RTSPMessageEvent handler = DataReceived;

            if (handler != null)
                handler(this, aRTSPChunkEventArgs);
        }

        /// <summary>
        /// Does the reading job.
        /// </summary>
        /// <remarks>
        /// This method read one message from TCP connection.
        /// If it a response it add the associate question.
        /// The sopping is made by the closing of the TCP connection.
        /// </remarks>
        private void DoJob()
        {
            try
            {
                _logger.Debug("Connection Open");
                while (_transport.Connected)
                {
                    // La lectuer est blocking sauf si la connection est coupé
                    RTSPChunk currentMessage = ReadOneMessage(_stream);

                    if (currentMessage != null)
                    {
                        if (!(currentMessage is RTSPData))
                        {
                            // on logue le tout
                            if (currentMessage.SourcePort != null)
                                _logger.Debug("Receive from {0}", currentMessage.SourcePort.RemoteAdress);
                            currentMessage.LogMessage();
                        }
                        if (currentMessage is RTSPResponse)
                        {

                            RTSPResponse response = currentMessage as RTSPResponse;
                            lock (_sentMessage)
                            {
                                // add the original question to the response.
                                RTSPRequest originalRequest;
                                if (_sentMessage.TryGetValue(response.CSeq, out originalRequest))
                                {
                                    _sentMessage.Remove(response.CSeq);
                                    response.OriginalRequest = originalRequest;
                                    // restore the original sequence number.
                                    response.CSeq = originalRequest.CSeq;
                                }
                                else
                                {
                                    _logger.Warn("Receive response not asked {0}", response.CSeq);
                                }
                            }
                            OnMessageReceived(new RTSPChunkEventArgs(response));

                        }
                        else if (currentMessage is RTSPRequest)
                        {
                            OnMessageReceived(new RTSPChunkEventArgs(currentMessage));
                        }
                        else if (currentMessage is RTSPData)
                        {
                            OnDataReceived(new RTSPChunkEventArgs(currentMessage));
                        }

                    }
                    else
                    {
                        _stream.Close();
                        _transport.Close();
                    }
                }
                _logger.Debug("Connection Close");
            }
            catch (IOException error)
            {
                _logger.Warn("IO Error", error);
                _stream.Close();
                _transport.Close();
            }
            catch (SocketException error)
            {
                _logger.Warn("Socket Error", error);
                _stream.Close();
                _transport.Close();
            }
            catch (Exception error)
            {
                _logger.Warn("Unknow Error", error);
            }
        }

        private enum ReadingState
        {
            NewCommand,
            Headers,
            Data,
            End,
            InterleavedData,
            MoreInterleavedData,
        }

        /// <summary>
        /// Sends the message.
        /// </summary>
        /// <param name="aMessage">A message.</param>
        /// <returns><see cref="true"/> if it is Ok, otherwise <see cref="false"/></returns>
        public bool SendMessage(RTSPMessage aMessage)
        {
            if (aMessage == null)
                throw new ArgumentNullException("aMessage");
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                _logger.Warn("Reconnect to a client, strange !!");
                try
                {
                    ReConnect();
                }
                catch (Exception)
                {
                    // on a pas put se connecter on dit au manager de plus compter sur nous
                    return false;
                }
            }

            // if it it a request  we store the original message
            // and we renumber it.
            //TODO handle lost message (for example every minute cleanup old message)
            if (aMessage is RTSPRequest)
            {
                RTSPMessage originalMessage = aMessage;
                // Do not modify original message
                aMessage = aMessage.Clone() as RTSPMessage;
                _sequenceNumber++;
                aMessage.CSeq = _sequenceNumber;
                lock (_sentMessage)
                {
                    _sentMessage.Add(aMessage.CSeq, originalMessage as RTSPRequest);
                }
            }

            _logger.Debug("Send Message");
            aMessage.LogMessage();
            aMessage.SendTo(_stream);
            return true;
        }

        /// <summary>
        /// Reconnect this instance of RTSPListener.
        /// </summary>
        public void ReConnect()
        {
            //if it is already connected do not reconnect
            if (_transport.Connected)
                return;

            // If it is not connected listenthread should have die.
            if (_ListenTread != null && _ListenTread.IsAlive)
                _ListenTread.Join();

            // reconnect 
            _transport.ReConnect();
            _stream = _transport.GetStream();

            // If listen thread exist restart it
            if (_ListenTread != null)
                Start();
        }

        /// <summary>
        /// Reads one message.
        /// </summary>
        /// <param name="commandStream">The RTSP stream.</param>
        /// <returns>Message readen</returns>
        public RTSPChunk ReadOneMessage(Stream commandStream)
        {
            if (commandStream == null)
                throw new ArgumentNullException("commandStream");
            Contract.EndContractBlock();

            ReadingState currentReadingState = ReadingState.NewCommand;
            // current decode message , create a fake new to permit compile.
            RTSPChunk currentMessage = null;

            int size = 0;
            int byteReaden = 0;
            List<byte> buffer = new List<byte>(256);
            string oneLine = String.Empty;
            while (currentReadingState != ReadingState.End)
            {

                // if the system is not reading binary data.
                if (currentReadingState != ReadingState.Data && currentReadingState != ReadingState.MoreInterleavedData)
                {
                    oneLine = String.Empty;
                    bool needMoreChar = true;
                    // I do not know to make readline blocking
                    while (needMoreChar)
                    {
                        int currentByte = commandStream.ReadByte();

                        switch (currentByte)
                        {
                            case -1:
                                // the read is blocking, so if we got -1 it is because the client close;
                                currentReadingState = ReadingState.End;
                                needMoreChar = false;
                                break;
                            case '\n':
                                oneLine = ASCIIEncoding.UTF8.GetString(buffer.ToArray());
                                buffer.Clear();
                                needMoreChar = false;
                                break;
                            case '\r':
                                // simply ignore this
                                break;
                            case '$': // if first caracter of packet is $ it is an interleaved data packet
                                if (currentReadingState == ReadingState.NewCommand && buffer.Count == 0)
                                {
                                    currentReadingState = ReadingState.InterleavedData;
                                    needMoreChar = false;
                                }
                                else
                                    goto default;
                                break;
                            default:
                                buffer.Add((byte)currentByte);
                                break;
                        }
                    }
                }

                switch (currentReadingState)
                {
                    case ReadingState.NewCommand:
                        currentMessage = RTSPMessage.GetRTSPMessage(oneLine);
                        currentReadingState = ReadingState.Headers;
                        break;
                    case ReadingState.Headers:
                        string line = oneLine;
                        if (string.IsNullOrEmpty(line))
                        {
                            currentReadingState = ReadingState.Data;
                            ((RTSPMessage)currentMessage).InitialiseDataFromContentLength();
                        }
                        else
                        {
                            ((RTSPMessage)currentMessage).AddHeader(line);
                        }
                        break;
                    case ReadingState.Data:
                        if (currentMessage.Data.Length > 0)
                        {
                            // Read the remaning data
                            byteReaden += commandStream.Read(currentMessage.Data, byteReaden,
                                currentMessage.Data.Length - byteReaden);
                            _logger.Debug("Readen {0} byte of data", byteReaden);
                        }
                        // if we haven't read all go there again else go to end. 
                        if (byteReaden >= currentMessage.Data.Length)
                            currentReadingState = ReadingState.End;
                        break;
                    case ReadingState.InterleavedData:
                        currentMessage = new RTSPData();
                        ((RTSPData)currentMessage).Channel = commandStream.ReadByte();
                        size = (commandStream.ReadByte() << 8) + commandStream.ReadByte();
                        currentMessage.Data = new byte[size];
                        currentReadingState = ReadingState.MoreInterleavedData;
                        break;
                    case ReadingState.MoreInterleavedData:
                        // apparently non blocking
                        byteReaden += commandStream.Read(currentMessage.Data, byteReaden, size - byteReaden);
                        if (byteReaden < size)
                            currentReadingState = ReadingState.MoreInterleavedData;
                        else
                            currentReadingState = ReadingState.End;
                        break;
                    default:
                        break;
                }
            }
            if (currentMessage != null)
                currentMessage.SourcePort = this;
            return currentMessage;
        }

        /// <summary>
        /// Begins the send data.
        /// </summary>
        /// <param name="aRTSPData">A RTSP data.</param>
        /// <param name="asyncCallback">The async callback.</param>
        /// <param name="aState">A state.</param>
        public IAsyncResult BeginSendData(RTSPData aRTSPData, AsyncCallback asyncCallback, object aState)
        {
            if (aRTSPData == null)
                throw new ArgumentNullException("aRTSPData");
            Contract.EndContractBlock();

            return BeginSendData(aRTSPData.Channel, aRTSPData.Data, asyncCallback, aState);
        }

        /// <summary>
        /// Begins the send data.
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        /// <param name="asyncCallback">The async callback.</param>
        /// <param name="aState">A state.</param>
        public IAsyncResult BeginSendData(int channel, byte[] frame, AsyncCallback asyncCallback, object aState)
        {
            if (frame == null)
                throw new ArgumentNullException("frame");
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                _logger.Warn("Reconnect to a client, strange !!");
                ReConnect();
            }

            // $ in byte => 36
            _stream.WriteByte(36);
            _stream.WriteByte((byte)channel);
            int size = frame.Length;
            _stream.WriteByte((byte)((size & 0xFF00) >> 8));
            _stream.WriteByte((byte)(size & 0x00FF));
            return _stream.BeginWrite(frame, 0, size, asyncCallback, aState);
        }

        /// <summary>
        /// Ends the send data.
        /// </summary>
        /// <param name="result">The result.</param>
        public void EndSendData(IAsyncResult result)
        {
            _stream.EndWrite(result);
        }
    }
}
