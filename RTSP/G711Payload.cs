using Rtsp.Rtp;
using System;
using System.Collections.Generic;

namespace Rtsp
{
    // This class handles the G711 Payload
    // It has methods to process the RTP Payload

    public class G711Payload : IPayloadProcessor
    {
        public List<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet)
        {
            return new() { packet.Payload };
        }
    }
}
