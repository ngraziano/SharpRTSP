using Rtsp.Messages;
using System;
using System.Net;
using System.Text;

namespace Rtsp
{

    // WWW-Authentication and Authorization Headers
    public class AuthenticationBasic(NetworkCredential credentials) : Authentication(credentials)
    {
        public override string GetResponse(uint nonceCounter, string uri, string method, byte[] entityBodyBytes)
        {
            string usernamePasswordHash = $"{Credentials.UserName}:{Credentials.Password}";
            return $"Bassic {Convert.ToBase64String(Encoding.UTF8.GetBytes(usernamePasswordHash))}";
        }
        public override bool IsValid(RtspMessage message)
        {
            string? authorization = message.Headers["Authorization"];


            // Check Username and Password
            if (authorization != null && authorization.StartsWith("Basic "))
            {
                string base64_str = authorization.Substring(6); // remove 'Basic '
                byte[] data = Convert.FromBase64String(base64_str);
                string decoded = Encoding.UTF8.GetString(data);
                int split_position = decoded.IndexOf(':');
                string decoded_username = decoded.Substring(0, split_position);
                string decoded_password = decoded.Substring(split_position + 1);

                if ((decoded_username == Credentials.UserName) && (decoded_password == Credentials.Password))
                {
                    // _logger.Debug("Basic Authorization passed");
                    return true;
                }
                else
                {
                    // _logger.Debug("Basic Authorization failed");
                    return false;
                }
            }

            return false;
        }

        public override string ToString() => $"Authentication Basic";

    }
}