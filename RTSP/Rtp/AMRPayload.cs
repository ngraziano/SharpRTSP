using Rtsp.Onvif;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    /// <summary>
    /// This class handles the AMR Payload
    /// </summary>
    public class AMRPayload : IPayloadProcessor
    {
        // null: of arrays of ArrayPool<byte>.Shared, of no owner object of each buffer
        private readonly MemoryPool<byte>? _memoryPool;

        public AMRPayload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool;
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            // TODO check the RFC to handle the different modes

            // Octet-Aligned Mode (RFC 4867 Section 4.4.1)
            // First byte is the Payload Header
            if (packet.PayloadSize < 1)
            {
                return RawMediaFrame.Empty;
            }
            // byte payloadHeader = payload[0];

            int lenght = packet.PayloadSize - 1;
            var memory = RawMediaFrame.RentBuffer(_memoryPool, lenght, out IMemoryOwner<byte>? owner, out byte[]? array);
            // The rest of the RTP packet is the AMR data
            packet.Payload[1..].CopyTo(memory.Span);

            return RawMediaFrame.Rent(memory, owner, array, packet.Timestamp, RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _));
        }
    }
}
