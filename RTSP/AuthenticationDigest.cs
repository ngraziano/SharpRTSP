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
        public enum HASH_ALGORITHM { MD5, SHA256 };

        private readonly string _realm;
        private readonly string _nonce;
        private readonly string? _qop;
        private readonly string _cnonce;
        private readonly HASH_ALGORITHM _algorithm;

        public AuthenticationDigest(NetworkCredential credentials, string realm, string nonce, string? qop, HASH_ALGORITHM algorithm) : base(credentials)
        {
            _realm = realm ?? throw new ArgumentNullException(nameof(realm));
            _nonce = nonce ?? throw new ArgumentNullException(nameof(nonce));

            if (!string.IsNullOrEmpty(qop))
            {
                int commaIndex = qop!.IndexOf(',', StringComparison.OrdinalIgnoreCase);
                _qop = commaIndex > -1 ? qop![..commaIndex] : qop;
            }
            uint cnonce = (uint)Guid.NewGuid().GetHashCode();
            _cnonce = cnonce.ToString("X8");
            _algorithm = algorithm;
        }

        public override string GetServerResponse()
        {
            //TODO implement correctly
            string result = $"Digest realm=\"{_realm}\", nonce=\"{_nonce}\"";

            // algorithm defaults to MD5 (some clients may not expect an algorithm=MD5 parameter
            if (_algorithm == HASH_ALGORITHM.SHA256) result += $", algorithm=SHA-256";
            return result;
        }

        public override string GetResponse(uint nonceCounter, string uri, string method,
            byte[] entityBodyBytes)
        {
            HashAlgorithm hashAlgorithm;
            if (_algorithm == HASH_ALGORITHM.SHA256)
                hashAlgorithm = SHA256.Create();
            else /* default is MD5 */
                hashAlgorithm = MD5.Create();

            string ha1 = CalculateHash(hashAlgorithm, $"{Credentials.UserName}:{_realm}:{Credentials.Password}");
            string ha2Argument = $"{method}:{uri}";
            bool hasQop = !string.IsNullOrEmpty(_qop);

            if (hasQop && _qop!.Equals("auth-int", StringComparison.InvariantCultureIgnoreCase))
            {
                ha2Argument = $"{ha2Argument}:{CalculateHash(hashAlgorithm, entityBodyBytes)}";
            }
            string ha2 = CalculateHash(hashAlgorithm, ha2Argument);

            StringBuilder sb = new();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Digest username=\"{0}\", realm=\"{1}\", nonce=\"{2}\", uri=\"{3}\"", Credentials.UserName, _realm, _nonce, uri);
            if (!hasQop)
            {
                string response = CalculateHash(hashAlgorithm, $"{ha1}:{_nonce}:{ha2}");
                sb.AppendFormat(CultureInfo.InvariantCulture, ", response=\"{0}\"", response);
            }
            else
            {
                string response = CalculateHash(hashAlgorithm, $"{ha1}:{_nonce}:{nonceCounter:X8}:{_cnonce}:{_qop}:{ha2}");
                sb.AppendFormat(CultureInfo.InvariantCulture, ", response=\"{0}\", cnonce=\"{1}\", nc=\"{2:X8}\", qop=\"{3}\"", response, _cnonce, nonceCounter, _qop);
            }

            hashAlgorithm.Dispose();

            return sb.ToString();
        }
        public override bool IsValid(RtspRequest receivedMessage)
        {
            string? authorization = receivedMessage.Headers["Authorization"];

            // Check Username, URI, Nonce and the MD5 hashed Response
            if (authorization?.StartsWith("Digest ", StringComparison.Ordinal) == true)
            {
                // remove 'Digest '
                var valueStr = authorization[7..];
                string? username = null;
                string? realm = null;
                string? nonce = null;
                string? uri = null;
                string? response = null;
                HASH_ALGORITHM algorithm = HASH_ALGORITHM.MD5; // algorithm is optional, and defaults to MD5

                foreach (string value in valueStr.Split(','))
                {
                    string[] tuple = value.Trim().Split('=', 2);
                    if (tuple.Length != 2)
                    {
                        continue;
                    }
                    string var = tuple[1].Trim([' ', '\"']);
                    if (tuple[0].Equals("username", StringComparison.OrdinalIgnoreCase))
                    {
                        username = var;
                    }
                    else if (tuple[0].Equals("realm", StringComparison.OrdinalIgnoreCase))
                    {
                        realm = var;
                    }
                    else if (tuple[0].Equals("nonce", StringComparison.OrdinalIgnoreCase))
                    {
                        nonce = var;
                    }
                    else if (tuple[0].Equals("uri", StringComparison.OrdinalIgnoreCase))
                    {
                        uri = var;
                    }
                    else if (tuple[0].Equals("response", StringComparison.OrdinalIgnoreCase))
                    {
                        response = var;
                    }
                    else if (tuple[0].Equals("algorithm", StringComparison.OrdinalIgnoreCase))
                    {
                        if (String.Equals(var, "MD5")) algorithm = HASH_ALGORITHM.MD5;
                        else if (String.Equals(var,"SHA-256")) algorithm = HASH_ALGORITHM.SHA256;
                    }
                }

                // Create the MD5 Hash using all parameters passed in the Auth Header with the 
                // addition of the 'Password'
                HashAlgorithm hashAlgorithm;
                if (algorithm == HASH_ALGORITHM.SHA256)
                    hashAlgorithm = SHA256.Create();
                else /* Default to MD5 */
                    hashAlgorithm = MD5.Create();

                string hashA1 = CalculateHash(hashAlgorithm, username + ":" + realm + ":" + Credentials.Password);
                string hashA2 = CalculateHash(hashAlgorithm, receivedMessage.RequestTyped + ":" + uri);
                string expectedResponse = CalculateHash(hashAlgorithm, hashA1 + ":" + nonce + ":" + hashA2);
                hashAlgorithm.Dispose();

                // Check if everything matches
                // ToDo - extract paths from the URIs (ignoring SETUP's trackID)
                return (string.Equals(username, Credentials.UserName, StringComparison.OrdinalIgnoreCase))
                    && (string.Equals(realm, _realm, StringComparison.OrdinalIgnoreCase))
                    && (string.Equals(nonce, _nonce, StringComparison.OrdinalIgnoreCase))
                    && (string.Equals(response, expectedResponse, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        private static string CalculateHash(HashAlgorithm hashAlgorithm, string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            return CalculateHash(hashAlgorithm, inputBytes);
        }
        private static string CalculateHash(HashAlgorithm hashAlgorithm, byte[] input)
        {
            byte[] hash = hashAlgorithm.ComputeHash(input);

            var output = new StringBuilder();
            foreach (var t in hash)
            {
                output.Append(t.ToString("x2"));
            }

            return output.ToString();
        }
    }
}