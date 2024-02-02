using Rtsp;
using Rtsp.Messages;
using System;
using System.Security.Cryptography;
using System.Text;

namespace RtspClientExample
{
    public static class RTSPMessageAuthExtension
    {
        public static void AddAuthorization(this RtspMessage message, Authentication? authentication, Uri uri, uint commandCounter)
        {
            if (authentication is null)
            {
                return;
            }

            string authorization = authentication.GetResponse(commandCounter, uri.AbsoluteUri, message.Method, []);
            message.Headers.Add(RtspHeaderNames.Authorization, authorization);
        }
    }
}
