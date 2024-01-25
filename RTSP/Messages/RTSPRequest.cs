using System;

namespace Rtsp.Messages
{
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
            if (!Enum.TryParse(aStringRequest, true, out RequestType returnValue))
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
                RequestType.ANNOUNCE => new RtspRequestAnnounce(),
                RequestType.RECORD => new RtspRequestRecord(),
                /*
                RequestType.REDIRECT => new RtspRequestRedirect(),
                RequestType.SET_PARAMETER => new RtspRequestSetParameter(),
                */
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
        public string Request
        {
            get
            {
                return commandArray[0];
            }
        }

        /// <summary>
        /// Gets the request.
        /// <remarks>The return value is typed with <see cref="Rtsp.RequestType"/> if the value is not
        /// reconise the value is sent. The string value can be get by <see cref="Request"/></remarks>
        /// </summary>
        /// <value>The request.</value>
        public RequestType RequestTyped
        {
            get
            {
                return ParseRequest(commandArray[0]);
            }
            set
            {
                if (Enum.IsDefined(typeof(RequestType), value))
                    commandArray[0] = value.ToString();
                else
                    commandArray[0] = nameof(RequestType.UNKNOWN);
            }
        }

        private Uri? _RtspUri;

        /// <summary>
        /// Gets or sets the Rtsp asked URI.
        /// </summary>
        /// <value>The Rtsp asked URI.</value>
        /// <remarks>The request with uri * is return with null URI</remarks>
        public Uri? RtspUri
        {
            get
            {
                if (commandArray.Length < 2 || commandArray[1] == "*")
                {
                    return null;
                }
                if (_RtspUri == null)
                {
                    Uri.TryCreate(commandArray[1], UriKind.Absolute, out _RtspUri);
                }
                return _RtspUri;
            }
            set
            {
                _RtspUri = value;
                if (commandArray.Length < 2)
                {
                    Array.Resize(ref commandArray, 3);
                }
                commandArray[1] = (value != null ? value.ToString().TrimEnd('/') : "*");
            }
        }

        /// <summary>
        /// Gets the assiociate OK response with the request.
        /// </summary>
        /// <returns>an Rtsp response correcponding to request.</returns>
        public virtual RtspResponse CreateResponse()
        {
            var returnValue = new RtspResponse
            {
                ReturnCode = 200,
                CSeq = CSeq
            };
            if (Headers.TryGetValue(RtspHeaderNames.Session, out string? value))
            {
                returnValue.Headers[RtspHeaderNames.Session] = value;
            }

            return returnValue;
        }

        public object? ContextData { get; set; }
    }
}
