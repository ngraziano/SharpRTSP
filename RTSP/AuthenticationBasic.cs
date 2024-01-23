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

        public override string ToString() => $"Authentication Basic";

    }
}