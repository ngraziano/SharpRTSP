using Rtsp.Messages;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Rtsp
{

    // WWW-Authentication and Authorization Headers
    public abstract class Authentication
    {
        protected readonly string username;
        protected readonly string password;
        protected readonly string realm;

        // Constructor
        public Authentication(string username, string password, string realm)
        {
            this.username = username;
            this.password = password;
            this.realm = realm;
        }

        public abstract string GetHeader();


        public abstract bool IsValid(RtspMessage received_message);

     

    }
}