using System;
using System.Buffers;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    // This class handles the AMR Payload
    // It has methods to process the RTP Payload

    public class AMRPayload : IPayloadProcessor
    {
        private readonly MemoryPool<byte> _memoryPool;

        public AMRPayload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        }

        public IList<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet)
        {
            // TODO check the RFC to handle the different modes

            // Octet-Aligned Mode (RFC 4867 Section 4.4.1)
            // First byte is the Payload Header
            if (packet.PayloadSize < 1)
            {
                return [];
            }
            // byte payloadHeader = payload[0];

            // The rest of the RTP packet is the AMR data
            return [packet.Payload[1..].ToArray()];
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            // TODO check the RFC to handle the different modes

            // Octet-Aligned Mode (RFC 4867 Section 4.4.1)
            // First byte is the Payload Header
            if (packet.PayloadSize < 1)
            {
                return new();
            }
            // byte payloadHeader = payload[0];

            int lenght = packet.PayloadSize - 1;
            IMemoryOwner<byte> owner = _memoryPool.Rent(lenght);
            // The rest of the RTP packet is the AMR data
            packet.Payload[1..].CopyTo(owner.Memory.Span);

            return new([owner.Memory[..lenght]], [owner]);
        }
    }
}
