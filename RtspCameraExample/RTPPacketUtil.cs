using System;
using System.Buffers.Binary;


namespace RtspCameraExample
{
    public static class RTPPacketUtil
    {
        public const int RTP_VERSION = 2;

        public static void WriteHeader(Span<byte> rtp_packet, int rtp_version, bool rtp_padding, bool rtp_extension,
            int rtp_csrc_count, bool rtp_marker, int rtp_payload_type)
        {
            rtp_packet[0] = (byte)((rtp_version << 6) | ((rtp_padding ? 1 : 0) << 5) | ((rtp_extension ? 1 : 0) << 4) | rtp_csrc_count);
            rtp_packet[1] = (byte)(((rtp_marker ? 1 : 0) << 7) | (rtp_payload_type & 0x7F));
        }

        public static void WriteSequenceNumber(Span<byte> rtpPacket, ushort sequenceId)
        {
            BinaryPrimitives.WriteUInt16BigEndian(rtpPacket[2..], sequenceId);
        }

        public static void WriteTS(Span<byte> rtp_packet, uint ts)
        {
            BinaryPrimitives.WriteUInt32BigEndian(rtp_packet[4..], ts);
        }

        public static void WriteSSRC(Span<byte> rtp_packet, uint ssrc)
        {
            BinaryPrimitives.WriteUInt32BigEndian(rtp_packet[8..], ssrc);
        }
    }
}