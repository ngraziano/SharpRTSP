using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rtsp.Onvif;
using Rtsp.Utils;
using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Rtsp.Rtp
{
    // This class handles the H265 Payload
    // It has methods to parse parameters in the SDP
    // It has methods to process the RTP Payload

    // By Roger Hardiman, RJH Technical Consultancy Ltd
    public class H265Payload : IPayloadProcessor
    {
        private readonly ILogger _logger;

        // H265 / HEVC structure.
        // An 'Access Unit' is the set of NAL Units that form one Picture
        // NAL Units have a 2 byte header comprising of
        // F Bit, Type, Layer ID and TID

        private readonly bool hasDonl;

        private readonly PooledSequence nalsBuffer;
        // used to concatenate fragmented NALs where NALs are split over RTP packets
        private readonly PooledBufferWriter fragmentedNal;

        private DateTime _timestamp;

        // Constructor
        public H265Payload(bool hasDonl, ILogger<H265Payload>? logger, MemoryPool<byte>? memoryPool = null)
        {
            this.hasDonl = hasDonl;

            _logger = logger as ILogger ?? NullLogger.Instance;
            fragmentedNal = new(memoryPool);
            nalsBuffer = new(memoryPool);
        }

        /// <summary>
        /// Process a RTP Frame and extract the NAL and add it to the list.
        /// </summary>
        /// <param name="payload">An RTP packer</param>
        private void ProcessRTPFrame(ReadOnlySpan<byte> payload)
        {
            // Examine the first two bytes of the RTP data, the Payload Header
            // F (Forbidden Bit),
            // Type of NAL Unit (or VCL NAL Unit if Type is < 32),
            // LayerId
            // TID  (TemporalID = TID - 1)
            /*+---------------+---------------+
             *|0|1|2|3|4|5|6|7|0|1|2|3|4|5|6|7|
             *+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
             *|F|   Type    |  LayerId  | TID |
             *+-------------+-----------------+
             */

            int payloadHeader = BinaryPrimitives.ReadUInt16BigEndian(payload);
            int Fbit = payloadHeader >> 15 & 0x01;
            if (Fbit != 0)
            {
                _logger.LogWarning("F Bit is set in H265 Payload Header, invalid packet");
                return;
            }

            int type = payloadHeader >> 9 & 0x3F;
            // int payload_header_layer_id = payloadHeader >> 3 & 0x3F;
            // int payload_header_tid = payloadHeader & 0x7;

            // There are three ways to Packetize NAL units into RTP Packets
            //  Single NAL Unit Packet
            //  Aggregation Packet (payload_header_type = 48)
            //  Fragmentation Unit (payload_header_type = 49)

            // Aggregation Packet
            if (type == 48)
            {
                SplitAggregationPayload(payload);
            }
            // Fragmentation Unit
            else if (type == 49)
            {
                AggragateFragmentationPayload(payload, payloadHeader);
            }
            else
            {
                // Single NAL Unit Packet
                // 32=VPS
                // 33=SPS
                // 34=PPS
                _logger.LogTrace("Single NAL");
                var nalSpan = PrepareNewNal(payload.Length);
                payload.CopyTo(nalSpan);
            }
        }

        private void AggragateFragmentationPayload(ReadOnlySpan<byte> payload, int payloadHeader)
        {
            _logger.LogTrace("Fragmentation Unit");

            // Parse Fragmentation Unit Header
            int fu_header_s = payload[2] >> 7 & 0x01;  // start marker
            int fu_header_e = payload[2] >> 6 & 0x01;  // end marker
            int fu_header_type = payload[2] >> 0 & 0x3F; // fu type

            _logger.LogTrace("Frag FU-A s={headerS} e={headerE}", fu_header_s, fu_header_e);

            // Check Start flag
            if (fu_header_s == 1)
            {
                // Start of Fragment.
                // Initiise the fragmented_nal byte array

                // Empty the stream
                fragmentedNal.Clear();

                // Reconstrut the NAL header from the rtp_payload_header, replacing the Type with FU Type
                int nal_header = payloadHeader & 0x81FF; // strip out existing 'type'
                nal_header |= fu_header_type << 9;
                fragmentedNal.Write((byte)(nal_header >> 8 & 0xFF));
                fragmentedNal.Write((byte)(nal_header >> 0 & 0xFF));
            }

            // Part of Fragment
            // Append this payload to the fragmented_nal
            if (hasDonl)
            {
                // start copying after the DONL data
                fragmentedNal.Write(payload[5..]);
            }
            else
            {
                // there is no DONL data
                fragmentedNal.Write(payload[3..]);
            }

            // Check end Flag
            if (fu_header_e == 1)
            {
                // Add the NAL to the array of NAL units
                var nalSpan = PrepareNewNal(fragmentedNal.Length);
                fragmentedNal.CopyTo(nalSpan);
            }
        }

        private void SplitAggregationPayload(ReadOnlySpan<byte> payload)
        {
            _logger.LogTrace("Aggregation Packet");

            // RTP packet contains multiple NALs, each with a 16 bit header
            //   Read 16 byte size
            //   Read NAL
            // Use a Try/Catch to protect from bad RTP data where block sizes exceed the
            // available data
            try
            {
                int ptr = 2; // start after 16 bit Payload Header
                             // loop until the ptr has moved beyond the length of the data
                while (ptr < payload.Length - 1)
                {
                    if (hasDonl) ptr += 2; // step over the DONL data
                    int size = BinaryPrimitives.ReadUInt16BigEndian(payload[ptr..]);

                    ptr += 2;
                    var nalSpan = PrepareNewNal(size);
                    // copy the NAL
                    payload[ptr..(ptr + size)].CopyTo(nalSpan);
                    ptr += size;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "H265 Aggregate Packet processing error");
            }
        }

        private Span<byte> PrepareNewNal(int sizeWitoutHeader)
        {
            var memory = nalsBuffer.GetMemory(sizeWitoutHeader + 4);
            // Add the NAL start code 00 00 00 01
            memory.Span[0] = 0;
            memory.Span[1] = 0;
            memory.Span[2] = 0;
            memory.Span[3] = 1;
            return memory[4..].Span;
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

            // End Marker is set return the list of NALs
            // clone list of nalUnits and owners

            // FIXME : Why a deep copy here if we suppress the original buffers just after
            // it's a copy that was not present before.
            var data = nalsBuffer.Clone();
            var result = new RawMediaFrame(data.GetReadOnlySequence(), data)
            {
                RtpTimestamp = packet.Timestamp,
                ClockTimestamp = _timestamp,
            };
            nalsBuffer.Clear();
            return result;
        }
    }
}
