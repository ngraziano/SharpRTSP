using System.Threading;
using System.Threading.Tasks;

namespace Rtsp;

/// <summary>
/// Interface for a RTSP server listening to a socket
/// </summary>
public interface IRtspListenSocket
{
    /// <summary>
    /// Accept a new connection
    /// </summary>
    /// <returns>Connection accepeted</returns>
    Task<IRtspTransport> AcceptAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Start listening
    /// </summary>
    void Start();

    /// <summary>
    /// Stop listening
    /// </summary>
    void Stop();
}