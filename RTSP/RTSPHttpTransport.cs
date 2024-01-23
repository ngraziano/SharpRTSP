using System.IO;

namespace Rtsp
{
    public class RtspHttpTransport : IRtspTransport
    {
        public string RemoteAddress { get; }
        public bool Connected { get; }
        public uint CommandCounter { get; }

        public void Close()
        {
            throw new System.NotImplementedException();
        }

        public Stream GetStream()
        {
            throw new System.NotImplementedException();
        }

        public void Reconnect()
        {
            throw new System.NotImplementedException();
        }
    }
}
