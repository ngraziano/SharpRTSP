namespace Rtsp.Messages
{
    public class RtspRequestTeardown : RtspRequest
    {
        public RtspRequestTeardown()
        {
            Command = "TEARDOWN * RTSP/1.0";
        }
    }
}
