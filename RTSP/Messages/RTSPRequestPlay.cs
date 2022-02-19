namespace Rtsp.Messages
{
    public class RtspRequestPlay : RtspRequest
    {

        // Constructor
        public RtspRequestPlay()
        {
            Command = "PLAY * RTSP/1.0";
        }
    }
}
