namespace Rtsp;

using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

public class RtspOverHttpTLSListenSocket : RtspOverHttpListenSocket
{
    private readonly X509Certificate2 _certificate;
    private readonly RemoteCertificateValidationCallback? _userCertificateValidationCallback;

    public RtspOverHttpTLSListenSocket(
        TcpListener tcpListener,
        X509Certificate2 certificate,
        RemoteCertificateValidationCallback? userCertificateValidationCallback = null,
        ILoggerFactory? loggerFactory = null)
        : base(tcpListener, loggerFactory)
    {
        _certificate = certificate;
        _userCertificateValidationCallback = userCertificateValidationCallback;
    }

    protected override Stream GetStream(TcpClient client)
    {
        var sslStream = new SslStream(
            client.GetStream(),
            leaveInnerStreamOpen: true,
            _userCertificateValidationCallback);

        sslStream.AuthenticateAsServer(
            _certificate,
            clientCertificateRequired: false,
            System.Security.Authentication.SslProtocols.Tls12,
            checkCertificateRevocation: false);

        return sslStream;
    }
}