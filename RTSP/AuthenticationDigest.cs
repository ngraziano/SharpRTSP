using Rtsp.Messages;
using Rtsp.Utils;
using System;
using System.Net;
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

        public AuthenticationDigest(NetworkCredential credentials, string realm, string nonce, string qop) : base(credentials)
        {
            _realm = realm ?? throw new ArgumentNullException(nameof(realm));
            _nonce = nonce ?? throw new ArgumentNullException(nameof(nonce));

            if (!string.IsNullOrEmpty(qop))
            {
                int commaIndex = qop.IndexOf(',');
                _qop = commaIndex > -1 ? qop[..commaIndex] : qop;
            }
            uint cnonce = (uint)Guid.NewGuid().GetHashCode();
            _cnonce = cnonce.ToString("X8");
        }

        public override string GetResponse(uint nonceCounter, string uri, string method, byte[] entityBodyBytes)
        {
            string ha1 = MD5.GetHashHexValues($"{Credentials.UserName}:{_realm}:{Credentials.Password}");
            string ha2Argument = $"{method}:{uri}";
            bool hasQop = !string.IsNullOrEmpty(_qop);

            if (hasQop && _qop!.Equals("auth-int", StringComparison.InvariantCultureIgnoreCase))
            {
                ha2Argument = $"{ha2Argument}:{MD5.GetHashHexValues(entityBodyBytes)}";
            }
            string ha2 = MD5.GetHashHexValues(ha2Argument);

            StringBuilder sb = new();
            sb.AppendFormat("Digest username=\"{0}\", realm=\"{1}\", nonce=\"{2}\", uri=\"{3}\"", Credentials.UserName, _realm, _nonce, uri);
            if (!hasQop)
            {
                string response = MD5.GetHashHexValues($"{ha1}:{_nonce}:{ha2}");
                sb.AppendFormat(", response=\"{0}\"", response);
            }
            else
            {
                string response = MD5.GetHashHexValues($"{ha1}:{_nonce}:{nonceCounter:X8}:{_cnonce}:{_qop}:{ha2}");
                sb.AppendFormat(", response=\"{0}\", cnonce=\"{1}\", nc=\"{2:X8}\", qop=\"{3}\"", response, _cnonce, nonceCounter, _qop);
            }
            return sb.ToString();
        }
        public override bool IsValid(RtspMessage message)
        {
            string? authorization = message.Headers["Authorization"];

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
                string hashA1 = MD5.GetHashHexValues(auth_header_username + ":" + auth_header_realm + ":" + Credentials.Password);
                string hashA2 = MD5.GetHashHexValues(message.Method + ":" + auth_header_uri);
                string expected_response = MD5.GetHashHexValues(hashA1 + ":" + auth_header_nonce + ":" + hashA2);

                // Check if everything matches
                // ToDo - extract paths from the URIs (ignoring SETUP's trackID)
                if ((auth_header_username == Credentials.Password)
                    && (auth_header_realm == _realm)
                    && (auth_header_nonce == _nonce)
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


        public override string ToString() => $"Authentication Digest: Realm {_realm}, Nonce {_nonce}";
    }
}