using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Messages
{
    public class RtspRequestOptions : RtspRequest
    {
        /// <summary>
        /// Gets the assiociate OK response with the request.
        /// </summary>
        /// <returns>
        /// an Rtsp response corresponding to request.
        /// </returns>
        public override RtspResponse GetResponse()
        {
            RtspResponse response = base.GetResponse();
            // Add genric suported operations.
            response.Headers.Add(RtspHeaderNames.Public, "OPTIONS,DESCRIBE,ANNOUNCE,SETUP,PLAY,PAUSE,TEARDOWN,GET_PARAMETER,SET_PARAMETER,REDIRECT");

            return response;
        }

    }
}
