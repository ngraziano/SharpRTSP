namespace Rtsp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Sockets;


public class RtspListenSocket : IRtspListenSocket
{
    private readonly TcpListener _tcpListener;
    private readonly ILogger _logger;

    public RtspListenSocket(TcpListener tcpListener, ILoggerFactory? loggerFactory = null)
    {
        _tcpListener = tcpListener;
        _logger = loggerFactory?.CreateLogger< RtspListenSocket>() as ILogger ?? NullLogger.Instance;
    }

    public IRtspTransport Accept()
    {
        var client = _tcpListener.AcceptTcpClient();
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