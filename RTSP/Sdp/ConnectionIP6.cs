namespace Rtsp.Sdp
{
    using System;
    using System.Globalization;

    public class ConnectionIP6 : Connection
    {
        internal new static ConnectionIP6 Parse(string ipAddress)
        {
            string[] parts = ipAddress.Split('/');

            if (parts.Length > 2)
                throw new FormatException("Too much address subpart in " + ipAddress);

            var result = new ConnectionIP6
            {
                Host = parts[0]
            };

            if (parts.Length > 1)
            {
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int numberOfAddress))
                    throw new FormatException("Invalid number of address : " + parts[1]);
                result.NumberOfAddress = numberOfAddress;
            }

            return result;
        }
    }
}
