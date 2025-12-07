using System;
using System.IO;
using System.Net.Security;

namespace Rtsp;

public class RTSPHttpsTransport(Uri uri, System.Net.NetworkCredential credentials, RemoteCertificateValidationCallback? userCertificateSelectionCallback = null) : RtspHttpTransport(uri, credentials)
{
    private readonly RemoteCertificateValidationCallback? _userCertificateSelectionCallback = userCertificateSelectionCallback;

    public override Stream GetStream()
    {
        var sslStream = new SslStream(base.GetStream(), true, _userCertificateSelectionCallback);

        sslStream.AuthenticateAsClient(Uri.Host);
        return sslStream;
    }
}