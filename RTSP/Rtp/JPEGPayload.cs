﻿using System;
using System.Collections.Generic;
using System.IO;

namespace Rtsp.Rtp
{
    public class JPEGPayload : IPayloadProcessor
    {
        const byte MARKER_SOF0 = 0xc0;          // start-of-frame, baseline scan
        const byte MARKER_SOI = 0xd8;           // start of image
        const byte MARKER_EOI = 0xd9;           // end of image
        const byte MARKER_SOS = 0xda;           // start of scan
        const byte MARKER_DRI = 0xdd;           // restart interval
        const byte MARKER_DQT = 0xdb;           // define quantization tables
        const byte MARKER_DHT = 0xc4;           // huffman tables
        const byte MARKER_APP_FIRST = 0xe0;
        const byte MARKER_APP_LAST = 0xef;
        const byte MARKER_COMMENT = 0xfe;

        const int JPEG_HEADER_SIZE = 8;
        const int JPEG_MAX_SIZE = 16 * 1024 * 1024;

        private static readonly byte[] DefaultQuantizers = new byte[]
            {
                16,
                11,
                12,
                14,
                12,
                10,
                16,
                14,
                13,
                14,
                18,
                17,
                16,
                19,
                24,
                40,
                26,
                24,
                22,
                22,
                24,
                49,
                35,
                37,
                29,
                40,
                58,
                51,
                61,
                60,
                57,
                51,
                56,
                55,
                64,
                72,
                92,
                78,
                64,
                68,
                87,
                69,
                55,
                56,
                80,
                109,
                81,
                87,
                95,
                98,
                103,
                104,
                103,
                62,
                77,
                113,
                121,
                112,
                100,
                120,
                92,
                101,
                103,
                99,
                17,
                18,
                18,
                24,
                21,
                24,
                47,
                26,
                26,
                47,
                99,
                66,
                56,
                66,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99,
                99
            };

        private static readonly byte[] LumDcCodelens = new byte[] {
                0,
            1,
            5,
            1,
            1,
            1,
            1,
            1,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0
        };

        private static readonly byte[] LumDcSymbols = new byte[]
        {
                0,
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8,
            9,
            10,
            11
        };

        private static readonly byte[] LumAcCodelens = new byte[]
        {
                0,
            2,
            1,
            3,
            3,
            2,
            4,
            3,
            5,
            5,
            4,
            4,
            0,
            0,
            1,
            0x7d
        };

        private static readonly byte[] LumAcSymbols = new byte[]
        {
                0x01,
            0x02,
            0x03,
            0x00,
            0x04,
            0x11,
            0x05,
            0x12,
            0x21,
            0x31,
            0x41,
            0x06,
            0x13,
            0x51,
            0x61,
            0x07,
            0x22,
            0x71,
            0x14,
            0x32,
            0x81,
            0x91,
            0xa1,
            0x08,
            0x23,
            0x42,
            0xb1,
            0xc1,
            0x15,
            0x52,
            0xd1,
            0xf0,
            0x24,
            0x33,
            0x62,
            0x72,
            0x82,
            0x09,
            0x0a,
            0x16,
            0x17,
            0x18,
            0x19,
            0x1a,
            0x25,
            0x26,
            0x27,
            0x28,
            0x29,
            0x2a,
            0x34,
            0x35,
            0x36,
            0x37,
            0x38,
            0x39,
            0x3a,
            0x43,
            0x44,
            0x45,
            0x46,
            0x47,
            0x48,
            0x49,
            0x4a,
            0x53,
            0x54,
            0x55,
            0x56,
            0x57,
            0x58,
            0x59,
            0x5a,
            0x63,
            0x64,
            0x65,
            0x66,
            0x67,
            0x68,
            0x69,
            0x6a,
            0x73,
            0x74,
            0x75,
            0x76,
            0x77,
            0x78,
            0x79,
            0x7a,
            0x83,
            0x84,
            0x85,
            0x86,
            0x87,
            0x88,
            0x89,
            0x8a,
            0x92,
            0x93,
            0x94,
            0x95,
            0x96,
            0x97,
            0x98,
            0x99,
            0x9a,
            0xa2,
            0xa3,
            0xa4,
            0xa5,
            0xa6,
            0xa7,
            0xa8,
            0xa9,
            0xaa,
            0xb2,
            0xb3,
            0xb4,
            0xb5,
            0xb6,
            0xb7,
            0xb8,
            0xb9,
            0xba,
            0xc2,
            0xc3,
            0xc4,
            0xc5,
            0xc6,
            0xc7,
            0xc8,
            0xc9,
            0xca,
            0xd2,
            0xd3,
            0xd4,
            0xd5,
            0xd6,
            0xd7,
            0xd8,
            0xd9,
            0xda,
            0xe1,
            0xe2,
            0xe3,
            0xe4,
            0xe5,
            0xe6,
            0xe7,
            0xe8,
            0xe9,
            0xea,
            0xf1,
            0xf2,
            0xf3,
            0xf4,
            0xf5,
            0xf6,
            0xf7,
            0xf8,
            0xf9,
            0xfa
        };

        private static readonly byte[] ChmDcCodelens = new byte[] {
                0,
            3,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            0,
            0,
            0,
            0,
            0
        };

        private static readonly byte[] ChmDcSymbols = new byte[]
        {
            0,
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8,
            9,
            10,
            11
        };

        private static readonly byte[] ChmAcCodelens = new byte[]
        {
                0,
            2,
            1,
            2,
            4,
            4,
            3,
            4,
            7,
            5,
            4,
            4,
            0,
            1,
            2,
            0x77
        };

        private static readonly byte[] ChmAcSymbols = new byte[]
        {
                0x00,
            0x01,
            0x02,
            0x03,
            0x11,
            0x04,
            0x05,
            0x21,
            0x31,
            0x06,
            0x12,
            0x41,
            0x51,
            0x07,
            0x61,
            0x71,
            0x13,
            0x22,
            0x32,
            0x81,
            0x08,
            0x14,
            0x42,
            0x91,
            0xa1,
            0xb1,
            0xc1,
            0x09,
            0x23,
            0x33,
            0x52,
            0xf0,
            0x15,
            0x62,
            0x72,
            0xd1,
            0x0a,
            0x16,
            0x24,
            0x34,
            0xe1,
            0x25,
            0xf1,
            0x17,
            0x18,
            0x19,
            0x1a,
            0x26,
            0x27,
            0x28,
            0x29,
            0x2a,
            0x35,
            0x36,
            0x37,
            0x38,
            0x39,
            0x3a,
            0x43,
            0x44,
            0x45,
            0x46,
            0x47,
            0x48,
            0x49,
            0x4a,
            0x53,
            0x54,
            0x55,
            0x56,
            0x57,
            0x58,
            0x59,
            0x5a,
            0x63,
            0x64,
            0x65,
            0x66,
            0x67,
            0x68,
            0x69,
            0x6a,
            0x73,
            0x74,
            0x75,
            0x76,
            0x77,
            0x78,
            0x79,
            0x7a,
            0x82,
            0x83,
            0x84,
            0x85,
            0x86,
            0x87,
            0x88,
            0x89,
            0x8a,
            0x92,
            0x93,
            0x94,
            0x95,
            0x96,
            0x97,
            0x98,
            0x99,
            0x9a,
            0xa2,
            0xa3,
            0xa4,
            0xa5,
            0xa6,
            0xa7,
            0xa8,
            0xa9,
            0xaa,
            0xb2,
            0xb3,
            0xb4,
            0xb5,
            0xb6,
            0xb7,
            0xb8,
            0xb9,
            0xba,
            0xc2,
            0xc3,
            0xc4,
            0xc5,
            0xc6,
            0xc7,
            0xc8,
            0xc9,
            0xca,
            0xd2,
            0xd3,
            0xd4,
            0xd5,
            0xd6,
            0xd7,
            0xd8,
            0xd9,
            0xda,
            0xe2,
            0xe3,
            0xe4,
            0xe5,
            0xe6,
            0xe7,
            0xe8,
            0xe9,
            0xea,
            0xf2,
            0xf3,
            0xf4,
            0xf5,
            0xf6,
            0xf7,
            0xf8,
            0xf9,
            0xfa
        };

        static readonly byte[] StartMarkerBytes = new byte[] { 0xFF, 0xD8 };
        static readonly byte[] EndMarkerBytes = new byte[] { 0xFF, 0xD9 };

        readonly MemoryStream _frameStream = new(64 * 1024);
        //private readonly List<byte[]> temporary_rtp_payloads = [];
        private readonly List<ReadOnlyMemory<byte>> temporaryRtpPayloads = new(256);

        private ReadOnlyMemory<byte> extensionMemory;
        private bool hasExtensionMemory;

        private int _currentDri;
        private int _currentQ;
        private int _currentType;
        private int _currentFrameWidth;
        private int _currentFrameHeight;

        private int _extensionFrameWidth = 0;
        private int _extensionFrameHeight = 0;

        private bool _hasExternalQuantizationTable;

        private byte[] _jpegHeaderBytes = Array.Empty<byte>();

        private byte[] _quantizationTables = Array.Empty<byte>();
        private int _quantizationTablesLength;

        public List<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet)
        {
            temporaryRtpPayloads.Add(packet.Payload); // Todo Could optimise this and go direct to Process Frame if just 1 packet in frame

            if (packet.HasExtension)
            {
                extensionMemory = packet.Extension;
                hasExtensionMemory = true;
            }

            if (packet.IsMarker)
            {
                // End Marker is set. Process the list of RTP Packets (forming 1 RTP frame) and save the results
                ReadOnlyMemory<byte> nalUnits = ProcessJPEGRTPFrame(temporaryRtpPayloads);
                temporaryRtpPayloads.Clear();

                extensionMemory = null;
                hasExtensionMemory = false;

                return new() { nalUnits };
            }
            // we don't have a frame yet. Keep accumulating RTP packets
            return new();
        }

        private ReadOnlyMemory<byte> ProcessJPEGRTPFrame(List<ReadOnlyMemory<byte>> rtp_payloads)
        {
            _frameStream.SetLength(0);

            if (hasExtensionMemory)
            {
                ReadOnlySpan<byte> extension = extensionMemory.Span;
                int extensionType = (extension[0] << 8) + (extension[1] << 0);
                if (extensionType == 0xFFD8)
                {
                    int headerPosition = 4;
                    int extensionSize = extension.Length;
                    while (headerPosition < (extensionSize - 4))
                    {
                        int blockType = (extension[headerPosition] << 8) + extension[headerPosition + 1];
                        int blockSize = (extension[headerPosition + 2] << 8) + extension[headerPosition + 3];

                        if (blockType == 0xFFC0)
                        {
                            if (JpegExtractExtensionWidthHeight(extension, headerPosition, blockSize + 2, out int width, out int height) == 1)
                            {
                                _extensionFrameWidth = width / 8;
                                _extensionFrameHeight = height / 8;
                            }
                        }
                        headerPosition += (blockSize + 2);
                    }
                }
            }

            foreach (ReadOnlyMemory<byte> payloadMemory in rtp_payloads)
            {
                var payload = payloadMemory.Span;

                if (rtp_payloads.Count < JPEG_HEADER_SIZE) { return null; }

                int offset = 1;
                int fragmentOffset = payload[offset] << 16 | payload[offset + 1] << 8 | payload[offset + 2];
                offset += 3;

                int type = payload[offset++];
                int q = payload[offset++];
                int width = payload[offset++] * 8;
                int height = payload[offset++] * 8;
                int dri = 0;

                if (width == 0 && height == 0 && _extensionFrameWidth > 0 && _extensionFrameHeight > 0)
                {
                    width = _extensionFrameWidth * 8;
                    height = _extensionFrameHeight * 8;
                }

                if (type > 63)
                {
                    dri = payload[offset] << 8 | payload[offset];
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
                            int quantizationTablesLength = payload[offset + 2] << 8 | payload[offset + 3];
                            offset += 4;

                            if (!ArrayUtils.IsBytesEquals(payload.ToArray(), offset, quantizationTablesLength, _quantizationTables, 0, _quantizationTablesLength))
                            {
                                if (_quantizationTables.Length < quantizationTablesLength)
                                {
                                    _quantizationTables = new byte[quantizationTablesLength];
                                }
                                Buffer.BlockCopy(payload.ToArray(), offset, _quantizationTables, 0, quantizationTablesLength);
                                _quantizationTablesLength = quantizationTablesLength;
                                quantizationTableChanged = true;
                            }
                            offset += quantizationTablesLength;
                        }
                    }

                    if (quantizationTableChanged || _currentType != type || _currentQ != q || _currentFrameWidth != width || _currentFrameHeight != height ||
                        _currentDri != dri)
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

                if (fragmentOffset != 0 && _frameStream.Position == 0) { return null; /* ? */ }
                if (_frameStream.Position > JPEG_MAX_SIZE) { return null; }

                int dataSize = payload.Length - offset;
                if (dataSize < 0) { return null; }

                _frameStream.Write(payload);
            }

            return _frameStream.ToArray();

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
        private void FillJpegHeader(byte[] buffer, int type, int width, int height, int dri)
        {
            int qtablesCount = _quantizationTablesLength > 64 ? 2 : 1;
            int offset = 0;

            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xD8;
            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xe0;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x10;
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
                buffer[offset++] = 0xFF;
                buffer[offset++] = 0xdd;
                buffer[offset++] = 0x00;
                buffer[offset++] = 0x04;
                buffer[offset++] = (byte)(dri >> 8);
                buffer[offset++] = (byte)dri;
            }

            int tableSize = qtablesCount == 1 ? _quantizationTablesLength : _quantizationTablesLength / 2;
            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xdb;
            buffer[offset++] = 0x00;
            buffer[offset++] = (byte)(tableSize + 3);
            buffer[offset++] = 0x00;

            int qtablesOffset = 0;
            Buffer.BlockCopy(_quantizationTables, qtablesOffset, buffer, offset, tableSize);
            qtablesOffset += tableSize;
            offset += tableSize;

            if (qtablesCount > 1)
            {
                tableSize = _quantizationTablesLength - _quantizationTablesLength / 2;

                buffer[offset++] = 0xFF;
                buffer[offset++] = 0xdb;
                buffer[offset++] = 0x00;
                buffer[offset++] = (byte)(tableSize + 3);
                buffer[offset++] = 0x01;
                Buffer.BlockCopy(_quantizationTables, qtablesOffset, buffer, offset, tableSize);
                offset += tableSize;
            }

            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xc0;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x11;
            buffer[offset++] = 0x08;
            buffer[offset++] = (byte)(height >> 8);
            buffer[offset++] = (byte)height;
            buffer[offset++] = (byte)(width >> 8);
            buffer[offset++] = (byte)width;
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

            CreateHuffmanHeader(buffer, offset, LumDcCodelens, LumDcCodelens.Length, LumDcSymbols, LumDcSymbols.Length,
                0, 0);
            offset += 5 + LumDcCodelens.Length + LumDcSymbols.Length;

            CreateHuffmanHeader(buffer, offset, LumAcCodelens, LumAcCodelens.Length, LumAcSymbols, LumAcSymbols.Length,
                0, 1);
            offset += 5 + LumAcCodelens.Length + LumAcSymbols.Length;

            CreateHuffmanHeader(buffer, offset, ChmDcCodelens, ChmDcCodelens.Length, ChmDcSymbols, ChmDcSymbols.Length,
                1, 0);
            offset += 5 + ChmDcCodelens.Length + ChmDcSymbols.Length;

            CreateHuffmanHeader(buffer, offset, ChmAcCodelens, ChmAcCodelens.Length, ChmAcSymbols, ChmAcSymbols.Length,
                1, 1);
            offset += 5 + ChmAcCodelens.Length + ChmAcSymbols.Length;

            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xda;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x0C;
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

        private static void CreateHuffmanHeader(byte[] buffer, int offset, byte[] codelens, int ncodes, byte[] symbols,
                int nsymbols, int tableNo, int tableClass)
        {
            buffer[offset++] = 0xff;
            buffer[offset++] = 0xc4;
            buffer[offset++] = 0;
            buffer[offset++] = (byte)(3 + ncodes + nsymbols);
            buffer[offset++] = (byte)(tableClass << 4 | tableNo);
            Buffer.BlockCopy(codelens, 0, buffer, offset, ncodes);
            offset += ncodes;
            Buffer.BlockCopy(symbols, 0, buffer, offset, nsymbols);
        }

        private static int JpegExtractExtensionWidthHeight(ReadOnlySpan<byte> extension, int headerPosition, int size, out int width, out int height)
        {
            width = -1;
            height = -1;

            if (size < 17) { return -3; }

            int i = 0;
            do
            {
                if (extension[headerPosition + i] == 0xFF && extension[headerPosition + i + 1] == 0xC0)
                {
                    height = ((extension[headerPosition + i + 5] << 8) & 0x0000FF00) | (extension[headerPosition + i + 6] & 0x000000FF);
                    width = ((extension[headerPosition + i + 7] << 8) & 0x0000FF00) | (extension[headerPosition + i + 8] & 0x000000FF);
                    return 1;
                }
                ++i;
            }
            while (i < (size - 17));
            return 0;
        }
    }
}
