using System;
using System.Globalization;

namespace Rtsp.Sdp
{
    public abstract class Connection
    {
        protected Connection()
        {
            //Default value from spec
            NumberOfAddress = 1;
        }

        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the number of address specifed in connection.
        /// </summary>
        /// <value>The number of address.</value>
        //TODO handle it a different way (list of adress ?)
        public int NumberOfAddress { get; set; }

        public static Connection Parse(string value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            string[] parts = value.Split(' ');

            if (parts.Length != 3)
                throw new FormatException("Value do not contain 3 parts as needed.");

            if (!string.Equals(parts[0], "IN", StringComparison.Ordinal))
                throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, "Net type {0} not suported", parts[0]));

            return parts[1] switch
            {
                "IP4" => ConnectionIP4.Parse(parts[2]),
                "IP6" => ConnectionIP6.Parse(parts[2]),
                _ => throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, "Address type {0} not suported", parts[1])),
            };
        }
    }
}
