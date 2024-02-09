using System;
using System.Buffers;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    public class MP2TransportPayload : IPayloadProcessor
    {
        private readonly MemoryPool<byte> _memoryPool;

        public MP2TransportPayload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        }

        public List<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet)
        {
            // TODO check the RFC 2250
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
