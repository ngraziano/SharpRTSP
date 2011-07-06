using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace RTSP.Messages
{
    /// <summary>
    /// An RTSP Request
    /// </summary>
    public class RTSPRequest : RTSPMessage
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
            RequestType returnValue;
            if (!Enum.TryParse<RequestType>(aStringRequest, true, out returnValue))
                returnValue = RequestType.UNKNOWN;
            return returnValue;
        }

        /// <summary>
        /// Gets the RTSP request.
        /// </summary>
        /// <param name="aRequestParts">A request parts.</param>
        /// <returns>the parsed request</returns>
        internal static RTSPMessage GetRTSPRequest(string[] aRequestParts)
        {
            // <pex>
            Debug.Assert(aRequestParts != (string[])null, "aRequestParts");
            Debug.Assert(aRequestParts.Length != 0, "aRequestParts.Length == 0");
            // </pex>
            // we already know this is a Request
            RTSPRequest returnValue;
            switch (ParseRequest(aRequestParts[0]))
            {
                case RequestType.OPTIONS:
                    returnValue = new RTSPRequestOptions();
                    break;
                case RequestType.SETUP:
                    returnValue = new RTSPRequestSetup();
                    break;
                    /*
                case RequestType.DESCRIBE:
                    break;
                case RequestType.ANNOUNCE:
                    break;
                case RequestType.GET_PARAMETER:
                    break;

                case RequestType.PAUSE:
                    break;
                case RequestType.PLAY:
                    break;
                case RequestType.RECORD:
                    break;
                case RequestType.REDIRECT:
                    break;
                
                case RequestType.SET_PARAMETER:
                    break;
                case RequestType.TEARDOWN:
                    break;
                     */
                case RequestType.UNKNOWN:
                default:
                    returnValue = new RTSPRequest();
                    break;
            } 


             
            return returnValue;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPRequest"/> class.
        /// </summary>
        public RTSPRequest()
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
                return _command[0];
            }
        }

        /// <summary>
        /// Gets the request.
        /// <remarks>The return value is typed with <see cref="RTSP.RequestType"/> if the value is not
        /// reconise the value is sent. The string value can be get by <see cref="Request"/></remarks>
        /// </summary>
        /// <value>The request.</value>
        public RequestType RequestTyped
        {
            get
            {
                return ParseRequest(_command[0]);
            }
            set
            {
                if (Enum.IsDefined(typeof(RequestType), value))
                    _command[0] = value.ToString();
                else
                    _command[0] = RequestType.UNKNOWN.ToString();
            }
        }

        Uri _RTSPUri = null;
        /// <summary>
        /// Gets or sets the RTSP asked URI.
        /// </summary>
        /// <value>The RTSP asked URI.</value>
        /// <remarks>The request with uri * is return with null URI</remarks>
        public Uri RTSPUri
        {
            get
            {
                if (_command.Length < 2 || _command[1]=="*")
                    return null;
                if (_RTSPUri == null)
                    Uri.TryCreate(_command[1], UriKind.Absolute, out _RTSPUri);
                return _RTSPUri;
            }
            set
            {
                _RTSPUri = value;
                if (_command.Length < 2)
                {
                    Array.Resize(ref _command, 3);
                }
                _command[1] = value != null ? value.ToString():"*";
            }
        }

        /// <summary>
        /// Gets the assiociate OK response with the request.
        /// </summary>
        /// <returns>an RTSP response correcponding to request.</returns>
        public virtual RTSPResponse GetResponse()
        {
            RTSPResponse returnValue = new RTSPResponse();
            returnValue.ReturnCode = 200;
            returnValue.CSeq = this.CSeq;
            if (this.Headers.ContainsKey(RTSPHeaderNames.Session))
            {
                returnValue.Headers[RTSPHeaderNames.Session] = this.Headers[RTSPHeaderNames.Session]; 
            }

            return returnValue;
        }

    }
}
