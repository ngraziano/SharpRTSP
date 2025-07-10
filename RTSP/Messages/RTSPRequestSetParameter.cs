namespace Rtsp.Messages
{
    public class RtspRequestSetParameter : RtspRequest
    {
        public RtspRequestSetParameter()
        {
            Command = "SET_PARAMETER * RTSP/1.0";
        }
    }
}
