namespace Rtsp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

public class RtspTlsListenSocket : IRtspListenSocket
{
    private readonly TcpListener _tcpListener;
    private readonly X509Certificate2 _certificate;
    private readonly RemoteCertificateValidationCallback? _userCertificateValidationCallback;
    private readonly ILogger _logger;

    public RtspTlsListenSocket(TcpListener tcpListener,
        X509Certificate2 certificate, RemoteCertificateValidationCallback? userCertificateValidationCallback = null,
        ILoggerFactory? loggerFactory = null)
    {
        _tcpListener = tcpListener;
        _logger = loggerFactory?.CreateLogger<RtspTlsListenSocket>() as ILogger ?? NullLogger.Instance;
        _certificate = certificate;
        _userCertificateValidationCallback = userCertificateValidationCallback;
    }

    public IRtspTransport Accept()
    {
        var client = _tcpListener.AcceptTcpClient();
        return new RtspTcpTlsTransport(client, _certificate, _userCertificateValidationCallback);
    }

    public void Start()
    {
        _tcpListener.Start();
    }

    public void Stop()
    {
        _tcpListener.Stop();
    }
}