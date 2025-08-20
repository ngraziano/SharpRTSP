namespace Rtsp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

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

    public async Task<IRtspTransport> AcceptAsync(CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
#else
        TcpClient client;
        using (cancellationToken.Register(() => _tcpListener.Stop()))
        {
            client = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
        }
#endif
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