using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RTSP.Messages
{
    public class RTSPRequestSetup : RTSPRequest
    {
        /// <summary>
        /// Gets the transports associate with the request.
        /// </summary>
        /// <value>The transport.</value>
        public RTSPTransport[] GetTransports()
        {

            if (!Headers.ContainsKey(RTSPHeaderNames.Transport))
                return new RTSPTransport[] { new RTSPTransport() };

            string[] items = Headers[RTSPHeaderNames.Transport].Split(',');
            return Array.ConvertAll<string, RTSPTransport>(items,
                new Converter<string, RTSPTransport>(RTSPTransport.Parse));

        }

    }
}
