using Rtsp.Messages;
using System;
using System.Buffers;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace Rtsp
{
    public class RtspHttpTransport : IRtspTransport, IDisposable
    {
        private const int MaxResponseHeadersSize = 8 * 1024;
        private static readonly byte[] DoubleCrlfBytes = "\r\n\r\n"u8.ToArray();

        private class HttpTransportStream : Stream
        {
            private readonly Stream _inStream;
            private readonly string _sessionCookie = Guid.NewGuid().ToString("N")[..10];
            private readonly RtspHttpTransport _parent;
            private TcpClient? _outClient;
            private readonly MemoryStream _sendBuffer = new();

            public HttpTransportStream(RtspHttpTransport parent)
            {
                _inStream = parent._dataClient!.GetStream();
                _parent = parent;
            }

            internal bool Open()
            {
                string request = _parent.ComposeGetRequest(_sessionCookie);
                byte[] requestByte = Encoding.ASCII.GetBytes(request);
                _inStream.Write(requestByte);

                byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxResponseHeadersSize);
                int read = ReadUntilEndOfHeaders(_inStream, buffer, MaxResponseHeadersSize);

                using MemoryStream ms = new(buffer, 0, read);
                using StreamReader streamReader = new(ms, Encoding.ASCII);

                // Parse first HTTP response line
                string? responseLine = streamReader.ReadLine();
                if (string.IsNullOrEmpty(responseLine)) { throw new HttpBadResponseException("Empty response"); }

                string[] tokens = responseLine.Split(' ', 3);
                if (tokens.Length != 3) { throw new HttpRequestException("Invalid first response line"); }

                HttpStatusCode statusCode = (HttpStatusCode)int.Parse(tokens[1], NumberStyles.Integer, NumberFormatInfo.InvariantInfo);
                if (statusCode == HttpStatusCode.OK) { return true; }

                if (statusCode == HttpStatusCode.Unauthorized && !_parent._credentials.IsEmpty() && _parent._authentication is null)
                {
                    NameValueCollection headers = HeadersParser.ParseHeaders(streamReader);
                    string? authenticateHeader = headers.Get(RtspHeaderNames.WWWAuthenticate);

                    if (string.IsNullOrEmpty(authenticateHeader))
                        throw new HttpBadResponseCodeException(statusCode);

                    _parent._authentication = Authentication.Create(_parent._credentials, authenticateHeader);

                    return false;
                }

                throw new HttpBadResponseCodeException(statusCode);
            }

            public override bool CanRead => _inStream.CanRead;

            public override bool CanSeek => false;

            // we can write when read stream is available because we reconnect if necessary
            public override bool CanWrite => _inStream.CanRead;

            public override long Length => throw new NotSupportedException("Not supported in network");

            public override long Position
            {
                get => throw new NotSupportedException("Not supported in network");
                set => throw new NotSupportedException("Not supported in network");
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Not supported in network");

            public override void SetLength(long value) => throw new NotSupportedException("Not supported in network");

            public override void Flush()
            {
                if (_outClient?.Connected != true)
                {
                    _outClient?.Dispose();
                    _outClient = new TcpClient();
                    _outClient.Connect(_parent._uri.Host, _parent._uri.Port);

                    string base64CodedCommandString = Convert.ToBase64String(_sendBuffer.ToArray());
                    byte[] base64CommandBytes = Encoding.ASCII.GetBytes(base64CodedCommandString);

                    string request = _parent.ComposePostRequest(_sessionCookie, base64CommandBytes);
                    byte[] requestBytes = Encoding.ASCII.GetBytes(request);

                    _outClient.GetStream().Write(requestBytes);
                    _outClient.GetStream().Write(base64CommandBytes);
                }
                else
                {
                    string base64CodedCommandString = Convert.ToBase64String(_sendBuffer.ToArray());
                    byte[] base64CommandBytes = Encoding.ASCII.GetBytes(base64CodedCommandString);
                    _outClient.GetStream().Write(base64CommandBytes);
                }

                _sendBuffer.SetLength(0);
            }

            public override int Read(byte[] buffer, int offset, int count) => _inStream.Read(buffer, offset, count);

            public override void Write(byte[] buffer, int offset, int count) => _sendBuffer.Write(buffer, offset, count);

            private static int ReadUntilEndOfHeaders(Stream stream, byte[] buffer, int length)
            {
                int offset = 0;
                int totalRead = 0;

                while (true)
                {
                    int count = length - totalRead;

                    if (count == 0)
                        throw new InvalidOperationException($"Response is too large (> {length / 1024} KB)");

                    int read = stream.Read(buffer, offset, count);

                    if (read == 0)
                        throw new EndOfStreamException("End of http stream");

                    totalRead += read;

                    int startIndex = Math.Max(0, offset - (DoubleCrlfBytes.Length - 1));
                    if (buffer.AsSpan()[startIndex..totalRead].IndexOf(DoubleCrlfBytes) != -1)
                    {
                        return totalRead;
                    }

                    offset += read;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inStream.Dispose();
                    _outClient?.Dispose();
                    _sendBuffer.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private readonly NetworkCredential _credentials;
        private readonly Uri _uri;

        private TcpClient? _dataClient;

        private HttpTransportStream? _stream;
        private Authentication? _authentication;
        private uint _commandCounter;
        private bool disposedValue;

        public RtspHttpTransport(Uri uri, NetworkCredential credentials)
        {
            _credentials = credentials;
            _uri = uri;
            Reconnect();
            if (_dataClient is null)
            {
                throw new InvalidOperationException("The HTTP client could not be opened.");
            }
            LocalEndPoint = _dataClient.Client.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("The local endpoint can not be determined.");
            RemoteEndPoint = _dataClient.Client.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException("The remote endpoint can not be determined.");
        }

        public string RemoteAddress => _uri.ToString();

        public IPEndPoint LocalEndPoint { get; }
        public IPEndPoint RemoteEndPoint { get; }

        public bool Connected => _dataClient?.Connected == true;

        public uint NextCommandIndex() => ++_commandCounter;

        public void Close()
        {
            _stream?.Close();
            _dataClient?.Close();
        }

        public Stream GetStream()
        {
            if (_dataClient?.Connected != true || _stream is null)
                throw new InvalidOperationException("Client is not connected");

            return _stream;
        }

        public void Reconnect()
        {
            if (Connected) return;
            _commandCounter = 0;
            int retry = 0;
            do
            {
                // retry if need authentication
                _dataClient = new TcpClient();
                _dataClient.Connect(_uri.Host, _uri.Port);
                _stream = new HttpTransportStream(this);
                retry++;
            }
            while (!_stream.Open() && retry < 2);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Close();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private string GetAuthorizationHeader(uint counter, string method, byte[] requestBytes)
        {
            if (_authentication == null)
            {
                return string.Empty;
            }

            string headerValue = _authentication.GetResponse(counter, _uri.PathAndQuery, method, requestBytes);
            return $"Authorization: {headerValue}\r\n";
        }

        private string ComposeGetRequest(string sessionCookie)
        {
            string authorizationHeader = GetAuthorizationHeader(NextCommandIndex(), "GET", []);

            StringBuilder sb = new();
            sb.AppendLine($"GET {_uri.PathAndQuery} HTTP/1.0");
            sb.AppendLine($"x-sessioncookie: {sessionCookie}");
            if (!string.IsNullOrEmpty(authorizationHeader)) { sb.AppendLine(authorizationHeader); }
            sb.AppendLine();
            return sb.ToString();
        }

        private string ComposePostRequest(string sessionCookie, byte[] commandBytes)
        {
            string authorizationHeader = GetAuthorizationHeader(NextCommandIndex(), "POST", commandBytes);

            StringBuilder sb = new();
            sb.AppendLine($"POST {_uri.PathAndQuery} HTTP/1.0");
            sb.AppendLine($"x-sessioncookie: {sessionCookie}");
            sb.AppendLine("Content-Type: application/x-rtsp-tunnelled");
            sb.AppendLine("Content-Length: 32767");
            if (!string.IsNullOrEmpty(authorizationHeader)) { sb.AppendLine(authorizationHeader); }
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
