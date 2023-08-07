namespace Rtsp.Messages
{
    public class RtspRequestDescribe : RtspRequest
    {
        public RtspRequestDescribe()
        {
            Command = "DESCRIBE * RTSP/1.0";
        }
    }
}
