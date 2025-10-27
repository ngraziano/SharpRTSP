using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rtsp.Onvif;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace Rtsp.Rtp
{
    // This class handles the AV1 Payload
    // It has methods to parse parameters in the SDP
    // It has methods to process the RTP Payload

    public class AV1Payload : IPayloadProcessor
    {
        private readonly ILogger _logger;

        // AV1 structure.

        private readonly List<ReadOnlyMemory<byte>> obus = [];
        private readonly List<IMemoryOwner<byte>> owners = [];

        private readonly MemoryStream fragmentedObu = new();
        private readonly MemoryPool<byte> _memoryPool;
        private DateTime _timestamp;

        // Constructor
        public AV1Payload(ILogger<AV1Payload> logger, MemoryPool<byte> memoryPool = null)
        {
            _logger = logger as ILogger ?? NullLogger.Instance;
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        }

        private void ProcessRTPFrame(ReadOnlySpan<byte> payload)
        {
            //  0 1 2 3 4 5 6 7
            // +-+-+-+-+-+-+-+-+
            // |Z|Y| W |N|-|-|-|
            // +-+-+-+-+-+-+-+-+
            byte aggregationHeader = payload[0];

            int zBit = (aggregationHeader & (1 << 7)) != 0 ? 1 : 0;
            int yBit = (aggregationHeader & (1 << 6)) != 0 ? 1 : 0;
            int wSize = (aggregationHeader & 0x30) >> 4;
            int nBit = (aggregationHeader & (1 << 3)) != 0 ? 1 : 0;

            int obuCount = 0;
            int dataRemaining = payload.Length - 1;
            int obuPointer = 0;

            while(dataRemaining > 0)
            {
                int obuSize = dataRemaining;
                if(wSize == 0 || (wSize > 1 && obuCount != wSize - 1))
                {
                    int obuSizeLen = ReadLeb128(payload, 1 + obuPointer, out obuSize);
                    dataRemaining -= obuSizeLen;
                    obuPointer += obuSizeLen;
                }

                bool isFirstObu = obuCount == 0;
                bool isLastObu = (dataRemaining - obuSize) == 0;

                AssembleOBU(payload.Slice(1 + obuPointer, obuSize), isFirstObu, isLastObu, zBit, yBit, nBit);

                dataRemaining -= obuSize;
                obuPointer += obuSize;
                obuCount++;
            }

            if(wSize != 0 && wSize != obuCount)
            {
                _logger.LogError($"Mismatched OBU count");
            }
        }

        private void AssembleOBU(ReadOnlySpan<byte> readOnlySpan, bool isFirstObu, bool isLastObu, int zBit, int yBit, int nBit)
        {
            if(isFirstObu && zBit != 0)
            {
                // continuation of OBU from last RTP
                fragmentedObu.Write(readOnlySpan);

                if(!(isLastObu && yBit != 0))
                {
                    CreateOBU(fragmentedObu);
                }
            }
            else if(isLastObu && yBit != 0)
            {
                // reset the stream
                fragmentedObu.SetLength(0);

                // OBU will continue in the next fragment
                fragmentedObu.Write(readOnlySpan);
            }
            else
            {
                // we should have a complete OBU here
                fragmentedObu.Write(readOnlySpan);
                
                CreateOBU(fragmentedObu);
            }
        }

        private bool _seenSequenceHeader = false;

        private void CreateOBU(MemoryStream fragmentedObu)
        {
            fragmentedObu.Seek(0, SeekOrigin.Begin);
            int obuLength = (int)fragmentedObu.Length;
            int obuHeader = fragmentedObu.ReadByte();
            int obuHeaderLen = 1;
            int obuHeaderExtensions = -1;
            int obuType = (obuHeader & 0x78) >> 3;
            byte[] obuLizeLeb128 = null;

            if (obuType == 1)
            {
                _seenSequenceHeader = true;
            }

            if ((obuHeader & 0x04) == 0x04)
            {
                obuHeaderLen += 1;
                obuHeaderExtensions = fragmentedObu.ReadByte();
            }
            if ((obuHeader & 0x02) != 0x02)
            {
                // we'll have to restore the OBU size inside the OBU
                obuHeader = (byte)(obuHeader | 0x02);
                obuLizeLeb128 = WriteLeb128(obuLength - obuHeaderLen);
            }

            // keep dropping frames until we get a sequence header
            if (_seenSequenceHeader)
            {
                Span<byte> obuSpan;

                if (obuLizeLeb128 != null)
                {
                    // restore the OBU size inside the OBU
                    obuSpan = PrepareNewObu(obuLength + obuLizeLeb128.Length);
                    obuSpan[0] = (byte)obuHeader;
                    if (obuHeaderExtensions != -1)
                    {
                        obuSpan[1] = (byte)obuHeaderExtensions;
                    }
                    obuLizeLeb128.CopyTo(obuSpan[obuHeaderLen..]);
                    fragmentedObu.GetBuffer().AsSpan()[obuHeaderLen..obuLength].CopyTo(obuSpan[(obuHeaderLen + obuLizeLeb128.Length)..]);
                }
                else
                {
                    obuSpan = PrepareNewObu(obuLength); 
                    fragmentedObu.GetBuffer().AsSpan()[..obuLength].CopyTo(obuSpan);
                }

                //Log.Trace($"OBU {obuType}, payload: {Utilities.ToHexString(obuSpan.ToArray())}");
            }

            // reset buffer
            fragmentedObu.SetLength(0);
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            if (packet.Extension.Length > 0)
            {
                _timestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _);
            }

            ProcessRTPFrame(packet.Payload);

            if (!packet.IsMarker)
            {
                // we don't have a frame yet. Keep accumulating RTP packets
                return RawMediaFrame.Empty;
            }

            // End Marker is set return the list of OBUs
            // clone list of nalUnits and owners
            var result = new RawMediaFrame([.. obus], [.. owners])
            {
                RtpTimestamp = packet.Timestamp,
                ClockTimestamp = _timestamp,
            };
            obus.Clear();
            owners.Clear();
            return result;
        }

        private Span<byte> PrepareNewObu(int size)
        {
            var owner = _memoryPool.Rent(size);
            owners.Add(owner);
            var memory = owner.Memory[..(size)];
            obus.Add(memory);
            return memory.Span;
        }

        public int ReadLeb128(ReadOnlySpan<byte> source, int index, out int value)
        {
            int arrayIndex = index;
            int v = 0;
            int Leb128Bytes = 0;
            for (int i = 0; i < 8; i++)
            {
                int leb128_byte = source[arrayIndex++];
                v = v | ((leb128_byte & 0x7f) << (i * 7));
                Leb128Bytes += 1;
                if ((leb128_byte & 0x80) == 0)
                {
                    break;
                }
            }
            value = v;
            return Leb128Bytes;
        }

        public byte[] WriteLeb128(int value)
        {
            List<byte> bytes = new List<byte>();
            int v = value;
            int Leb128Bytes = 0;
            for (int i = 0; i < 8; i++)
            {
                int vv = v & 0x7f;
                v = v >> 7;

                if (v > 0)
                {
                    bytes.Add((byte)(vv | 0x80));
                    Leb128Bytes++;
                }
                else
                {
                    bytes.Add((byte)vv);
                    Leb128Bytes++;
                    break;
                }
            }

            return bytes.ToArray();
        }
    }
}