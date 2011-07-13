using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Messages
{
    public class RtspRequestSetup : RtspRequest
    {
        /// <summary>
        /// Gets the transports associate with the request.
        /// </summary>
        /// <value>The transport.</value>
        public RtspTransport[] GetTransports()
        {

            if (!Headers.ContainsKey(RtspHeaderNames.Transport))
                return new RtspTransport[] { new RtspTransport() };

            string[] items = Headers[RtspHeaderNames.Transport].Split(',');
            return Array.ConvertAll<string, RtspTransport>(items,
                new Converter<string, RtspTransport>(RtspTransport.Parse));

        }

    }
}
