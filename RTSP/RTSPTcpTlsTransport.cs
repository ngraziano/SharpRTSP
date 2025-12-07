using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Rtsp
{
    /// <summary>
    /// TCP Connection with TLS for Rtsp
    /// </summary>
    public class RtspTcpTlsTransport : RtspTcpTransport
    {
        private readonly RemoteCertificateValidationCallback? _userCertificateValidationCallback;
        private readonly X509Certificate2? _serverCertificate;

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspTcpTlsTransport"/> class as a SSL/TLS Client
        /// </summary>
        /// <param name="tcpConnection">The underlying TCP connection.</param>
        /// <param name="userCertificateValidationCallback">The user certificate validation callback, <see langword="null"/> if default should be used.</param>
        public RtspTcpTlsTransport(TcpClient tcpConnection, RemoteCertificateValidationCallback? userCertificateValidationCallback = null) : base(tcpConnection)
        {
            _userCertificateValidationCallback = userCertificateValidationCallback;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspTcpTransport"/> class as a SSL/TLS Client.
        /// </summary>
        /// <param name="uri">The RTSP uri to connect to.</param>
        /// <param name="userCertificateValidationCallback">The user certificate validation callback, <see langword="null"/> if default should be used.</param>
        public RtspTcpTlsTransport(Uri uri, RemoteCertificateValidationCallback? userCertificateValidationCallback)
            : this(new TcpClient(uri.Host, uri.Port), userCertificateValidationCallback)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspTcpTlsTransport"/> class as a SSL/TLS Server with a certificate.
        /// </summary>
        /// <param name="tcpConnection">The underlying TCP connection.</param>
        /// <param name="certificate">The certificate for the TLS Server.</param>
        /// <param name="userCertificateValidationCallback">The user certificate validation callback, <see langword="null"/> if default should be used.</param>
        public RtspTcpTlsTransport(TcpClient tcpConnection, X509Certificate2 certificate, RemoteCertificateValidationCallback? userCertificateValidationCallback = null)
            : this(tcpConnection, userCertificateValidationCallback)
        {
            _serverCertificate = certificate;
        }

        /// <summary>
        /// Gets the stream of the transport.
        /// </summary>
        /// <returns>A stream</returns>
        public override Stream GetStream()
        {
            var sslStream = new SslStream(base.GetStream(), leaveInnerStreamOpen: true, _userCertificateValidationCallback);

            // Use presence of server certificate to select if this is the SSL/TLS Server or the SSL/TLS Client
            if (_serverCertificate is not null)
            {
                sslStream.AuthenticateAsServer(
                    _serverCertificate,
                    clientCertificateRequired: false,
                    SslProtocols.Tls12,
                    checkCertificateRevocation: false);
            }
            else
            {
                sslStream.AuthenticateAsClient(RemoteEndPoint.ToString());
            }
            return sslStream;
        }
    }
}
