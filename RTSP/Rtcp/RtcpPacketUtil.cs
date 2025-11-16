namespace Rtsp.Rtcp;

using System;
using System.Buffers.Binary;

public static class RtcpPacketUtil
{
    public const int RTCP_VERSION = 2;
    public const int RTCP_PACKET_TYPE_SENDER_REPORT = 200;
    public const int RTCP_PACKET_TYPE_RECEIVER_REPORT = 201;

    private static readonly DateTime ntpStartTime = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void WriteHeader(Span<byte> packet, int version, bool hasPadding, int reportCount, int packetType, int length, uint ssrc)
    {
        packet[0] = (byte)((version << 6) + ((hasPadding ? 1 : 0) << 5) + reportCount);
        packet[1] = (byte)packetType;
        BinaryPrimitives.WriteUInt16BigEndian(packet[2..], (ushort)length);
        BinaryPrimitives.WriteUInt32BigEndian(packet[4..], ssrc);
    }

    public static void WriteSenderReport(Span<byte> rtcpSenderReport, DateTime wallClock, uint rtpTimestamp, uint rtpPacketCount, uint octetCount)
    {
        // NTP Most Signigicant Word is relative to 0h, 1 Jan 1900
        // This will wrap around in 2036
        TimeSpan tmpTime = wallClock - ntpStartTime;
        double totalSeconds = tmpTime.TotalSeconds;

        // whole number of seconds
        uint ntp_msw_seconds = (uint)Math.Truncate(totalSeconds);
        // fractional part, scaled between 0 and MaxInt
        uint ntp_lsw_fractions = (uint)(totalSeconds % 1 * uint.MaxValue);

        BinaryPrimitives.WriteUInt32BigEndian(rtcpSenderReport[8..], ntp_msw_seconds);
        BinaryPrimitives.WriteUInt32BigEndian(rtcpSenderReport[12..], ntp_lsw_fractions);
        BinaryPrimitives.WriteUInt32BigEndian(rtcpSenderReport[16..], rtpTimestamp);
        BinaryPrimitives.WriteUInt32BigEndian(rtcpSenderReport[20..], rtpPacketCount);
        BinaryPrimitives.WriteUInt32BigEndian(rtcpSenderReport[24..], octetCount);
    }
}
