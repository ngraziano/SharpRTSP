using System;
using System.Collections.Generic;

namespace Rtsp
{
    // This class handles the AMR Payload
    // It has methods to process the RTP Payload

    public class AMRPayload : IPayloadProcessor
    {
        public List<byte[]> ProcessRTPPacket(byte[] rtp_payload, int rtp_marker)
        {

            // Octet-Aligned Mode (RFC 4867 Section 4.4.1)

            // First byte is the Payload Header
            if (rtp_payload.Length < 1)
            {
                return new();
            }
            byte payloadHeader = rtp_payload[0];

            // The rest of the RTP packet is the AMR data
            List<byte[]> audio_data = new();

            byte[] amr_data = new byte[rtp_payload.Length - 1];
            Array.Copy(rtp_payload, 1, amr_data, 0, rtp_payload.Length - 1);
            audio_data.Add(amr_data);

            return audio_data;
        }

    }
}
