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
    IRtspTransport Accept();

    /// <summary>
    /// Start listening
    /// </summary>
    void Start();

    /// <summary>
    /// Stop listening
    /// </summary>
    void Stop();
}