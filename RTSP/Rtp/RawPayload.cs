using Rtsp.Onvif;
using System.Buffers;

namespace Rtsp.Rtp
{
    public class RawPayload : IPayloadProcessor
    {
        private readonly MemoryPool<byte> _memoryPool;

        public RawPayload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            var memoryOwner = _memoryPool.Rent(packet.PayloadSize);
            packet.Payload.CopyTo(memoryOwner.Memory.Span);
            return new RawMediaFrame(new ReadOnlySequence<byte>(memoryOwner.Memory[..packet.PayloadSize]), memoryOwner)
            {
                ClockTimestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _),
                RtpTimestamp = packet.Timestamp,
            };
        }
    }
}
