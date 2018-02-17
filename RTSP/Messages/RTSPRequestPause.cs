namespace Rtsp.Messages
{
    public class RtspRequestPause : RtspRequest
    {

        // Constructor
        public RtspRequestPause()
        {
            Command = "PAUSE * RTSP/1.0";
        }
    }
}
