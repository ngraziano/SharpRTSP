using System;
using System.Linq;

namespace Rtsp.Messages
{
    public class RtspRequestSetup : RtspRequest
    {
        public RtspRequestSetup()
        {
            Command = "SETUP * RTSP/1.0";
        }

        /// <summary>
        /// Gets the transports associate with the request.
        /// </summary>
        /// <value>The transport.</value>
        public RtspTransport[] GetTransports()
        {
            if (!Headers.TryGetValue(RtspHeaderNames.Transport, out string? transportString) || transportString is null)
                return new RtspTransport[] { new RtspTransport() };

            return transportString.Split(',').Select(RtspTransport.Parse).ToArray();
        }

        public void AddTransport(RtspTransport newTransport)
        {
            string actualTransport = string.Empty;
            if (Headers.ContainsKey(RtspHeaderNames.Transport))
                actualTransport = Headers[RtspHeaderNames.Transport] + ",";
            Headers[RtspHeaderNames.Transport] = actualTransport + newTransport.ToString();
        }
    }
}
