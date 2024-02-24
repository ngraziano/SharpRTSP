using Rtsp.Messages;
using System;

namespace Rtsp.Onvif
{
    public static class RtspMessageOnvifExtension
    {
        public static void AddPlayback(this RtspRequestPlay message, DateTime seekTime, double scale = 1.0)
        {
            message.Headers.Add(RtspHeaderNames.Scale, FormattableString.Invariant($"{scale:0.0}"));
            message.Headers.Add(RtspHeaderNames.Range, $"clock={seekTime:o}-");
        }
        public static void AddPlayback(this RtspRequestPlay message, DateTime seekTimeFrom, DateTime seekTimeTo, double scale = 1.0)
        {
            message.Headers.Add(RtspHeaderNames.Scale, FormattableString.Invariant($"{scale:0.0}"));
            message.Headers.Add(RtspHeaderNames.Range, $"clock={seekTimeFrom:o}-{seekTimeTo:o}");
        }

        // Here we can add other methods to add header like Require: onvif-replay
    }
}
