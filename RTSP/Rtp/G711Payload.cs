using System;
using System.Buffers;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    // This class handles the G711 Payload
    // It has methods to process the RTP Payload
    public class G711Payload : IPayloadProcessor
    {
        private readonly MemoryPool<byte> _memoryPool;

        public G711Payload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        }

        public List<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet)
        {
            return [packet.Payload.ToArray()];
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            var owner = _memoryPool.Rent(packet.PayloadSize);
            var memory = owner.Memory[..packet.PayloadSize];
            packet.Payload.CopyTo(memory.Span);
            return new RawMediaFrame([memory], [owner]);
        }
    }
}
