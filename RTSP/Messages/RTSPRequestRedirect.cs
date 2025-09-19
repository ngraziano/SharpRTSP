namespace Rtsp.Messages;

public class RtspRequestRedirect : RtspRequest
{
    public RtspRequestRedirect()
    {
        Command = "REDIRECT * RTSP/1.0";
    }
}
