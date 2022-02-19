namespace Rtsp.Messages
{
    public class RtspRequestGetParameter : RtspRequest
    {

        // Constructor
        public RtspRequestGetParameter()
        {
            Command = "GET_PARAMETER * RTSP/1.0";
        }
    }
}
