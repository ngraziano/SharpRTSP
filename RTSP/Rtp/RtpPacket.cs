using System;
using System.Buffers.Binary;

namespace Rtsp.Rtp
{
    public readonly ref struct RtpPacket
    {
        private readonly ReadOnlySpan<byte> rawData;

        public RtpPacket(ReadOnlySpan<byte> rawData)
        {
            this.rawData = rawData;
        }

        public ReadOnlySpan<byte> RawData => rawData;

        public bool IsWellFormed => rawData.Length >= 12 && Version == 2 && PayloadSize >= 0;

        public int Version => (rawData[0] >> 6) & 0x03;
        public bool HasPadding => (rawData[0] & 0x20) > 0;
        public bool HasExtension => (rawData[0] & 0x10) > 0;
        public int CsrcCount => rawData[0] & 0x0F;
        public bool IsMarker => (rawData[1] & 0x80) > 0;
        public int PayloadType => rawData[1] & 0x7F;
        public int SequenceNumber => BinaryPrimitives.ReadUInt16BigEndian(rawData[2..]);
        public uint Timestamp => BinaryPrimitives.ReadUInt32BigEndian(rawData[4..]);
        public uint Ssrc => BinaryPrimitives.ReadUInt32BigEndian(rawData[8..]);

        public int? ExtensionHeaderId => HasExtension ? (rawData[HeaderSize] << 8) + rawData[HeaderSize + 1] : null;

        private int HeaderSize => 12 + (CsrcCount * 4);

        private int ExtensionSize => HasExtension ? ((rawData[HeaderSize + 2] << 8) + rawData[HeaderSize + 3] + 1) * 4 : 0;

        private int PaddingSize => HasPadding ? rawData[^1] : 0;

        public int PayloadSize => rawData.Length - HeaderSize - ExtensionSize - PaddingSize;

        public ReadOnlySpan<byte> Payload => rawData[(HeaderSize + ExtensionSize)..^PaddingSize];
        public ReadOnlySpan<byte> Extension => rawData[HeaderSize..(HeaderSize + ExtensionSize)];
    }
}
