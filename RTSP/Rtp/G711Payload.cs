using System.Buffers;

namespace Rtsp.Rtp
{
    // This class handles the G711 Payload
    // It has methods to process the RTP Payload
    public class G711Payload : RawPayload
    {
        public G711Payload(MemoryPool<byte>? memoryPool = null) : base(memoryPool)
        {
        }
    }
}
