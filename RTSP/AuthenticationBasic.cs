using Rtsp.Messages;
using System;
using System.Net;
using System.Text;

namespace Rtsp
{
    public class AuthenticationBasic : Authentication
    {
        public const string AUTHENTICATION_PREFIX = "Basic ";

        private readonly string _realm;
        

        public AuthenticationBasic(NetworkCredential credentials, string realm) : base(credentials)
        { 
            _realm = realm ?? throw new ArgumentNullException(nameof(realm));
        }

        public override string GetResponse(uint nonceCounter, string uri, string method, byte[] entityBodyBytes)
        {
            string usernamePasswordHash = $"{Credentials.UserName}:{Credentials.Password}";
            return AUTHENTICATION_PREFIX + Convert.ToBase64String(Encoding.UTF8.GetBytes(usernamePasswordHash));
        }

        public override string GetServerResponse()
        {
            return $"{AUTHENTICATION_PREFIX}realm=\"{_realm}\"";
        }

        public override bool IsValid(RtspMessage receivedMessage)
        {
            string? authorization = receivedMessage.Headers["Authorization"];

            // Check Username and Password
            if (authorization?.StartsWith(AUTHENTICATION_PREFIX, StringComparison.OrdinalIgnoreCase) != true)
            {
                return false;
            }
            // remove 'Basic '
            string base64_str = authorization[AUTHENTICATION_PREFIX.Length..];
            string decoded;
            try
            {
                byte[] data = Convert.FromBase64String(base64_str);
                decoded = Encoding.UTF8.GetString(data);
            }
            catch
            {
                return false;
            }

            return decoded.Split(':', 2) switch
            {
                [string username, string password] => string.Equals(username, Credentials.UserName, StringComparison.OrdinalIgnoreCase)
                                                        && string.Equals(password, Credentials.Password, StringComparison.Ordinal),
                _ => false
            };
        }
        public override string ToString() => "Authentication Basic";

    }
}