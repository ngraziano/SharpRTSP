using Rtsp.Onvif;
using Rtsp.Utils;
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

        private readonly PooledBufferWriter _frameBuffer;
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
            _frameBuffer = new(_memoryPool);
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

            if (!packet.IsMarker || _frameBuffer.Length == 0)
            {
                // we don't have a frame yet. Keep accumulating RTP packets
                return RawMediaFrame.Empty;
            }
            // End Marker is set. The frame is complete
            var length = (int)_frameBuffer.Length;
            var memoryOwner = _memoryPool.Rent(length);
            _frameBuffer.CopyTo(memoryOwner.Memory.Span);
            _frameBuffer.Clear();
            return new RawMediaFrame(new ReadOnlySequence<byte>(memoryOwner.Memory.Slice(0, length)), memoryOwner)
            {
                RtpTimestamp = packet.Timestamp,
                ClockTimestamp = _timestamp ?? DateTime.MinValue,
            };
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

                _frameBuffer.Write(_jpegHeaderBytes.AsSpan());
            }

            if (fragmentOffset != 0 && _frameBuffer.Length == 0) { return false; }
            if (_frameBuffer.Length > JPEG_MAX_SIZE) { return false; }

            int dataSize = payload.Length - offset;
            if (dataSize < 0) { return false; }

            _frameBuffer.Write(payload[offset..]);

            return true;
        }

        private void ReInitializeJpegHeader()
        {
            if (!_hasExternalQuantizationTable) { GenerateQuantizationTables(_currentQ); }
            int jpegHeaderSize = GetJpegHeaderSize(_currentDri);
            _jpegHeaderBytes = new byte[jpegHeaderSize];

            FillJpegHeader(_jpegHeaderBytes, _currentType, _currentFrameWidth, _currentFrameHeight, _currentDri);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) { return min; }
            if (value > max) { return max; }
            return value;
        }

        private void GenerateQuantizationTables(int factor)
        {
            _quantizationTablesLength = JPEGDefaultTables.DefaultQuantizers.Length;
            if (_quantizationTables.Length < _quantizationTablesLength)
            {
                _quantizationTables = new byte[_quantizationTablesLength];
            }

            factor = Clamp(factor, 1, 99);

            int q = factor < 50 ? 5000 / factor : 200 - (factor * 2);
            for (int i = 0; i < 128; ++i)
            {
                int newVal = Clamp(((JPEGDefaultTables.DefaultQuantizers[i] * q) + 50) / 100, 1, 255);

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
            // 8-bit precision
            buffer[offset++] = 0x08;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)height);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)width);
            offset += 2;
            // number of components
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

            offset += CreateHuffmanHeader(buffer[offset..], JPEGDefaultTables.LumDcTable);
            offset += CreateHuffmanHeader(buffer[offset..], JPEGDefaultTables.LumAcTable);
            offset += CreateHuffmanHeader(buffer[offset..], JPEGDefaultTables.ChmDcTable);
            offset += CreateHuffmanHeader(buffer[offset..], JPEGDefaultTables.ChmAcTable);

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

        private static int CreateHuffmanHeader(Span<byte> buffer, HuffmanTable table)
        {
            int offset = 0;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], MARKER_DHT);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(3 + table.Codelens.Length + table.Symbols.Length));
            offset += 2;
            buffer[offset++] = (byte)(table.Class << 4 | table.Number);
            table.Codelens.CopyTo(buffer[offset..]);
            offset += table.Codelens.Length;
            table.Symbols.CopyTo(buffer[offset..]);
            offset += table.Symbols.Length;
            return offset;
        }
    }
}
