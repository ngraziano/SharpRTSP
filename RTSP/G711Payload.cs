using System.Collections.Generic;

namespace Rtsp
{
    // This class handles the G711 Payload
    // It has methods to process the RTP Payload

    public class G711Payload : IPayloadProcessor
    {
        public List<byte[]> ProcessRTPPacket(byte[] rtp_payload, int rtp_marker)
        {

            List<byte[]> audio_data = new()
            {
                rtp_payload
            };

            return audio_data;
        }
    }
}
