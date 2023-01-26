using Rtsp.Onvif;
using Rtsp.Utils;
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
        private readonly MemoryPool<byte> _memoryPool;

        public AMRPayload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
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
            IMemoryOwner<byte> owner = _memoryPool.Rent(lenght);
            // The rest of the RTP packet is the AMR data
            packet.Payload[1..].CopyTo(owner.Memory.Span);

            return new(new ReadOnlySequence<byte>(owner.Memory[..lenght]), owner)
            {
                ClockTimestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _),
                RtpTimestamp = packet.Timestamp,
            };
        }
    }
}
