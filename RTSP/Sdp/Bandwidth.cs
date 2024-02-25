using System;
using System.Globalization;

namespace Rtsp.Sdp
{
    public class Bandwidth
    {

        public required string Type { get; init; }
        public required int Value { get; init; }

        internal static Bandwidth Parse(string value)
        {
            var splitted = value.Split(':', 2);
            if (splitted.Length != 2)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Invalid bandwidth format");
            }

            if (!int.TryParse(splitted[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bwParsed))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Invalid bandwidth format");
            }
            return new Bandwidth() { Type = splitted[0], Value = bwParsed };
        }
    }
}
