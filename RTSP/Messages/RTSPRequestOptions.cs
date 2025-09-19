namespace Rtsp.Messages;

public class RtspRequestOptions : RtspRequest
{
    public RtspRequestOptions()
    {
        Command = "OPTIONS * RTSP/1.0";
    }

    /// <summary>
    /// Gets the associate OK response with the request.
    /// </summary>
    /// <returns>
    /// an Rtsp response corresponding to request.
    /// </returns>
    public override RtspResponse CreateResponse()
    {
        var response = base.CreateResponse();
        // Add generic supported operations.
        response.Headers.Add(RtspHeaderNames.Public, "OPTIONS,DESCRIBE,ANNOUNCE,SETUP,PLAY,PAUSE,TEARDOWN,GET_PARAMETER,SET_PARAMETER,REDIRECT");

        return response;
    }
}
