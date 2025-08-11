namespace Rtsp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class RtspHttpServerTransport : IRtspTransport, IDisposable
{
    private class HttpTransportStream : Stream
    {
        private readonly Stream _outStream;
        private readonly RtspHttpServerTransport _parent;

        public HttpTransportStream(RtspHttpServerTransport parent)
        {
            Debug.Assert(parent._getChannelClient != null);
            _outStream = parent._getChannelClient!.GetStream();
            _parent = parent;
        }

        public override bool CanRead => _outStream.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => _outStream.CanWrite;

        public override long Length => throw new NotSupportedException("Not supported in network");

        public override long Position
        {
            get => throw new NotSupportedException("Not supported in network");
            set => throw new NotSupportedException("Not supported in network");
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Not supported in network");

        public override void SetLength(long value) => throw new NotSupportedException("Not supported in network");

        public override void Flush() => _outStream.Flush();

#if NET8_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var reader = _parent._decodedDataPipe.Reader;
            var result = await reader.ReadAtLeastAsync(buffer.Length, cancellationToken).ConfigureAwait(false);

            result.Buffer.Slice(0, buffer.Length).CopyTo(buffer.Span);
            var handlePart = result.Buffer.GetPosition(buffer.Length);
            reader.AdvanceTo(handlePart, handlePart);

            return buffer.Length;
        }

        // Remove a copy when possible
        public override void Write(ReadOnlySpan<byte> buffer) => _outStream.Write(buffer);
#endif

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var reader = _parent._decodedDataPipe.Reader;
            var result = await reader.ReadAtLeastAsync(count, cancellationToken).ConfigureAwait(false);

            result.Buffer.Slice(0, count).CopyTo(buffer.AsSpan(offset, count));
            var handlePart = result.Buffer.GetPosition(count);
            reader.AdvanceTo(handlePart, handlePart);

            return count;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, default).Result;
        }

        public override void Write(byte[] buffer, int offset, int count) => _outStream.Write(buffer, offset, count);
    }

    private readonly ILogger _logger;
    private TcpClient? _postChannelClient;
    private TcpClient? _getChannelClient;
    private Stream? _stream;
    private uint _commandCounter;
    private bool _disposedValue;
    private readonly Pipe _decodedDataPipe = new();
    private readonly CancellationTokenSource _stop = new();
    public readonly DateTime creationTime = DateTime.UtcNow;

    internal enum UpdateState
    {
        Ok,
        NewSession,
        Error,
    }

    public string RemoteAddress => RemoteEndPoint.ToString();

    public IPEndPoint RemoteEndPoint { get; private set; } = null!;

    public IPEndPoint LocalEndPoint { get; private set; } = null!;

    public bool Connected => _getChannelClient?.Connected == true;

    public bool IsObsolete
    {
        get
        {
            // Not fully initialized, it can live 5 minutes
            if (_getChannelClient is null || _postChannelClient is null)
            {
                return creationTime.AddMinutes(5) < DateTime.UtcNow;
            }
            return !_getChannelClient.Connected;
        }
    }

    internal RtspHttpServerTransport(ILogger<RtspHttpServerTransport>? logger)
    {
        _logger = logger as ILogger ?? NullLogger.Instance;
    }

    public void Close()
    {
        _stop.Cancel();
        _postChannelClient?.Close();
        _getChannelClient?.Close();
    }

    public Stream GetStream() => _stream ?? throw new InvalidOperationException("Invalid internal state");

    public uint NextCommandIndex() => ++_commandCounter;

    public void Reconnect() => throw new InvalidOperationException("Server can not reconnect to client");

    internal UpdateState UpdatePostChannel(TcpClient client)
    {
        _logger.LogDebug("New post channel detected");
        var wasPresent = _postChannelClient != null;
        _postChannelClient?.Close();

        _postChannelClient = client;
        _ = Task.Factory.StartNew(async () => await DecodePostChannel(_stop.Token).ConfigureAwait(false),
            _stop.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Current);

        if (!wasPresent && _getChannelClient != null)
        {
            return UpdateState.NewSession;
        }
        return UpdateState.Ok;
    }

    private async Task DecodePostChannel(CancellationToken token)
    {
        Debug.Assert(_postChannelClient != null);
        try
        {
            var pipeSource = PipeReader.Create(_postChannelClient!.GetStream());
            var pipeDest = _decodedDataPipe.Writer;
            while (!token.IsCancellationRequested)
            {

                var sourceReadResult = await pipeSource.ReadAsync(token).ConfigureAwait(false);
                var sourceBuffer = sourceReadResult.Buffer;

                var roundLength = (int)(sourceBuffer.Length / 4 * 4);
                // ask a buffer too big but it allow to use it as temporary buffer
                var decodedBuffer = pipeDest.GetSpan((int)sourceBuffer.Length);
                sourceBuffer.CopyTo(decodedBuffer);
                var decodeResult = Base64.DecodeFromUtf8InPlace(decodedBuffer[..roundLength], out int written);
                if (decodeResult == OperationStatus.Done)
                {
                    pipeDest.Advance(written);
                    var sourcePosition = sourceBuffer.GetPosition(roundLength);
                    pipeSource.AdvanceTo(sourcePosition, sourcePosition);
                    FlushResult flushResult = await pipeDest.FlushAsync(token).ConfigureAwait(false);
                    if (flushResult.IsCompleted)
                    {
                        // reader is closed
                        _logger.LogDebug("Dest channel close");
                        break;
                    }
                    if (sourceReadResult.IsCompleted)
                    {
                        _logger.LogDebug("Post Channel close");
                        // source tcp is closed
                        break;
                    }

                }
                else if (decodeResult != OperationStatus.NeedMoreData)
                {
                    _logger.LogWarning("Invalid data receive for base64, fail to decode post channel, data ={data}",
                        Encoding.UTF8.GetString(sourceBuffer.Slice(0, roundLength).ToArray()));
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Decode post channel canceled");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Error during post channel decode");
        }
        _postChannelClient?.Dispose();

    }

    internal UpdateState UpdateGetChannel(TcpClient client)
    {
        _logger.LogDebug("New get channel");
        if (_getChannelClient != null)
        {
            _logger.LogWarning("Get channel already present, fail");
            return UpdateState.Error;
        }
        _getChannelClient = client;
        _stream = new HttpTransportStream(this);
        RemoteEndPoint = _getChannelClient?.Client?.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException("The local endpoint can not be determined.");
        LocalEndPoint = _getChannelClient?.Client?.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("The local endpoint can not be determined.");


        if (_postChannelClient != null)
        {
            return UpdateState.NewSession;
        }
        return UpdateState.Ok;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Close();
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
