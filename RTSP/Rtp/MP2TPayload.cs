using System.Buffers;

namespace Rtsp.Rtp
{
    // TODO check the RFC 2250
    public class MP2TransportPayload : RawPayload
    {
        public MP2TransportPayload(MemoryPool<byte>? memoryPool = null)
            : base(memoryPool)
        {
        }
    }
}
