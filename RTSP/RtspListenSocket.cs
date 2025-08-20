namespace Rtsp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class RtspListenSocket : IRtspListenSocket
{
    private readonly TcpListener _tcpListener;
    private readonly ILogger _logger;

    public RtspListenSocket(TcpListener tcpListener, ILoggerFactory? loggerFactory = null)
    {
        _tcpListener = tcpListener;
        _logger = loggerFactory?.CreateLogger<RtspListenSocket>() as ILogger ?? NullLogger.Instance;
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
        return new RtspTcpTransport(client);
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