using System;
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
            sb.AppendFormat("Digest username=\"{0}\", realm=\"{1}\", nonce=\"{2}\", uri=\"{3}\"", Credentials.UserName, _realm, _nonce, uri);
            if (!hasQop)
            {
                string response = CalculateMD5Hash(md5, $"{ha1}:{_nonce}:{ha2}");
                sb.AppendFormat(", response=\"{0}\"", response);
            }
            else
            {
                string response = CalculateMD5Hash(md5, $"{ha1}:{_nonce}:{nonceCounter:X8}:{_cnonce}:{_qop}:{ha2}");
                sb.AppendFormat(", response=\"{0}\", cnonce=\"{1}\", nc=\"{2:X8}\", qop=\"{3}\"", response, _cnonce, nonceCounter, _qop);
            }
            return sb.ToString();
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