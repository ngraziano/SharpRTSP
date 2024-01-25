using System.Net;

namespace Rtsp
{
    static class NetworkCredentialExtensions
    {
        public static bool IsEmpty(this NetworkCredential networkCredential)
        {
            return string.IsNullOrEmpty(networkCredential.UserName) || networkCredential.Password == null;
        }
    }
}
