namespace Rtsp.Messages
{
    public class RtspRequestTeardown : RtspRequest
    {

        // Constructor
        public RtspRequestTeardown()
        {
            Command = "TEARDOWN * RTSP/1.0";
        }
    }
}
