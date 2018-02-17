namespace Rtsp.Messages
{
    public class RtspRequestDescribe : RtspRequest
    {

        // constructor

        public RtspRequestDescribe()
        {
            Command = "DESCRIBE * RTSP/1.0";
        }
    }
}
