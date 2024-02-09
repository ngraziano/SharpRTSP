using System;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    public interface IPayloadProcessor
    {
        List<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet);

        RawMediaFrame ProcessPacket(RtpPacket packet);
    }
}
