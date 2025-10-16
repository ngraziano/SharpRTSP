using System;
using System.Net;
using System.Net.Security;

namespace Rtsp
{
    public static class RtspUtils
    {
        /// <summary>
        /// Registers the rtsp scheùe for uri.
        /// </summary>
        public static void RegisterUri()
        {
            if (!UriParser.IsKnownScheme("rtsp"))
            {
                UriParser.Register(new HttpStyleUriParser(), "rtsp", 554);
            }
        }

        public static IRtspTransport CreateRtspTransportFromUrl(Uri uri, NetworkCredential networkCredential, RemoteCertificateValidationCallback? userCertificateSelectionCallback = null)
        {
            return uri.Scheme switch
            {
                "rtsp" => new RtspTcpTransport(uri),
                "rtsps" => new RtspTcpTlsTransport(uri, userCertificateSelectionCallback),
                "http" => new RtspHttpTransport(uri, networkCredential),
                "https" => new RTSPHttpsTransport(uri, networkCredential, userCertificateSelectionCallback),
                _ => throw new ArgumentException("The uri scheme is not supported", nameof(uri))
            };
        }
    }
}
