namespace Rtsp.Messages
{
    public class RtspRequestAnnounce : RtspRequest
    {
        public RtspRequestAnnounce()
        {
            Command = "ANNOUNCE * RTSP/1.0";
        }
    }
}