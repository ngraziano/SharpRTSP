namespace Rtsp;

using System.Net;

static class NetworkCredentialExtensions
{
    extension(NetworkCredential networkCredential)
    {
        public bool IsEmpty()
        {
            return string.IsNullOrEmpty(networkCredential.UserName) || networkCredential.Password == null;
        }
    }
}
