using Rtsp.Messages;
using System;
using System.Net;
using System.Text;

namespace Rtsp
{

    // WWW-Authentication and Authorization Headers
    public class AuthenticationBasic : Authentication
    {
        public AuthenticationBasic(NetworkCredential credentials) : base(credentials)
        { }

        public override string GetResponse(uint nonceCounter, string uri, string method, byte[] entityBodyBytes)
        {
            string usernamePasswordHash = $"{Credentials.UserName}:{Credentials.Password}";
            return $"Bassic {Convert.ToBase64String(Encoding.UTF8.GetBytes(usernamePasswordHash))}";
        }
        public override bool IsValid(RtspMessage received_message)
        {
            string? authorization = received_message.Headers["Authorization"];


            // Check Username and Password
            if (authorization != null && authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                string base64_str = authorization.Substring(6); // remove 'Basic '
                byte[] data = Convert.FromBase64String(base64_str);
                string decoded = Encoding.UTF8.GetString(data);
                int split_position = decoded.IndexOf(':', StringComparison.Ordinal);
                string decoded_username = decoded[..split_position];
                string decoded_password = decoded[(split_position + 1)..];

                return string.Equals(decoded_username, Credentials.UserName, StringComparison.OrdinalIgnoreCase) && string.Equals(decoded_password, Credentials.Password, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        public override string ToString() => $"Authentication Basic";

    }
}