using System;

namespace Rtsp.Rtp
{
    public readonly ref struct RtpPacket
    {
        private readonly ReadOnlySpan<byte> rawData;

        public RtpPacket(ReadOnlySpan<byte> rawData)
        {
            this.rawData = rawData;
        }

        public int Version => (rawData[0] >> 6) & 0x03;
        public bool HasPadding => (rawData[0] & 0x20) > 0;
        public bool HasExtension => (rawData[0] & 0x10) > 0;
        public int CsrcCount => rawData[0] & 0x0F;
        public bool IsMarker => (rawData[1] & 0x80) > 0;
        public int PayloadType => rawData[1] & 0x7F;
        public int SequenceNumber => (rawData[2] << 8) + rawData[3];
        public ulong Timestamp => (ulong)(rawData[4] << 24) + (ulong)(rawData[5] << 16) + (ulong)(rawData[6] << 8) + (ulong)rawData[7];
        public ulong Ssrc => (ulong)(rawData[8] << 24) + (ulong)(rawData[9] << 16) + (ulong)(rawData[10] << 8) + (ulong)rawData[11];

        public int? ExtensionHeaderId => HasExtension ? (rawData[HeaderSize] << 8) + rawData[HeaderSize + 1] : null;

        private int HeaderSize => 12 + (CsrcCount * 4);

        private int ExtensionSize => HasExtension ? ((rawData[HeaderSize + 2] << 8) + rawData[HeaderSize + 3] + 1) * 4 : 0;

        private int PaddingSize => HasPadding ? rawData[^1] : 0;

        public int PayloadSize => rawData.Length - HeaderSize - ExtensionSize - PaddingSize;

        public ReadOnlySpan<byte> Payload => rawData[(HeaderSize + ExtensionSize)..^PaddingSize];
        public ReadOnlySpan<byte> Extension => rawData[HeaderSize..(HeaderSize + ExtensionSize)];
    }
}
