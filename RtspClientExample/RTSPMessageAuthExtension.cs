using Rtsp;
using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RtspClientExample
{
    public static class RTSPMessageAuthExtension
    {
        public static void AddAuthorization(this RtspMessage message, NetworkCredential credentials, Authentication authentication, Uri uri, uint commandCounter)
        {
            switch (authentication)
            {
                case AuthenticationBasic basic:
                    {
                        string authorization = basic.GetResponse(commandCounter, string.Empty, string.Empty, Array.Empty<byte>());
                        message.Headers.Add(RtspHeaderNames.Authorization, authorization);
                    }
                    break;
                case AuthenticationDigest digest:
                    {
                        string authorization = digest.GetResponse(commandCounter, uri.AbsoluteUri, message.Method, Array.Empty<byte>());
                        message.Headers.Add(RtspHeaderNames.Authorization, authorization);

                    }
                    break;
            }
        }

        // Generate Basic or Digest Authorization
        public static void AddAuthorization(this RtspMessage message, string username, string password,
            string auth_type, string realm, string nonce, string url)
        {

            if (string.IsNullOrEmpty(username)) return;
            if (string.IsNullOrEmpty(password)) return;
            if (string.IsNullOrEmpty(realm)) return;

            if (auth_type == "Digest" && string.IsNullOrEmpty(nonce))
                return;

            if (auth_type == "Basic")
            {
                byte[] credentials = Encoding.UTF8.GetBytes(username + ":" + password);
                string credentials_base64 = Convert.ToBase64String(credentials);
                string basic_authorization = "Basic " + credentials_base64;

                message.Headers.Add(RtspHeaderNames.Authorization, basic_authorization);
            }
            else if (auth_type == "Digest")
            {
                string method = message.Method; // DESCRIBE, SETUP, PLAY etc

                MD5 md5 = MD5.Create();
                string hashA1 = CalculateMD5Hash(md5, username + ":" + realm + ":" + password);
                string hashA2 = CalculateMD5Hash(md5, method + ":" + url);
                string response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

                const string quote = "\"";
                string digest_authorization = "Digest username=" + quote + username + quote + ", "
                    + "realm=" + quote + realm + quote + ", "
                    + "nonce=" + quote + nonce + quote + ", "
                    + "uri=" + quote + url + quote + ", "
                    + "response=" + quote + response + quote;

                message.Headers.Add(RtspHeaderNames.Authorization, digest_authorization);
            }
        }

        // MD5 (lower case)
        public static string CalculateMD5Hash(
            MD5 md5_session, string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = md5_session.ComputeHash(inputBytes);

            StringBuilder output = new();
            for (int i = 0; i < hash.Length; i++)
            {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }
    }
}
