using Rtsp.Messages;
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Rtsp;

public static class RTSPMessageAuthExtension
{
    public static void AddAuthorization(this RtspMessage message, NetworkCredential credentials, Authentication authentication, Uri uri, uint commandCounter)
    {
        switch (authentication)
        {
            case AuthenticationBasic basic:
                {
                    string authorization = basic.GetResponse(commandCounter, string.Empty, string.Empty, []);
                    message.Headers.Add(RtspHeaderNames.Authorization, authorization);
                }
                break;
            case AuthenticationDigest digest:
                {
                    string authorization = digest.GetResponse(commandCounter, uri.AbsoluteUri, message.Method, []);
                    message.Headers.Add(RtspHeaderNames.Authorization, authorization);

                }
                break;
        }
    }
}
