using Rtsp.Rtp;
using System;
using System.Collections.Generic;
using System.Text;

namespace Rtsp
{
    public interface IPayloadProcessor
    {
        List<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet);
    }
}
