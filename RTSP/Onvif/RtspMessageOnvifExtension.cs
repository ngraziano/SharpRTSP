using Rtsp.Messages;
using System;

namespace Rtsp.Onvif
{
    public static class RtspMessageOnvifExtension
    {
        // be aware:
        // seekTime:o returns datetime like yyyy-MM-dd hh:mm:ss, but onvif requires format to be yyyyMMddTHHmmss with the clock=
        // if using :o, will return a "not suported" error

        public static void AddPlayback(this RtspRequestPlay message, DateTime seekTime, double scale = 1.0)
        {
            message.Headers.Add(RtspHeaderNames.Scale, FormattableString.Invariant($"{scale:0.0}"));
            message.Headers.Add(RtspHeaderNames.Range, $"clock={Seek(seekTime)}-");
        }
        public static void AddPlayback(this RtspRequestPlay message, DateTime seekTimeFrom, DateTime seekTimeTo, double scale = 1.0)
        {
            message.Headers.Add(RtspHeaderNames.Scale, FormattableString.Invariant($"{scale:0.0}"));
            message.Headers.Add(RtspHeaderNames.Range, $"clock={Seek(seekTimeFrom)}-{Seek(seekTimeTo)}");
        }

        private static string Seek(DateTime dt) => FormattableString.Invariant($"{dt:yyyyMMdd}T{dt:HHmmss}Z");

        /// <summary>
        /// Add the Require: onvif-replay header to the message for ONVIF compatibility
        /// </summary>
        /// <param name="message">Message to modify</param>
        public static void AddRequireOnvifRequest(this RtspMessage message)
        {
            if (!message.Headers.ContainsKey(RtspHeaderNames.Require))
            {
                message.Headers.Add(RtspHeaderNames.Require, "onvif-replay");
            }
        }

        /// <summary>
        /// Add the Rate-Control header to the message for ONVIF replay compatibility
        /// </summary>
        /// <param name="message">Message to modify</param>
        /// <param name="rateControl">is rate controled by server, see onvif specification</param>
        public static void AddRateControlOnvifRequest(this RtspMessage message, bool rateControl)
        {
            if (!message.Headers.ContainsKey(RtspHeaderNames.RateControl))
            {
                message.Headers.Add(RtspHeaderNames.RateControl, rateControl ? "yes" : "no");
            }
        }
    }
}
