using System;
using System.Collections.Generic;
using System.Text;

namespace Rtsp
{
    internal interface IPayloadProcessor
    {
        List<byte[]> ProcessRTPPacket(byte[] rtp_payload, int rtp_marker);
    }
}
