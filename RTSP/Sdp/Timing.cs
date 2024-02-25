using System;
using System.Globalization;

namespace Rtsp.Sdp
{
    public class Timing
    {
        public required long StartTime { get; init; }
        public required long StopTime { get; init; }

        internal static Timing Parse(string timing, string _)
        {
            var parts = timing.Split(' ');
            if (parts.Length != 2)
            {
                throw new ArgumentException("Invalid timing format, need two number", nameof(timing));
            }

            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long start))
            {
                throw new ArgumentException("Invalid timing format, start time is not a number", nameof(timing));
            }
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long stop))
            {
                throw new ArgumentException("Invalid timing format, stop time is not a number", nameof(timing));
            }

            // TODO: Parse repeat
            return new()
            {
                StartTime = start,
                StopTime = stop,
            };
        }
    }
}
