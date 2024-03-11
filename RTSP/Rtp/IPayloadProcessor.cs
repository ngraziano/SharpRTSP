using System;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    public interface IPayloadProcessor
    {
        [Obsolete("Use ProcessPacket instead and dispose result")]
        IList<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet, out DateTime? timeStamp);

        RawMediaFrame ProcessPacket(RtpPacket packet);
    }
}
