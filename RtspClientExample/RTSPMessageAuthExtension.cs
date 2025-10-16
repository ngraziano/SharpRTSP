using Rtsp;
using Rtsp.Messages;
using System;

namespace RtspClientExample
{
    public static class RTSPMessageAuthExtension
    {
        /// <summary>
        /// An helper method to add the Authorization header if required.
        /// </summary>
        /// <param name="message">Message to add to.</param>
        /// <param name="authentication">Authentication value</param>
        /// <param name="uri">Uri to connect to</param>
        /// <param name="commandCounter">A counter for authorization info.</param>
        public static void AddAuthorization(this RtspRequest message, Authentication? authentication, Uri uri, uint commandCounter)
        {
            if (authentication is null)
            {
                return;
            }

            string authorization = authentication.GetResponse(commandCounter, uri.AbsoluteUri, message.RequestTyped.ToString(), []);
            // remove if already one...
            message.Headers.Remove(RtspHeaderNames.Authorization);
            message.Headers.Add(RtspHeaderNames.Authorization, authorization);
        }
    }
}
