using Rtsp.Messages;
using System;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Rtsp
{
    // WWW-Authentication and Authorization Headers
    public class AuthenticationDigest : Authentication
    {
        private readonly string _realm;
        private readonly string _nonce;
        private readonly string? _qop;
        private readonly string _cnonce;

        public AuthenticationDigest(NetworkCredential credentials, string realm, string nonce, string? qop) : base(credentials)
        {
            _realm = realm ?? throw new ArgumentNullException(nameof(realm));
            _nonce = nonce ?? throw new ArgumentNullException(nameof(nonce));

            if (!string.IsNullOrEmpty(qop))
            {
                int commaIndex = qop!.IndexOf(',', StringComparison.OrdinalIgnoreCase);
                _qop = commaIndex > -1 ? qop[..commaIndex] : qop;
            }
            uint cnonce = (uint)Guid.NewGuid().GetHashCode();
            _cnonce = cnonce.ToString("X8");
        }

        public override string GetServerResponse()
        {
            //TODO implement correctly
            return $"Digest realm=\"{_realm}\", nonce=\"{_nonce}\"";
        }

        public override string GetResponse(uint nonceCounter, string uri, string method, byte[] entityBodyBytes)
        {
            MD5 md5 = MD5.Create();
            string ha1 = CalculateMD5Hash(md5, $"{Credentials.UserName}:{_realm}:{Credentials.Password}");
            string ha2Argument = $"{method}:{uri}";
            bool hasQop = !string.IsNullOrEmpty(_qop);

            if (hasQop && _qop!.Equals("auth-int", StringComparison.InvariantCultureIgnoreCase))
            {
                ha2Argument = $"{ha2Argument}:{CalculateMD5Hash(md5, entityBodyBytes)}";
            }
            string ha2 = CalculateMD5Hash(md5, ha2Argument);

            StringBuilder sb = new();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Digest username=\"{0}\", realm=\"{1}\", nonce=\"{2}\", uri=\"{3}\"", Credentials.UserName, _realm, _nonce, uri);
            if (!hasQop)
            {
                string response = CalculateMD5Hash(md5, $"{ha1}:{_nonce}:{ha2}");
                sb.AppendFormat(CultureInfo.InvariantCulture, ", response=\"{0}\"", response);
            }
            else
            {
                string response = CalculateMD5Hash(md5, $"{ha1}:{_nonce}:{nonceCounter:X8}:{_cnonce}:{_qop}:{ha2}");
                sb.AppendFormat(CultureInfo.InvariantCulture, ", response=\"{0}\", cnonce=\"{1}\", nc=\"{2:X8}\", qop=\"{3}\"", response, _cnonce, nonceCounter, _qop);
            }
            return sb.ToString();
        }
        public override bool IsValid(RtspMessage received_message)
        {
            string? authorization = received_message.Headers["Authorization"];

            // Check Username, URI, Nonce and the MD5 hashed Response
            if (authorization?.StartsWith("Digest ", StringComparison.Ordinal) == true)
            {
                // remove 'Digest '
                var value_str = authorization[7..];
                string? auth_header_username = null;
                string? auth_header_realm = null;
                string? auth_header_nonce = null;
                string? auth_header_uri = null;
                string? auth_header_response = null;

                foreach (string value in value_str.Split(','))
                {
                    string[] tuple = value.Trim().Split('=', 2);
                    if (tuple.Length != 2)
                    {
                        continue;
                    }
                    string var = tuple[1].Trim([' ', '\"']);
                    if (tuple[0].Equals("username", StringComparison.OrdinalIgnoreCase))
                    {
                        auth_header_username = var;
                    }
                    else if (tuple[0].Equals("realm", StringComparison.OrdinalIgnoreCase))
                    {
                        auth_header_realm = var;
                    }
                    else if (tuple[0].Equals("nonce", StringComparison.OrdinalIgnoreCase))
                    {
                        auth_header_nonce = var;
                    }
                    else if (tuple[0].Equals("uri", StringComparison.OrdinalIgnoreCase))
                    {
                        auth_header_uri = var;
                    }
                    else if (tuple[0].Equals("response", StringComparison.OrdinalIgnoreCase))
                    {
                        auth_header_response = var;
                    }
                }

                // Create the MD5 Hash using all parameters passed in the Auth Header with the 
                // addition of the 'Password'
                MD5 md5 = MD5.Create();
                string hashA1 = CalculateMD5Hash(md5, auth_header_username + ":" + auth_header_realm + ":" + Credentials.Password);
                string hashA2 = CalculateMD5Hash(md5, received_message.Method + ":" + auth_header_uri);
                string expected_response = CalculateMD5Hash(md5, hashA1 + ":" + auth_header_nonce + ":" + hashA2);

                // Check if everything matches
                // ToDo - extract paths from the URIs (ignoring SETUP's trackID)
                return (string.Equals(auth_header_username, Credentials.UserName, StringComparison.OrdinalIgnoreCase))
                    && (string.Equals(auth_header_realm, _realm, StringComparison.OrdinalIgnoreCase))
                    && (string.Equals(auth_header_nonce, _nonce, StringComparison.OrdinalIgnoreCase))
                    && (string.Equals(auth_header_response, expected_response, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        // MD5 (lower case)
        private static string CalculateMD5Hash(MD5 md5_session, string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            return CalculateMD5Hash(md5_session, inputBytes);
        }
        private static string CalculateMD5Hash(MD5 md5_session, byte[] input)
        {
            byte[] hash = md5_session.ComputeHash(input);

            var output = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }


    }
}