using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RTSP.Messages
{
    public class RTSPRequestOptions : RTSPRequest
    {
        /// <summary>
        /// Gets the assiociate OK response with the request.
        /// </summary>
        /// <returns>
        /// an RTSP response corresponding to request.
        /// </returns>
        public override RTSPResponse GetResponse()
        {
            RTSPResponse response = base.GetResponse();
            // Add genric suported operations.
            response.Headers.Add(RTSPHeaderNames.Public, "OPTIONS,DESCRIBE,ANNOUNCE,SETUP,PLAY,PAUSE,TEARDOWN,GET_PARAMETER,SET_PARAMETER,REDIRECT");

            return response;
        }

    }
}
