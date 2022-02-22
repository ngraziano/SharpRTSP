using System;

namespace Rtsp.Messages
{
    public class RtspRequestSetup : RtspRequest
    {

        // Constructor
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

            string[] items = transportString.Split(',');
            return Array.ConvertAll(items,
                new Converter<string, RtspTransport>(RtspTransport.Parse));

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
