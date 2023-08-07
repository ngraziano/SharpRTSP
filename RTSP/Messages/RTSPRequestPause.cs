namespace Rtsp.Messages
{
    public class RtspRequestPause : RtspRequest
    {
        public RtspRequestPause()
        {
            Command = "PAUSE * RTSP/1.0";
        }
    }
}
