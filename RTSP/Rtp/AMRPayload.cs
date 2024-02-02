using System;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    // This class handles the AMR Payload
    // It has methods to process the RTP Payload

    public class AMRPayload : IPayloadProcessor
    {
        public List<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet)
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
    }
}
