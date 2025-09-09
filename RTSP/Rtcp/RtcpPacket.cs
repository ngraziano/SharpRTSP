using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Rtsp.Rtcp
{
    public readonly ref struct RtcpPacket
    {
        private readonly ReadOnlySpan<byte> rawData;

        public RtcpPacket(ReadOnlySpan<byte> rawData)
        {
            this.rawData = rawData;
        }

        public bool IsEmpty => rawData.IsEmpty;
        public bool IsWellFormed => rawData.Length >= 4 && Version == 2 && Length >= 0;
        public int Version => (rawData[0] >> 6) & 0x03;
        public bool HasPadding => ((rawData[0] >> 5) & 0x01) != 0;
        public int Count => rawData[0] & 0x1F;
        public int PacketType => rawData[1];
        public int Length => BinaryPrimitives.ReadUInt16BigEndian(rawData[2..4]);

        public uint SenderSsrc => PacketType switch
        {
            RtcpPacketUtil.RTCP_PACKET_TYPE_SENDER_REPORT or
            RtcpPacketUtil.RTCP_PACKET_TYPE_RECEIVER_REPORT
                => BinaryPrimitives.ReadUInt32BigEndian(rawData[4..8]),
            _ => throw new InvalidOperationException("No Sender SSRC for this type of packet"),
        };

        public SenderReportPacket SenderReport => PacketType switch
        {
            RtcpPacketUtil.RTCP_PACKET_TYPE_SENDER_REPORT
                => new SenderReportPacket(rawData, Count),
            _ => throw new InvalidOperationException("Packet is not of sender report type"),
        };



        public RtcpPacket Next => new(rawData[((Length + 1) * 4)..]);

        // NTP Most Significant Word is relative to 0h, 1 Jan 1900
        // This will wrap around in 2036
        private static readonly DateTime ntpStartTime = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [StructLayout(LayoutKind.Auto)]
        public readonly ref struct SenderReportPacket
        {
            private readonly int count;
            private readonly ReadOnlySpan<byte> rawData;
            public SenderReportPacket(ReadOnlySpan<byte> rawData, int count)
            {
                this.rawData = rawData;
                this.count = count;
            }

            public DateTime Clock => ntpStartTime.AddSeconds(
                BinaryPrimitives.ReadUInt32BigEndian(rawData[8..12])
                + (BinaryPrimitives.ReadUInt32BigEndian(rawData[12..16]) / (double)uint.MaxValue)
                );

            public uint RtpTimestamp => BinaryPrimitives.ReadUInt32BigEndian(rawData[16..20]);

            public uint PacketCount => BinaryPrimitives.ReadUInt32BigEndian(rawData[20..24]);
            public uint OctetCount => BinaryPrimitives.ReadUInt32BigEndian(rawData[24..28]);
        }
    }
}
