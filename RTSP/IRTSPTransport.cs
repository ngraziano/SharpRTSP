namespace Rtsp;

using System;
using System.Net;

/// <summary>
/// Interface for Transport of Rtsp (TCP, TCP+SSL,..)
/// </summary>
public interface IRtspTransport
{
    /// <summary>
    /// Gets the stream of the transport.
    /// </summary>
    /// <returns>A stream</returns>
    System.IO.Stream GetStream();

    [Obsolete("Get the address from the RemoteEndPoint instead.")]
    /// <summary>
    /// Gets the remote address.
    /// </summary>
    /// <value>The remote address.</value>
    /// <remarks>This property actually returns an IP:Port pair or a URI, depending on the underlying transport.</remarks>
    string RemoteAddress { get; }

    /// <summary>
    /// Gets the remote endpoint.
    /// </summary>
    /// <value>The remote endpoint.</value>
    IPEndPoint RemoteEndPoint { get; }

    /// <summary>
    /// Gets the remote endpoint.
    /// </summary>
    /// <value>The remote endpoint.</value>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>
    /// Get next command index. Increment at each call.
    /// </summary>
    uint NextCommandIndex();

    /// <summary>
    /// Closes this instance.
    /// </summary>
    void Close();

    /// <summary>
    /// Gets a value indicating whether this <see cref="IRtspTransport"/> is connected.
    /// </summary>
    /// <value><see langword="true"/> if connected; otherwise, <see langword="false"/>.</value>
    bool Connected { get; }

    /// <summary>
    /// Reconnect this instance.
    /// <remarks>Must do nothing if already connected.</remarks>
    /// </summary>
    /// <exception cref="System.Net.Sockets.SocketException">Error during socket </exception>
    void Reconnect();
}
