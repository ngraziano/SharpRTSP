using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;

namespace Rtsp;

public class RTSPHttpsTransport(Uri uri, System.Net.NetworkCredential credentials, RemoteCertificateValidationCallback? userCertificateSelectionCallback = null) : RtspHttpTransport(uri, credentials)
{
    // Initialized before the base constructor runs, which already opens the first connection.
    private readonly RemoteCertificateValidationCallback? _userCertificateSelectionCallback = userCertificateSelectionCallback;

    /// <summary>
    /// Gets the stream of one of the two connections of the tunnel, TLS from its first byte: the
    /// HTTP of the tunnel goes inside it.
    /// </summary>
    protected override Stream OpenStream(TcpClient client)
    {
        var sslStream = new SslStream(client.GetStream(), false, _userCertificateSelectionCallback);

        sslStream.AuthenticateAsClient(Uri.DnsSafeHost);
        return sslStream;
    }
}