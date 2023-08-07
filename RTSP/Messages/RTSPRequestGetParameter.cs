namespace Rtsp.Messages
{
    public class RtspRequestGetParameter : RtspRequest
    {
        public RtspRequestGetParameter()
        {
            Command = "GET_PARAMETER * RTSP/1.0";
        }
    }
}
