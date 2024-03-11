using System;
using System.Net;

namespace Rtsp
{
    [Serializable]
    public class HttpBadResponseCodeException : Exception
    {
        public HttpStatusCode Code { get; }

        public HttpBadResponseCodeException(HttpStatusCode code) : base($"Bad response code: {code}")
        {
            Code = code;
        }

        public HttpBadResponseCodeException() { }

        public HttpBadResponseCodeException(string? message) : base(message)
        {
        }

        public HttpBadResponseCodeException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
