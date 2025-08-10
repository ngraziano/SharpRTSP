namespace Rtsp;

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _outStream.Dispose();
                _parent.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private TcpClient? _postChannelClient;
    private TcpClient? _getChannelClient;
    private Stream? _stream;
    private uint _commandCounter;
    private bool _disposedValue;
    private readonly Pipe _decodedDataPipe = new();
    private readonly CancellationTokenSource _stop = new();


    internal enum UpdateState
    {
        Ok,
        NewSession,
        Error,
    }

    public string RemoteAddress => RemoteEndPoint.ToString();

    public IPEndPoint RemoteEndPoint => _getChannelClient?.Client.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException("The local endpoint can not be determined.");

    public IPEndPoint LocalEndPoint => _getChannelClient?.Client.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("The local endpoint can not be determined.");

    public bool Connected => _getChannelClient?.Connected == true;

    internal RtspHttpServerTransport() { }

    public void Close()
    {
        _postChannelClient?.Close();
        _getChannelClient?.Close();
    }

    public Stream GetStream() => _stream ?? throw new InvalidOperationException("Invalid internal state");

    public uint NextCommandIndex() => ++_commandCounter;

    public void Reconnect() => throw new InvalidOperationException("Server can not reconnect to client");

    internal UpdateState UpdatePostChannel(TcpClient client)
    {
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
                        break;
                    }
                    if (sourceReadResult.IsCompleted)
                    {
                        // source tcp is closed
                        break;
                    }

                }
                else if (decodeResult != OperationStatus.NeedMoreData)
                {
                    throw new InvalidOperationException("Fail to decode base64 post channel");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // IGNORE
        }
        catch (IOException)
        {
            // IGNORE
        }
        _postChannelClient?.Dispose();

    }

    internal UpdateState UpdateGetChannel(TcpClient client)
    {
        if (_getChannelClient != null) return UpdateState.Error;

        _getChannelClient = client;
        _stream = new HttpTransportStream(this);

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
