using System;
using System.Globalization;
using System.Linq;

namespace Rtsp.Messages
{
    public class RtspResponse : RtspMessage
    {
        public const int DEFAULT_TIMEOUT = 60;

        /// <summary>
        /// Gets the default error message for an error code.
        /// </summary>
        /// <param name="aErrorCode">An error code.</param>
        /// <returns>The default error message associate</returns>
        private static string GetDefaultError(int aErrorCode)
        {
            return aErrorCode switch
            {
                100 => "Continue",
                200 => "OK",
                201 => "Created",
                250 => "Low on Storage Space",
                300 => "Multiple Choices",
                301 => "Moved Permanently",
                302 => "Moved Temporarily",
                303 => "See Other",
                305 => "Use Proxy",
                400 => "Bad Request",
                401 => "Unauthorized",
                402 => "Payment Required",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                406 => "Not Acceptable",
                407 => "Proxy Authentication Required",
                408 => "Request Timeout",
                410 => "Gone",
                411 => "Length Required",
                412 => "Precondition Failed",
                413 => "Request Entity Too Large",
                414 => "Request-URI Too Long",
                415 => "Unsupported Media Type",
                451 => "Invalid parameter",
                452 => "Illegal Conference Identifier",
                453 => "Not Enough Bandwidth",
                454 => "Session Not Found",
                455 => "Method Not Valid In This State",
                456 => "Header Field Not Valid",
                457 => "Invalid Range",
                458 => "Parameter Is Read-Only",
                459 => "Aggregate Operation Not Allowed",
                460 => "Only Aggregate Operation Allowed",
                461 => "Unsupported Transport",
                462 => "Destination Unreachable",
                500 => "Internal Server Error",
                501 => "Not Implemented",
                502 => "Bad Gateway",
                503 => "Service Unavailable",
                504 => "Gateway Timeout",
                505 => "RTSP Version Not Supported",
                551 => "Option not support",
                _ => "Return: " + aErrorCode.ToString(CultureInfo.InvariantCulture),
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspResponse"/> class.
        /// </summary>
        public RtspResponse()
        {
            // Initialise with a default result code.
            Command = "RTSP/1.0 200 OK";
        }

        private int _returnCode;
        /// <summary>
        /// Gets or sets the return code of the response.
        /// </summary>
        /// <value>The return code.</value>
        /// <remarks>On change the error message is set to the default one associate with the code</remarks>
        public int ReturnCode
        {
            get
            {
                if (_returnCode == 0 && commandArray.Length >= 2)
                {
                    int.TryParse(commandArray[1], out _returnCode);
                }

                return _returnCode;
            }
            set
            {
                if (ReturnCode != value)
                {
                    _returnCode = value;
                    // make sure we have the room
                    if (commandArray.Length < 3)
                    {
                        Array.Resize(ref commandArray, 3);
                    }
                    commandArray[1] = value.ToString(CultureInfo.InvariantCulture);
                    commandArray[2] = GetDefaultError(value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the error/return message.
        /// </summary>
        /// <value>The return message.</value>
        public string ReturnMessage
        {
            get
            {
                return commandArray.Length < 3 ? string.Empty: commandArray[2];
            }
            set
            {
                // Make sure we have the room
                if (commandArray.Length < 3)
                {
                    Array.Resize(ref commandArray, 3);
                }
                commandArray[2] = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance correspond to an OK response.
        /// </summary>
        /// <value><c>true</c> if this instance is OK; otherwise, <c>false</c>.</value>
        public bool IsOk => ReturnCode > 0 && ReturnCode < 400;

        /// <summary>
        /// Gets the timeout in second.
        /// <remarks>The default timeout is 60.</remarks>
        /// </summary>
        /// <value>The timeout.</value>
        public int Timeout
        {
            get
            {
                int returnValue = DEFAULT_TIMEOUT;
                if (Headers.TryGetValue(RtspHeaderNames.Session, out string? sessionString) && sessionString != null)
                {
                    string[] parts = sessionString.Split(';');
                    if (parts.Length > 1)
                    {
                        string[] subParts = parts[1].Split('=');
                        if (subParts.Length > 1 &&
                            string.Equals(subParts[0], "TIMEOUT", StringComparison.InvariantCultureIgnoreCase)
                            && !int.TryParse(subParts[1], out returnValue))
                        {
                            returnValue = DEFAULT_TIMEOUT;
                        }
                    }
                }
                return returnValue;
            }
            set
            {
                if (Headers.TryGetValue(RtspHeaderNames.Session, out string? sessionString) && sessionString != null)
                {
                    if (value != DEFAULT_TIMEOUT)
                    {
                        Headers[RtspHeaderNames.Session] =
                            sessionString.Split(';').First()
                            + ";timeout=" + value.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        //remove timeout part
                        Headers[RtspHeaderNames.Session] = sessionString.Split(';').First();
                    }
                }
            }
        }

        /// <summary>
        /// Gets the session ID.
        /// </summary>
        /// <value>The session ID.</value>
        public override string? Session
        {
            get
            {
                if (!Headers.TryGetValue(RtspHeaderNames.Session, out string? sessionString) || sessionString is null)
                    return null;

                return sessionString.Split(';')[0];
            }
            set
            {
                if (Timeout != DEFAULT_TIMEOUT)
                {
                    Headers[RtspHeaderNames.Session] = value + ";timeout=" + Timeout.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    Headers[RtspHeaderNames.Session] = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the original request associate with the response.
        /// </summary>
        /// <value>The original request.</value>
        public RtspRequest? OriginalRequest { get; set; }
    }
}
