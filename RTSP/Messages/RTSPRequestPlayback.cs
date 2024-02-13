using System;

namespace Rtsp.Messages;
public class RTSPRequestPlayback : RtspRequestPlay
{
    /// <summary>
    /// Instantiate a new Request play with range and scale headers
    /// </summary>
    /// <param name="seekTime">The seek time to start from</param>
    /// <param name="scale">The "direction" of playback, < -1.0 for reverse playback</param>
    public RTSPRequestPlayback(DateTime seekTime, double scale = 1.0) : base()
    {
        Headers.Add("range", FormattableString.Invariant($"clock={seekTime:yyyyMMdd}T{seekTime:HHmmss}-"));
        Headers.Add("scale", FormattableString.Invariant($"{scale}"));
    }
    public RTSPRequestPlayback(DateTime seekTimeFrom, DateTime seekTimeTo, double scale = 1.0) : base()
    {
        if(seekTimeFrom> seekTimeTo) { throw new ArgumentException("seekTimeFrom cannot be after seekTimeTo", nameof(seekTimeFrom)); }

        Headers.Add("range", FormattableString.Invariant($"clock={seekTimeFrom:yyyyMMdd}T{seekTimeFrom:HHmmss}-{seekTimeTo:yyyyMMdd}T{seekTimeTo:HHmmss}-"));
        Headers.Add("scale", FormattableString.Invariant($"{scale}"));
    }
}
