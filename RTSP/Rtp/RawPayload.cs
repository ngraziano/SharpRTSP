using Rtsp.Onvif;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    public class RawPayload : IPayloadProcessor
    {
        // null: of arrays of ArrayPool<byte>.Shared, of no owner object of each buffer
        private readonly MemoryPool<byte>? _memoryPool;

        public RawPayload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool;
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            var memory = RawMediaFrame.RentBuffer(_memoryPool, packet.PayloadSize, out IMemoryOwner<byte>? owner, out byte[]? array);
            packet.Payload.CopyTo(memory.Span);
            return RawMediaFrame.Rent(memory, owner, array, packet.Timestamp, RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _));
        }
    }
}
