using System;

namespace Rtsp
{
    public static class RtspUtils
    {
        /// <summary>
        /// Registers the URI.
        /// </summary>
        public static void RegisterUri()
        {
            if (!UriParser.IsKnownScheme("rtsp"))
                UriParser.Register(new HttpStyleUriParser(), "rtsp", 554);
        }
    }
}
