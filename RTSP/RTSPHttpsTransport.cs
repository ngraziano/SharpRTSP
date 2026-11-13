namespace Rtsp;

using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;

[Obsolete("Will be remove in next main version, use RtspHttpsTransport instead")]
public class RTSPHttpsTransport(
    Uri uri,
    System.Net.NetworkCredential credentials,
    RemoteCertificateValidationCallback? userCertificateSelectionCallback = null) : RtspHttpsTransport(
    uri,
    credentials,
    userCertificateSelectionCallback)
{
}

public class RtspHttpsTransport(
    Uri uri,
    System.Net.NetworkCredential credentials,
    RemoteCertificateValidationCallback? userCertificateSelectionCallback = null)
    : RtspHttpTransport(uri, credentials)
{
    /// <summary>
    /// Gets the stream of one of the two connections of the tunnel, before any HTTP is written on it.
    /// </summary>
    /// <param name="client">The TCP connection.</param>
    /// <returns>The stream the HTTP requests and responses go over.</returns>
    protected override Stream OpenStream(TcpClient client)
    {
        var sslStream = new SslStream(client.GetStream(), false, userCertificateSelectionCallback);

        sslStream.AuthenticateAsClient(Uri.DnsSafeHost);
        return sslStream;
    }
}