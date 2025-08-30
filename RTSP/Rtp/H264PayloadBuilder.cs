using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Rtsp.Rtp
{
    /// <summary>
    /// Allow to build RTP packet from raw NAL
    /// </summary>
    /// <remarks>
    /// CSRC is not supported
    /// </remarks>
    public class H264PayloadBuilder
    {
        private readonly int _payloadType;
        private readonly int _packetMaxSize;
        private readonly MemoryPool<byte> pool = MemoryPool<byte>.Shared;


        public uint Ssrc { get; }
        public ushort SequenceNumber { get; private set; }

        public H264PayloadBuilder(int payloadType, uint ssrc, int packetMaxSize, ushort initialsequenceNumber)
        {
            _payloadType = payloadType;
            Ssrc = ssrc;
            _packetMaxSize = packetMaxSize;
            SequenceNumber = initialsequenceNumber;
        }

        /// <summary>
        /// Prepare rtp packets from one or more NALS
        /// </summary>
        /// <param name="nalArray">List of NAL, each nal may or may not contain the 00 00 00 01 header</param>
        /// <param name="rtpTimestamp">Rtp timestamps of the generated packet</param>
        /// <returns>List of memory of packet and list of owner of this memories </returns>
        /// <remarks>All the NALS must have the same RTP timestamp</remarks>
        public (List<Memory<byte>>, List<IMemoryOwner<byte>>) PrepareVideoRtpPackets(
            List<byte[]> nalArray,
            uint rtpTimestamp
            )
        {
            List<Memory<byte>> rtp_packets = [];
            List<IMemoryOwner<byte>> memoryOwners = [];

            int payloadMaxSize = _packetMaxSize - RtpPacketUtil.DataOffset(0, extensionDataSizeInWord: null);


            for (int x = 0; x < nalArray.Count; x++)
            {
                ReadOnlySpan<byte> rawNal = SkipNalStart(nalArray[x]);

                int multipleInPacketSize = 1 + 2 + rawNal.Length;

                int multiplePacketIndex = x;
                // check if we can put multiple nal in one packet
                for (int y = x + 1; y < nalArray.Count; y++)
                {
                    int newSize = multipleInPacketSize + 2 + SkipNalStart(nalArray[y]).Length;
                    if (newSize > payloadMaxSize)
                    {
                        break;
                    }
                    multiplePacketIndex = y;
                }

                if (multiplePacketIndex > x)
                {
                    bool lastNal = multiplePacketIndex == nalArray.Count - 1;
                    var nals = nalArray.Skip(x).Take(1 + multiplePacketIndex - x).ToList();
                    AddMultipleNal(rtpTimestamp, rtp_packets, memoryOwners, nals, lastNal);
                    x = multiplePacketIndex;
                }
                else
                {

                    bool lastNal = x == nalArray.Count - 1;

                    // The H264 Payload could be sent as one large RTP packet (assuming the receiver can handle it)
                    // or as a Fragmented Data, split over several RTP packets with the same Timestamp.
                    if (rawNal.Length <= payloadMaxSize)
                    {
                        AddFullNal(rtpTimestamp, rtp_packets, memoryOwners, rawNal, lastNal);
                    }
                    else
                    {
                        AddFragmentedNal(rtpTimestamp, rtp_packets, memoryOwners, rawNal, lastNal);
                    }
                }
            }

            return (rtp_packets, memoryOwners);
        }

        private void AddMultipleNal(uint rtpTimestamp, List<Memory<byte>> rtp_packets, List<IMemoryOwner<byte>> memoryOwners, List<byte[]> nals, bool lastNal)
        {
            // Put the whole NAL into one RTP packet.
            var headerSize = RtpPacketUtil.DataOffset(0, extensionDataSizeInWord: null);

            var payloadSize = 1 + nals.Sum(nal => 2 + SkipNalStart(nal).Length);

            var destSize = headerSize + payloadSize;
            var owner = pool.Rent(destSize);
            memoryOwners.Add(owner);
            var rtp_packet = owner.Memory[..(destSize)];

            // Create an single RTP fragment
            RtpPacketUtil.WriteHeader(rtp_packet.Span,
                RtpPacketUtil.RTP_VERSION,
                padding: false,
                hasExtension: false,
                csrcCount: 0,
                marker: lastNal, _payloadType);

            RtpPacketUtil.WriteSequenceNumber(rtp_packet.Span, SequenceNumber++);
            RtpPacketUtil.WriteSSRC(rtp_packet.Span, Ssrc);

            RtpPacketUtil.WriteTimestamp(rtp_packet.Span, rtpTimestamp);


            var offset = headerSize;
            rtp_packet.Span[offset++] = 24;
            foreach (var nal in nals)
            {
                var rawNal = SkipNalStart(nal);
                BinaryPrimitives.WriteUInt16BigEndian(rtp_packet[offset..(offset + 2)].Span, (ushort)rawNal.Length);

                // Now append the raw NAL
                rawNal.CopyTo(rtp_packet[(offset + 2)..].Span);

                offset += rawNal.Length + 2;
            }

            rtp_packets.Add(rtp_packet);
        }

        /// <summary>
        /// Skip the header 00 00 00 01
        /// </summary>
        /// <param name="nal">The nal</param>
        /// <returns>The part of nal without the header</returns>
        private static ReadOnlySpan<byte> SkipNalStart(ReadOnlySpan<byte> nal)
        {
            if (nal.Length > 3 && nal[0] == 0 && nal[1] == 0 && nal[2] == 0 && nal[3] == 1)
            {
                return nal[4..];
            }
            return nal;
        }

        private void AddFragmentedNal(uint rtpTimestamp, List<Memory<byte>> rtp_packets, List<IMemoryOwner<byte>> memoryOwners, ReadOnlySpan<byte> rawNal, bool last_nal)
        {
            bool start = true;
            bool end = false;

            var headerSize = RtpPacketUtil.DataOffset(0, extensionDataSizeInWord: null);

            int payloadMaxSize = _packetMaxSize - headerSize;

            // consume first byte of the raw_nal. It is used in the FU header
            byte firstByte = rawNal[0];
            rawNal = rawNal[1..];

            while (rawNal.Length > 0)
            {
                int payload_size = Math.Min(payloadMaxSize, rawNal.Length);
                end = (rawNal.Length == payload_size);

                // 2 bytes for FU-A header 
                var destSize = headerSize + 2 + payload_size;
                var owner = pool.Rent(destSize);
                memoryOwners.Add(owner);
                var rtpPacket = owner.Memory[..destSize];

                RtpPacketUtil.WriteHeader(rtpPacket.Span, RtpPacketUtil.RTP_VERSION,
                    padding: false, hasExtension: false, csrcCount: 0, marker: last_nal && end, _payloadType);

                RtpPacketUtil.WriteSequenceNumber(rtpPacket.Span, SequenceNumber++);
                RtpPacketUtil.WriteSSRC(rtpPacket.Span, Ssrc);
                RtpPacketUtil.WriteTimestamp(rtpPacket.Span, rtpTimestamp);

                // Now append the Fragmentation Header (with Start and End marker) and part of the raw_nal
                const byte f_bit = 0;
                byte nri = (byte)(firstByte >> 5 & 0x03); // Part of the 1st byte of the Raw NAL (NAL Reference ID)
                const byte type = 28; // FU-A Fragmentation

                rtpPacket.Span[12] = (byte)((f_bit << 7) + (nri << 5) + type);
                rtpPacket.Span[13] = (byte)(((start ? 1 : 0) << 7) + ((end ? 1 : 0) << 6) + (0 << 5) + (firstByte & 0x1F));

                rawNal[..payload_size].CopyTo(rtpPacket[14..].Span);
                rawNal = rawNal[payload_size..];

                rtp_packets.Add(rtpPacket);

                start = false;
            }
        }

        private void AddFullNal(uint rtpTimestamp, List<Memory<byte>> rtp_packets, List<IMemoryOwner<byte>> memoryOwners, ReadOnlySpan<byte> rawNal, bool last_nal)
        {
            // Put the whole NAL into one RTP packet.
            var headerSize = RtpPacketUtil.DataOffset(0, extensionDataSizeInWord: null);

            var destSize = headerSize + rawNal.Length;
            var owner = pool.Rent(destSize);
            memoryOwners.Add(owner);
            var rtp_packet = owner.Memory[..(destSize)];

            // Create an single RTP fragment
            RtpPacketUtil.WriteHeader(rtp_packet.Span,
                RtpPacketUtil.RTP_VERSION,
                padding: false,
                hasExtension: false,
                csrcCount: 0,
                marker: last_nal, _payloadType);

            RtpPacketUtil.WriteSequenceNumber(rtp_packet.Span, SequenceNumber++);
            RtpPacketUtil.WriteSSRC(rtp_packet.Span, Ssrc);

            RtpPacketUtil.WriteTimestamp(rtp_packet.Span, rtpTimestamp);

            // Now append the raw NAL
            rawNal.CopyTo(rtp_packet[headerSize..].Span);

            rtp_packets.Add(rtp_packet);
        }
    }
}

