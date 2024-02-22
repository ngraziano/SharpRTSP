using Rtsp;
using Rtsp.Messages;
using System;

namespace RtspClientExample
{
    public static class RTSPMessageAuthExtension
    {
        public static void AddAuthorization(this RtspMessage message, Authentication? authentication, Uri uri, uint commandCounter)
        {
            if (authentication is null)
            {
                return;
            }

            string authorization = authentication.GetResponse(commandCounter, uri.AbsoluteUri, message.Method, []);
            // remove if already one...
            message.Headers.Remove(RtspHeaderNames.Authorization);
            message.Headers.Add(RtspHeaderNames.Authorization, authorization);
        }

        public static void AddPlayback(this RtspMessage message, DateTime seekTime, double scale = 1.0)
        {
            message.Headers.Add(RtspHeaderNames.Scale, $"{scale:0.0}");
            message.Headers.Add(RtspHeaderNames.Range, $"clock={seekTime:yyyyMMdd}T{seekTime:HHmmss}Z-");
        }
        public static void AddPlayback(this RtspMessage message, DateTime seekTimeFrom, DateTime seekTimeTo, double scale = 1.0)
        {
            message.Headers.Add(RtspHeaderNames.Scale, $"{scale:0.0}");
            message.Headers.Add(RtspHeaderNames.Range, $"clock={seekTimeFrom:yyyyMMdd}T{seekTimeFrom:HHmmss}Z-{seekTimeTo:yyyyMMdd}T{seekTimeTo:HHmmss}Z");
        }
    }
}
