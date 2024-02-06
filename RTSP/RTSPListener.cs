namespace Rtsp
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using Rtsp.Messages;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Rtsp lister
    /// </summary>
    public class RtspListener : IDisposable
    {
        private readonly ILogger _logger;
        private readonly IRtspTransport _transport;
        private readonly Dictionary<int, RtspRequest> _sentMessage = [];

        private CancellationTokenSource? _cancelationTokenSource;
        private Task? _mainTask;
        private Stream _stream;

        private int _sequenceNumber;

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspListener"/> class from a TCP connection.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="logger">Logger</param>
        public RtspListener(IRtspTransport connection, ILogger<RtspListener>? logger = null)
        {
            _logger = logger as ILogger ?? NullLogger.Instance;

            _transport = connection ?? throw new ArgumentNullException(nameof(connection));
            _stream = connection.GetStream();
        }

        /// <summary>
        /// Gets the remote address.
        /// </summary>
        /// <value>The remote adress.</value>
        public string RemoteAdress => _transport.RemoteAddress;

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            _cancelationTokenSource = new();
            _mainTask = Task.Factory.StartNew(async () => await DoJobAsync(_cancelationTokenSource.Token).ConfigureAwait(false),
                _cancelationTokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public void Stop()
        {
            // brutally  close the TCP socket....
            // I hope the teardown was sent elsewhere
            _transport.Close();
            _cancelationTokenSource?.Cancel();
        }

        /// <summary>
        /// Enable auto reconnect.
        /// </summary>
        public bool AutoReconnect { get; set; }

        /// <summary>
        /// Occurs when message is received.
        /// </summary>
        public event EventHandler<RtspChunkEventArgs>? MessageReceived;

        /// <summary>
        /// Raises the <see cref="E:MessageReceived"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnMessageReceived(RtspChunkEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <summary>
        /// Occurs when Data is received.
        /// </summary>
        public event EventHandler<RtspChunkEventArgs>? DataReceived;

        /// <summary>
        /// Raises the <see cref="E:DataReceived"/> event.
        /// </summary>
        /// <param name="rtspChunkEventArgs">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnDataReceived(RtspChunkEventArgs rtspChunkEventArgs)
        {
            DataReceived?.Invoke(this, rtspChunkEventArgs);
        }

        /// <summary>
        /// Does the reading job.
        /// </summary>
        /// <remarks>
        /// This method read one message from TCP connection.
        /// If it a response it add the associate question.
        /// The stopping is made by the closing of the TCP connection.
        /// </remarks>
        private async Task DoJobAsync(CancellationToken token)
        {
            try
            {
                _logger.LogDebug("Connection Open");
                while (_transport.Connected && !token.IsCancellationRequested)
                {
                    // La lectuer est blocking sauf si la connection est coupé
                    RtspChunk? currentMessage = await ReadOneMessageAsync(_stream, token).ConfigureAwait(false);

                    if (currentMessage is not null)
                    {
                        if (_logger.IsEnabled(LogLevel.Debug) && currentMessage is not RtspData)
                        {
                            // on logue le tout
                            if (currentMessage.SourcePort != null)
                                _logger.LogDebug("Receive from {remoteAdress}", currentMessage.SourcePort.RemoteAdress);
                            _logger.LogDebug("{message}", currentMessage);
                        }
                        switch (currentMessage)
                        {
                            case RtspResponse response:
                                lock (_sentMessage)
                                {
                                    // add the original question to the response.
                                    if (_sentMessage.TryGetValue(response.CSeq, out var originalRequest))
                                    {
                                        _sentMessage.Remove(response.CSeq);
                                        response.OriginalRequest = originalRequest;
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Receive response not asked {cseq}", response.CSeq);
                                    }
                                }
                                OnMessageReceived(new RtspChunkEventArgs(response));
                                break;

                            case RtspRequest:
                                OnMessageReceived(new RtspChunkEventArgs(currentMessage));
                                break;
                            case RtspData:
                                OnDataReceived(new RtspChunkEventArgs(currentMessage));
                                break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (IOException error)
            {
                _logger.LogWarning(error, "IO Error");
            }
            catch (SocketException error)
            {
                _logger.LogWarning(error, "Socket Error");
            }
            catch (ObjectDisposedException error)
            {
                _logger.LogWarning(error, "Object Disposed");
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "Unknow Error");
                //                throw;
            }
            finally
            {
                _stream.Close();
                _transport.Close();
            }

            _logger.LogDebug("Connection Close");
        }

        [Serializable]
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
        /// <param name="message">A message.</param>
        /// <returns><see cref="true"/> if it is Ok, otherwise <see cref="false"/></returns>
        public bool SendMessage(RtspMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if (!AutoReconnect)
                    return false;

                _logger.LogWarning("Reconnect to a client, strange !!");
                try
                {
                    Reconnect();
                }
                catch (SocketException)
                {
                    // on a pas put se connecter on dit au manager de plus compter sur nous
                    return false;
                }
            }

            // if it it a request  we store the original message
            // and we renumber it.
            //TODO handle lost message (for example every minute cleanup old message)
            if (message is RtspRequest originalMessage)
            {
                // Do not modify original message
                message = (RtspMessage)message.Clone();
                _sequenceNumber++;
                message.CSeq = _sequenceNumber;
                lock (_sentMessage)
                {
                    _sentMessage.Add(message.CSeq, originalMessage);
                }
            }

            _logger.LogDebug("Send Message\n {message}", message);
            if (_transport is RtspHttpTransport httpTransport)
            {
                byte[] data = message.Prepare();
                httpTransport.Write(data, 0, data.Length);
            }
            else
            {
                message.SendTo(_stream);
            }
            return true;
        }

        /// <summary>
        /// Reconnect this instance of RtspListener.
        /// </summary>
        /// <exception cref="System.Net.Sockets.SocketException">Error during socket </exception>
        public void Reconnect()
        {
            //if it is already connected do not reconnect
            if (_transport.Connected)
                return;

            // If it is not connected listenthread should have die.
            _mainTask?.Wait();

            _stream?.Dispose();

            // reconnect 
            _transport.Reconnect();
            _stream = _transport.GetStream();

            // If listen thread exist restart it
            if (_mainTask != null)
                Start();
        }

        /// <summary>
        /// Reads one message.
        /// </summary>
        /// <param name="commandStream">The Rtsp stream.</param>
        /// <returns>Message readen</returns>
        public async Task<RtspChunk?> ReadOneMessageAsync(Stream commandStream, CancellationToken token)
        {
            if (commandStream == null)
                throw new ArgumentNullException(nameof(commandStream));
            Contract.EndContractBlock();

            ReadingState currentReadingState = ReadingState.NewCommand;
            // current decode message , create a fake new to permit compile.
            RtspChunk? currentMessage = null;

            int size = 0;
            int byteReaden = 0;
            var buffer = new List<byte>(256);
            string oneLine = string.Empty;
            while (currentReadingState != ReadingState.End)
            {
                // if the system is not reading binary data.
                if (currentReadingState != ReadingState.Data && currentReadingState != ReadingState.MoreInterleavedData)
                {
                    oneLine = string.Empty;
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
                                oneLine = Encoding.UTF8.GetString(buffer.ToArray());
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
                                {
                                    goto default;
                                }

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
                        currentMessage = RtspMessage.GetRtspMessage(oneLine);
                        currentReadingState = ReadingState.Headers;
                        break;
                    case ReadingState.Headers:
                        string line = oneLine;
                        if (string.IsNullOrEmpty(line))
                        {
                            currentReadingState = ReadingState.Data;
                            ((RtspMessage)currentMessage!).InitialiseDataFromContentLength();
                        }
                        else
                        {
                            ((RtspMessage)currentMessage!).AddHeader(line);
                        }
                        break;
                    case ReadingState.Data when currentMessage is not null:
                        if (!currentMessage.Data.IsEmpty)
                        {
                            // Read the remaning data
                            int byteCount = await commandStream.ReadAsync(currentMessage.Data[byteReaden..], token).ConfigureAwait(false);
                            if (byteCount <= 0)
                            {
                                currentReadingState = ReadingState.End;
                                break;
                            }
                            byteReaden += byteCount;
                            _logger.LogDebug("Readen {byteReaden} byte of data", byteReaden);
                        }
                        // if we haven't read all go there again else go to end. 
                        if (byteReaden >= currentMessage.Data.Length)
                            currentReadingState = ReadingState.End;
                        break;
                    case ReadingState.InterleavedData:
                        currentMessage = new RtspData();
                        int channelByte = commandStream.ReadByte();
                        if (channelByte == -1)
                        {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        ((RtspData)currentMessage).Channel = channelByte;

                        int sizeByte1 = commandStream.ReadByte();
                        if (sizeByte1 == -1)
                        {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        int sizeByte2 = commandStream.ReadByte();
                        if (sizeByte2 == -1)
                        {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        size = (sizeByte1 << 8) + sizeByte2;
                        currentMessage.Data = new byte[size];
                        currentReadingState = ReadingState.MoreInterleavedData;
                        break;
                    case ReadingState.MoreInterleavedData when currentMessage is not null:
                        // apparently non blocking
                        {
                            int byteCount = await commandStream.ReadAsync(currentMessage.Data[byteReaden..], token).ConfigureAwait(false);
                            if (byteCount <= 0)
                            {
                                currentReadingState = ReadingState.End;
                                break;
                            }
                            byteReaden += byteCount;
                            if (byteReaden < size)
                                currentReadingState = ReadingState.MoreInterleavedData;
                            else
                                currentReadingState = ReadingState.End;
                            break;
                        }
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
        /// <param name="aRtspData">A Rtsp data.</param>
        /// <param name="asyncCallback">The async callback.</param>
        /// <param name="state">A state.</param>
        public IAsyncResult? BeginSendData(RtspData aRtspData, AsyncCallback asyncCallback, object state)
        {
            if (aRtspData is null)
                throw new ArgumentNullException(nameof(aRtspData));
            if (aRtspData.Data.IsEmpty)
                throw new ArgumentException("no data present", nameof(aRtspData));

            Contract.EndContractBlock();

            return BeginSendData(aRtspData.Channel, aRtspData.Data.Span, asyncCallback, state);
        }

        /// <summary>
        /// Begins the send data.
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        /// <param name="asyncCallback">The async callback.</param>
        /// <param name="state">A state.</param>
        public IAsyncResult? BeginSendData(int channel, ReadOnlySpan<byte> frame, AsyncCallback asyncCallback, object state)
        {
            if (frame.IsEmpty)
                throw new ArgumentNullException(nameof(frame));
            if (frame.Length > 0xFFFF)
                throw new ArgumentException("frame too large", nameof(frame));
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if (!AutoReconnect)
                    return null; // cannot write when transport is disconnected

                _logger.LogWarning("Reconnect to a client, strange.");
                Reconnect();
            }

            Span<byte> data = new byte[4 + frame.Length]; // add 4 bytes for the header
            data[0] = 36; // '$' character
            data[1] = (byte)channel;
            data[2] = (byte)((frame.Length & 0xFF00) >> 8);
            data[3] = (byte)(frame.Length & 0x00FF);
            frame.CopyTo(data[4..]);
            //Array.Copy(frame.Span, 0, data, 4, frame.Length);
            return _stream.BeginWrite(data.ToArray(), 0, data.Length, asyncCallback, state);
        }

        /// <summary>
        /// Ends the send data.
        /// </summary>
        /// <param name="result">The result.</param>
        public void EndSendData(IAsyncResult result)
        {
            try
            {
                _stream.EndWrite(result);
            }
            catch (Exception e)
            {
                // Error, for example stream has already been Disposed
                _logger.LogDebug(e, "Error during end send (can be ignored) ");
            }
        }

        /// <summary>
        /// Send data (Synchronous)
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        public void SendData(int channel, byte[] frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.Length > 0xFFFF)
                throw new ArgumentException("frame too large", nameof(frame));
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if (!AutoReconnect)
                    throw new Exception("Connection is lost");

                _logger.LogWarning("Reconnect to a client, strange.");
                Reconnect();
            }

            byte[] data = new byte[4 + frame.Length]; // add 4 bytes for the header
            data[0] = 36; // '$' character
            data[1] = (byte)channel;
            data[2] = (byte)((frame.Length & 0xFF00) >> 8);
            data[3] = (byte)(frame.Length & 0x00FF);
            Array.Copy(frame, 0, data, 4, frame.Length);
            lock (_stream)
            {
                _stream.Write(data, 0, data.Length);
            }
        }

        /// <summary>
        /// Send data (Synchronous)
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        public void SendData(int channel, ReadOnlySpan<byte> frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.Length > 0xFFFF)
                throw new ArgumentException("frame too large", nameof(frame));
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if (!AutoReconnect)
                    throw new Exception("Connection is lost");

                _logger.LogWarning("Reconnect to a client, strange.");
                Reconnect();
            }

            byte[] data = new byte[4 + frame.Length]; // add 4 bytes for the header
            data[0] = 36; // '$' character
            data[1] = (byte)channel;
            data[2] = (byte)((frame.Length & 0xFF00) >> 8);
            data[3] = (byte)(frame.Length & 0x00FF);
            frame.CopyTo(data.AsSpan(4));
            lock (_stream)
            {
                _stream.Write(data, 0, data.Length);
            }
        }
        /// <summary>
        /// Send data (Synchronous)
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        public void SendData(int channel, ReadOnlyMemory<byte> frame) => SendData(channel, frame.Span);

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
                Stop();
                _stream?.Dispose();
            }
        }

        #endregion
    }
}
