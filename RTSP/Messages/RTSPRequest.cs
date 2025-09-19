namespace Rtsp.Messages;

using System;

/// <summary>
/// An Rtsp Request
/// </summary>
public class RtspRequest : RtspMessage
{
    /// <summary>
    /// Request type.
    /// </summary>
    public enum RequestType
    {
        UNKNOWN,
        DESCRIBE,
        ANNOUNCE,
        GET_PARAMETER,
        OPTIONS,
        PAUSE,
        PLAY,
        RECORD,
        REDIRECT,
        SETUP,
        SET_PARAMETER,
        TEARDOWN,
    }

    /// <summary>
    /// Parses the request command.
    /// </summary>
    /// <param name="aStringRequest">A string request command.</param>
    /// <returns>The typed request.</returns>
    internal static RequestType ParseRequest(string aStringRequest)
    {
        if (!Enum.TryParse(aStringRequest, ignoreCase: true, out RequestType returnValue))
            returnValue = RequestType.UNKNOWN;
        return returnValue;
    }

    /// <summary>
    /// Gets the Rtsp request.
    /// </summary>
    /// <param name="aRequestParts">A request parts.</param>
    /// <returns>the parsed request</returns>
    internal static RtspMessage GetRtspRequest(string[] aRequestParts)
    {
        return ParseRequest(aRequestParts[0]) switch
        {
            RequestType.OPTIONS => new RtspRequestOptions(),
            RequestType.DESCRIBE => new RtspRequestDescribe(),
            RequestType.SETUP => new RtspRequestSetup(),
            RequestType.PLAY => new RtspRequestPlay(),
            RequestType.PAUSE => new RtspRequestPause(),
            RequestType.TEARDOWN => new RtspRequestTeardown(),
            RequestType.GET_PARAMETER => new RtspRequestGetParameter(),
            RequestType.SET_PARAMETER => new RtspRequestSetParameter(),
            RequestType.ANNOUNCE => new RtspRequestAnnounce(),
            RequestType.RECORD => new RtspRequestRecord(),
            RequestType.REDIRECT => new RtspRequestRedirect(),
            _ => new RtspRequest(),
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RtspRequest"/> class.
    /// </summary>
    public RtspRequest()
    {
        Command = "OPTIONS * RTSP/1.0";
    }

    /// <summary>
    /// Gets the request.
    /// </summary>
    /// <value>The request in string format.</value>
    public string Request => commandArray[0];

    /// <summary>
    /// Gets the request.
    /// <remarks>The return value is typed with <see cref="RtspRequest.RequestType"/> if the value is not
    /// recognise the value is sent. The string value can be got by <see cref="Request"/></remarks>
    /// </summary>
    /// <value>The request.</value>
    public RequestType RequestTyped
    {
        get => ParseRequest(commandArray[0]);
        set
        {
            if (Enum.IsDefined(typeof(RequestType), value))
                commandArray[0] = value.ToString();
            else
                commandArray[0] = nameof(RequestType.UNKNOWN);
        }
    }

    private Uri? _rtspUri;

    /// <summary>
    /// Gets or sets the Rtsp asked URI.
    /// </summary>
    /// <value>The Rtsp asked URI.</value>
    /// <remarks>The request with uri * is return with null URI</remarks>
    public Uri? RtspUri
    {
        get
        {
            if (commandArray.Length < 2 || string.Equals(commandArray[1], "*", StringComparison.InvariantCulture))
            {
                return null;
            }
            if (_rtspUri == null)
            {
                Uri.TryCreate(commandArray[1], UriKind.Absolute, out _rtspUri);
            }
            return _rtspUri;
        }
        set
        {
            _rtspUri = value;
            if (commandArray.Length < 2)
            {
                Array.Resize(ref commandArray, 3);
            }
            commandArray[1] = value is not null ? value.ToString() : "*";
        }
    }

    /// <summary>
    /// Gets the associate OK response with the request.
    /// </summary>
    /// <returns>an Rtsp response corresponding to request.</returns>
    public virtual RtspResponse CreateResponse()
    {
        var returnValue = new RtspResponse
        {
            ReturnCode = 200,
            CSeq = CSeq,
        };
        if (Headers.TryGetValue(RtspHeaderNames.Session, out var value))
        {
            returnValue.Headers[RtspHeaderNames.Session] = value;
        }

        return returnValue;
    }

    public object? ContextData { get; set; }
}
