using System;

namespace Rtsp.Utils
{
    public static class RtspUtils
    {
        /// <summary>
        /// Registers the rtsp scheùe for uri.
        /// </summary>
        public static void RegisterUri()
        {
            if (!UriParser.IsKnownScheme("rtsp"))
                UriParser.Register(new HttpStyleUriParser(), "rtsp", 554);
        }
    }
}
