using Rtsp.Messages;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Rtsp
{

    // WWW-Authentication and Authorization Headers
    public class AuthenticationBasic : Authentication
    {
        private const char quote = '\"';

        // Constructor
        public AuthenticationBasic(string username, string password, string realm)
            : base(username, password, realm)
        {
        }

        public override string GetHeader()
        {
            return $"Basic realm=\"{realm}\"";
        }


        public override bool IsValid(RtspMessage received_message)
        {

            string? authorization = received_message.Headers["Authorization"];


            // Check Username and Password
            if (authorization != null && authorization.StartsWith("Basic "))
            {
                string base64_str = authorization.Substring(6); // remove 'Basic '
                byte[] data = Convert.FromBase64String(base64_str);
                string decoded = Encoding.UTF8.GetString(data);
                int split_position = decoded.IndexOf(':');
                string decoded_username = decoded.Substring(0, split_position);
                string decoded_password = decoded.Substring(split_position + 1);

                if ((decoded_username == username) && (decoded_password == password))
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



        // Generate Basic or Digest Authorization
        public static string? GenerateAuthorization(string username, string password,
                                             string realm, string nonce, string url, string command)
        {

            if (username == null || username.Length == 0) return null;
            if (password == null || password.Length == 0) return null;
            if (realm == null || realm.Length == 0) return null;

            byte[] credentials = Encoding.UTF8.GetBytes(username + ":" + password);
            string credentials_base64 = Convert.ToBase64String(credentials);
            string basic_authorization = "Basic " + credentials_base64;
            return basic_authorization;
        }
    }
}