using Rtsp.Messages;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Rtsp
{

    // WWW-Authentication and Authorization Headers
    public class AuthenticationDigest : Authentication
    {

        private readonly string nonce;
        private readonly MD5 md5 = MD5.Create();

        // Constructor
        public AuthenticationDigest(string username, string password, string realm)
            : base(username, password, realm)
        {
            nonce = new Random().Next(100000000, 999999999).ToString(); // random 9 digit number            
        }

        public override string GetHeader()
        {
            return $"Digest realm=\"{realm}\", nonce=\"{nonce}\"";
        }


        public override bool IsValid(RtspMessage received_message)
        {

            string? authorization = received_message.Headers["Authorization"];

            // Check Username, URI, Nonce and the MD5 hashed Response
            if (authorization != null && authorization.StartsWith("Digest "))
            {
                string value_str = authorization.Substring(7); // remove 'Digest '
                string[] values = value_str.Split(',');
                string? auth_header_username = null;
                string? auth_header_realm = null;
                string? auth_header_nonce = null;
                string? auth_header_uri = null;
                string? auth_header_response = null;

                foreach (string value in values)
                {
                    string[] tuple = value.Trim().Split(new char[] { '=' }, 2); // split on first '=' 
                    if (tuple.Length == 2 && tuple[0].Equals("username"))
                    {
                        auth_header_username = tuple[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
                    }
                    else if (tuple.Length == 2 && tuple[0].Equals("realm"))
                    {
                        auth_header_realm = tuple[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
                    }
                    else if (tuple.Length == 2 && tuple[0].Equals("nonce"))
                    {
                        auth_header_nonce = tuple[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
                    }
                    else if (tuple.Length == 2 && tuple[0].Equals("uri"))
                    {
                        auth_header_uri = tuple[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
                    }
                    else if (tuple.Length == 2 && tuple[0].Equals("response"))
                    {
                        auth_header_response = tuple[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
                    }
                }

                // Create the MD5 Hash using all parameters passed in the Auth Header with the 
                // addition of the 'Password'
                string hashA1 = CalculateMD5Hash(md5, auth_header_username + ":" + auth_header_realm + ":" + password);
                string hashA2 = CalculateMD5Hash(md5, received_message.Method + ":" + auth_header_uri);
                string expected_response = CalculateMD5Hash(md5, hashA1 + ":" + auth_header_nonce + ":" + hashA2);

                // Check if everything matches
                // ToDo - extract paths from the URIs (ignoring SETUP's trackID)
                if ((auth_header_username == username)
                    && (auth_header_realm == realm)
                    && (auth_header_nonce == nonce)
                    && (auth_header_response == expected_response)
                   )
                {
                    // _logger.Debug("Digest Authorization passed");
                    return true;
                }
                else
                {
                    // _logger.Debug("Digest Authorization failed");
                    return false;
                }
            }
            return false;
        }



        // Generate Basic or Digest Authorization
        public static string? GenerateAuthorization(string username, string password,
                                             string realm, string nonce, string url, string command)
        {

            if (string.IsNullOrEmpty(username)) return null;
            if (string.IsNullOrEmpty(password)) return null;
            if (string.IsNullOrEmpty(realm)) return null;
            if (string.IsNullOrEmpty(nonce)) return null;


                MD5 md5 = MD5.Create();
                string hashA1 = CalculateMD5Hash(md5, username + ":" + realm + ":" + password);
                string hashA2 = CalculateMD5Hash(md5, command + ":" + url);
                string response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

                string digest_authorization = $"Digest username=\"{username}\", "
                    + $"realm=\"{realm}\", "
                    + $"nonce=\"{nonce}\", "
                    + $"uri=\"{url}\", "
                    + $"response=\"{response}\"";

                return digest_authorization;
        
        }



        // MD5 (lower case)
        private static string CalculateMD5Hash(MD5 md5_session, string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = md5_session.ComputeHash(inputBytes);

            var output = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }
    }
}