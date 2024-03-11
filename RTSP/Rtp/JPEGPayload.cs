using Rtsp.Onvif;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Rtsp.Rtp
{
    public class JPEGPayload : IPayloadProcessor
    {
        const ushort MARKER_SOF0 = 0xffc0;          // start-of-frame, baseline scan
        const ushort MARKER_SOI = 0xffd8;           // start of image
        const ushort MARKER_EOI = 0xffd9;           // end of image
        const ushort MARKER_SOS = 0xffda;           // start of scan
        const ushort MARKER_DRI = 0xffdd;           // restart interval
        const ushort MARKER_DQT = 0xffdb;           // define quantization tables
        const ushort MARKER_DHT = 0xffc4;           // huffman tables
        const ushort MARKER_APP_FIRST = 0xffe0;
        const ushort MARKER_APP_LAST = 0xffef;
        const ushort MARKER_COMMENT = 0xfffe;

        const int JPEG_HEADER_SIZE = 8;
        const int JPEG_MAX_SIZE = 16 * 1024 * 1024;

        private static readonly byte[] DefaultQuantizers = [
#pragma warning disable format

            16, 11, 12, 14, 12, 10, 16, 14,
            13, 14, 18, 17, 16, 19, 24, 40,
            26, 24, 22, 22, 24, 49, 35, 37,
            29, 40, 58, 51, 61, 60, 57, 51,
            56, 55, 64, 72, 92, 78, 64, 68,
            87, 69, 55, 56, 80, 109, 81, 87,
            95, 98, 103, 104, 103, 62, 77, 113,
            121, 112, 100, 120, 92, 101, 103, 99,
            17, 18, 18, 24, 21, 24, 47, 26,
            26, 47, 99, 66, 56, 66, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
#pragma warning restore format
        ];

        private static readonly byte[] LumDcCodelens = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];

        private static readonly byte[] LumDcSymbols = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

        private static readonly byte[] LumAcCodelens = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d];

        private static readonly byte[] LumAcSymbols = [
#pragma warning disable format
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
            0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
            0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08,
            0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
            0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16,
            0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
            0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
            0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
            0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
            0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
            0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6,
            0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
            0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4,
            0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
            0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea,
            0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
            0xf9, 0xfa,
#pragma warning restore format
        ];

        private static readonly byte[] ChmDcCodelens = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0,];

        private static readonly byte[] ChmDcSymbols = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11,];

        private static readonly byte[] ChmAcCodelens = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77,];

        private static readonly byte[] ChmAcSymbols = [
#pragma warning disable format
            0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
            0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
            0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
            0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
            0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34,
            0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
            0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38,
            0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
            0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
            0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
            0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
            0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
            0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96,
            0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
            0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4,
            0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
            0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2,
            0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
            0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9,
            0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
            0xf9, 0xfa,
#pragma warning restore format
            ];


        private readonly MemoryStream _frameStream = new(64 * 1024);
        private readonly MemoryPool<byte> _memoryPool;
        private int _currentDri;
        private int _currentQ;
        private int _currentType;
        private ushort _currentFrameWidth;
        private ushort _currentFrameHeight;

        private ushort _extensionFrameWidth;
        private ushort _extensionFrameHeight;

        private bool _hasExternalQuantizationTable;

        private byte[] _jpegHeaderBytes = [];

        private byte[] _quantizationTables = [];
        private int _quantizationTablesLength;

        private DateTime? _timestamp = null;

        public JPEGPayload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        }

        public IList<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet, out DateTime? timeStamp)
        {
            if (packet.HasExtension)
            {
                var extension = packet.Extension;
                _timestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(extension, out int headerPosition);
                extension = extension[headerPosition..];
                // if there is more data maybe it is JPEG extension
                if (extension.Length > 0)
                {
                    (_extensionFrameWidth, _extensionFrameHeight) = RtpPacketOnvifUtils.ProcessJpegFrameExtension(extension);
                }
            }
            ProcessJPEGRTPFrame(packet.Payload);

            if (!packet.IsMarker)
            {
                // we don't have a frame yet. Keep accumulating RTP packets
                timeStamp = DateTime.MinValue;
                return [];
            }
            // End Marker is set. The frame is complete
            var data = _frameStream.ToArray();
            _frameStream.SetLength(0);
            timeStamp = _timestamp;
            return [data];
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            if (packet.HasExtension)
            {
                var extension = packet.Extension;
                _timestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(extension, out int headerPosition);
                extension = extension[headerPosition..];
                // if there is more data maybe it is JPEG extension
                if (extension.Length > 0)
                {
                    (_extensionFrameWidth, _extensionFrameHeight) = RtpPacketOnvifUtils.ProcessJpegFrameExtension(extension);
                }
            }
            ProcessJPEGRTPFrame(packet.Payload);

            if (!packet.IsMarker || _frameStream.Length == 0)
            {
                // we don't have a frame yet. Keep accumulating RTP packets
                return new();
            }
            // End Marker is set. The frame is complete
            var length = (int)_frameStream.Length;
            var memoryOwner = _memoryPool.Rent(length);
            _frameStream.GetBuffer().AsSpan()[..length].CopyTo(memoryOwner.Memory.Span);
            _frameStream.SetLength(0);
            return new RawMediaFrame([memoryOwner.Memory[..length]], [memoryOwner], _timestamp ?? DateTime.MinValue);
        }

        private bool ProcessJPEGRTPFrame(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < JPEG_HEADER_SIZE) { return false; }

            int offset = 1;
            int fragmentOffset = payload[offset] << 16 | payload[offset + 1] << 8 | payload[offset + 2];
            offset += 3;

            int type = payload[offset++];
            int q = payload[offset++];
            ushort width = (ushort)(payload[offset++] * 8);
            ushort height = (ushort)(payload[offset++] * 8);
            int dri = 0;

            if (width == 0 && height == 0 && _extensionFrameWidth > 0 && _extensionFrameHeight > 0)
            {
                width = _extensionFrameWidth;
                height = _extensionFrameHeight;
            }

            if (type > 63)
            {
                dri = BinaryPrimitives.ReadInt16BigEndian(payload[offset..]);
                offset += 4;
            }

            if (fragmentOffset == 0)
            {
                bool quantizationTableChanged = false;
                if (q > 127)
                {
                    int mbz = payload[offset];
                    if (mbz == 0)
                    {
                        _hasExternalQuantizationTable = true;
                        int quantizationTablesLength = BinaryPrimitives.ReadUInt16BigEndian(payload[(offset + 2)..]);
                        offset += 4;

                        if (!payload[offset..(offset + quantizationTablesLength)].SequenceEqual(_quantizationTables.AsSpan()[0.._quantizationTablesLength]))
                        {
                            if (_quantizationTables.Length < quantizationTablesLength)
                            {
                                _quantizationTables = new byte[quantizationTablesLength];
                            }
                            payload[offset..(offset + quantizationTablesLength)].CopyTo(_quantizationTables);
                            _quantizationTablesLength = quantizationTablesLength;
                            quantizationTableChanged = true;
                        }
                        offset += quantizationTablesLength;
                    }
                }

                if (quantizationTableChanged
                    || _currentType != type
                    || _currentQ != q
                    || _currentFrameWidth != width
                    || _currentFrameHeight != height
                    || _currentDri != dri)
                {
                    _currentType = type;
                    _currentQ = q;
                    _currentFrameWidth = width;
                    _currentFrameHeight = height;
                    _currentDri = dri;

                    ReInitializeJpegHeader();
                }

                _frameStream.Write(_jpegHeaderBytes, 0, _jpegHeaderBytes.Length);
            }

            if (fragmentOffset != 0 && _frameStream.Position == 0) { return false; }
            if (_frameStream.Position > JPEG_MAX_SIZE) { return false; }

            int dataSize = payload.Length - offset;
            if (dataSize < 0) { return false; }

            _frameStream.Write(payload[offset..]);

            return true;
        }

        private void ReInitializeJpegHeader()
        {
            if (!_hasExternalQuantizationTable) { GenerateQuantizationTables(_currentQ); }
            int jpegHeaderSize = GetJpegHeaderSize(_currentDri);
            _jpegHeaderBytes = new byte[jpegHeaderSize];

            FillJpegHeader(_jpegHeaderBytes, _currentType, _currentFrameWidth, _currentFrameHeight, _currentDri);
        }

        private void GenerateQuantizationTables(int factor)
        {
            _quantizationTablesLength = 128;
            if (_quantizationTables.Length < _quantizationTablesLength)
            {
                _quantizationTables = new byte[_quantizationTablesLength];
            }

            if (factor < 1) { factor = 1; }
            else if (factor > 99) { factor = 99; }

            int q = factor < 50 ? 5000 / factor : 200 - factor * 2;
            for (int i = 0; i < 128; ++i)
            {
                int newVal = (DefaultQuantizers[i] * q + 50) / 100;
                if (newVal < 1) { newVal = 1; }
                else if (newVal > 255) { newVal = 255; }

                _quantizationTables[i] = (byte)newVal;
            }
        }
        private int GetJpegHeaderSize(int dri)
        {
            int qtlen = _quantizationTablesLength;
            int qtlenHalf = qtlen / 2;
            qtlen = qtlenHalf * 2;

            int qtablesCount = qtlen > 64 ? 2 : 1;
            return 485 + qtablesCount * 5 + qtlen + (dri > 0 ? 6 : 0);
        }
        private void FillJpegHeader(Span<byte> buffer, int type, int width, int height, int dri)
        {
            int qtablesCount = _quantizationTablesLength > 64 ? 2 : 1;
            int offset = 0;

            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_SOI);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_APP_FIRST);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 16);
            offset += 2;
            buffer[offset++] = (byte)'J';
            buffer[offset++] = (byte)'F';
            buffer[offset++] = (byte)'I';
            buffer[offset++] = (byte)'F';
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x00;

            if (dri > 0)
            {
                BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_DRI);
                offset += 2;
                BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 4);
                offset += 2;
                BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)dri);
                offset += 2;
            }

            int tableSize = qtablesCount == 1 ? _quantizationTablesLength : _quantizationTablesLength / 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_DQT);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(tableSize + 3));
            offset += 2;
            buffer[offset++] = 0x00;

            int qtablesOffset = 0;
            _quantizationTables.AsSpan(0, tableSize).CopyTo(buffer[offset..]);
            qtablesOffset += tableSize;
            offset += tableSize;

            if (qtablesCount > 1)
            {
                tableSize = _quantizationTablesLength - _quantizationTablesLength / 2;

                BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_DQT);
                offset += 2;
                BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(tableSize + 3));
                offset += 2;
                buffer[offset++] = 0x01;
                _quantizationTables.AsSpan(qtablesOffset, tableSize).CopyTo(buffer[offset..]);
                offset += tableSize;
            }

            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_SOF0);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 17);
            offset += 2;
            buffer[offset++] = 0x08;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)height);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)width);
            offset += 2;
            buffer[offset++] = 0x03;
            buffer[offset++] = 0x01;
            buffer[offset++] = (type & 1) != 0 ? (byte)0x22 : (byte)0x21;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x02;
            buffer[offset++] = 0x11;
            buffer[offset++] = qtablesCount == 1 ? (byte)0x00 : (byte)0x01;
            buffer[offset++] = 0x03;
            buffer[offset++] = 0x11;
            buffer[offset++] = qtablesCount == 1 ? (byte)0x00 : (byte)0x01;

            offset += CreateHuffmanHeader(buffer[offset..], LumDcCodelens, LumDcSymbols, 0, 0);
            offset += CreateHuffmanHeader(buffer[offset..], LumAcCodelens, LumAcSymbols, 0, 1);
            offset += CreateHuffmanHeader(buffer[offset..], ChmDcCodelens, ChmDcSymbols, 1, 0);
            offset += CreateHuffmanHeader(buffer[offset..], ChmAcCodelens, ChmAcSymbols, 1, 1);

            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_SOS);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 0x0C);
            offset += 2;
            buffer[offset++] = 0x03;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x02;
            buffer[offset++] = 0x11;
            buffer[offset++] = 0x03;
            buffer[offset++] = 0x11;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x3F;
            buffer[offset] = 0x00;
        }

        private static int CreateHuffmanHeader(Span<byte> buffer, Span<byte> codelens, Span<byte> symbols, int tableNo, int tableClass)
        {
            int offset = 0;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_DHT);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(3 + codelens.Length + symbols.Length));
            offset += 2;
            buffer[offset++] = (byte)(tableClass << 4 | tableNo);
            codelens.CopyTo(buffer[offset..]);
            offset += codelens.Length;
            symbols.CopyTo(buffer[offset..]);
            offset += symbols.Length;
            return offset;
        }
    }
}
