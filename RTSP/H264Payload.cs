using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rtsp.Rtp;
using System;
using System.Collections.Generic;
using System.IO;

namespace Rtsp
{
    // This class handles the H264 Payload
    // It has methods to parse parameters in the SDP
    // It has methods to process the RTP Payload

    public class H264Payload : IPayloadProcessor
    {
        private readonly ILogger _logger;

        int norm, fu_a, fu_b, stap_a, stap_b, mtap16, mtap24 = 0; // used for diagnostics stats

        // used to assemble the RTP packets that form one RTP Frame
        // Eg all the RTP Packets from M=0 through to M=1
        private readonly List<ReadOnlyMemory<byte>> temporaryRtpPayloads = new();



        private readonly MemoryStream fragmentedNal = new(); // used to concatenate fragmented H264 NALs where NALs are split over RTP packets


        // Constructor
        public H264Payload(ILogger<H264Payload>? logger)
        {
            _logger = logger as ILogger ?? NullLogger.Instance;
        }

        public List<ReadOnlyMemory<byte>> ProcessRTPPacket(RtpPacket packet)
        {

            // Add to the list of payloads for the current Frame of video
            temporaryRtpPayloads.Add(packet.Payload); // Todo Could optimise this and go direct to Process Frame if just 1 packet in frame

            if (packet.IsMarker)
            {
                // End Marker is set. Process the list of RTP Packets (forming 1 RTP frame) and save the NALs to a file
                var nalUnits = ProcessH264RTPFrame(temporaryRtpPayloads);
                temporaryRtpPayloads.Clear();

                return nalUnits;
            }
            // we don't have a frame yet. Keep accumulating RTP packets
            return new();
        }


        // Process a RTP Frame. A RTP Frame can consist of several RTP Packets which have the same Timestamp
        // Returns a list of NAL Units (with no 00 00 00 01 header and with no Size header)
        private List<ReadOnlyMemory<byte>> ProcessH264RTPFrame(List<ReadOnlyMemory<byte>> rtp_payloads)
        {
            _logger.LogDebug("RTP Data comprised of {payloadCount} rtp packets", rtp_payloads.Count);

            // Stores the NAL units for a Video Frame. May be more than one NAL unit in a video frame.
            List<ReadOnlyMemory<byte>> nalUnits = new();

            foreach (var payloadMemory in rtp_payloads)
            {
                var payload = payloadMemory.Span;
                // Examine the first rtp_payload and the first byte (the NAL header)
                int nal_header_f_bit = (payload[0] >> 7) & 0x01;
                int nal_header_nri = (payload[0] >> 5) & 0x03;
                int nal_header_type = (payload[0] >> 0) & 0x1F;

                // If the Nal Header Type is in the range 1..23 this is a normal NAL (not fragmented)
                // So write the NAL to the file
                if (nal_header_type >= 1 && nal_header_type <= 23)
                {
                    _logger.LogDebug("Normal NAL");
                    norm++;
                    nalUnits.Add(payloadMemory);
                }
                // There are 4 types of Aggregation Packet (split over RTP payloads)
                else if (nal_header_type == 24)
                {
                    _logger.LogDebug("Agg STAP-A");
                    stap_a++;

                    // RTP packet contains multiple NALs, each with a 16 bit header
                    //   Read 16 byte size
                    //   Read NAL
                    try
                    {
                        int ptr = 1; // start after the nal_header_type which was '24'
                        // if we have at least 2 more bytes (the 16 bit size) then consume more data
                        while (ptr + 2 < (payload.Length - 1))
                        {
                            int size = (payload[ptr] << 8) + (payload[ptr + 1] << 0);
                            ptr += 2;
                            // Add to list of NALs for this RTP frame. Start Codes like 00 00 00 01 get added later
                            nalUnits.Add(payloadMemory[ptr..(ptr + size)]);
                            ptr += size;
                        }
                    }
                    catch
                    {
                        _logger.LogDebug("H264 Aggregate Packet processing error");
                    }
                }
                else if (nal_header_type == 25)
                {
                    _logger.LogDebug("Agg STAP-B not supported");
                    stap_b++;
                }
                else if (nal_header_type == 26)
                {
                    _logger.LogDebug("Agg MTAP16 not supported");
                    mtap16++;
                }
                else if (nal_header_type == 27)
                {
                    _logger.LogDebug("Agg MTAP24 not supported");
                    mtap24++;
                }
                else if (nal_header_type == 28)
                {
                    _logger.LogDebug("Frag FU-A");
                    fu_a++;

                    // Parse Fragmentation Unit Header
                    int fu_header_s = (payload[1] >> 7) & 0x01;  // start marker
                    int fu_header_e = (payload[1] >> 6) & 0x01;  // end marker
                    int fu_header_r = (payload[1] >> 5) & 0x01;  // reserved. should be 0
                    int fu_header_type = (payload[1] >> 0) & 0x1F; // Original NAL unit header

                    _logger.LogDebug("Frag FU-A s={fuHeadersS} e={fuHeadersE}", fu_header_s, fu_header_e);

                    // Check Start and End flags
                    if (fu_header_s == 1 && fu_header_e == 0)
                    {
                        // Start of Fragment.
                        // Initiise the fragmented_nal byte array
                        // Build the NAL header with the original F and NRI flags but use the the Type field from the fu_header_type
                        byte reconstructed_nal_type = (byte)((nal_header_f_bit << 7) + (nal_header_nri << 5) + fu_header_type);

                        // Empty the stream
                        fragmentedNal.SetLength(0);

                        // Add reconstructed_nal_type byte to the memory stream
                        fragmentedNal.WriteByte(reconstructed_nal_type);

                        // copy the rest of the RTP payload to the memory stream
                        fragmentedNal.Write(payload[2..]);
                    }

                    if (fu_header_s == 0 && fu_header_e == 0)
                    {
                        // Middle part of Fragment
                        // Append this payload to the fragmented_nal
                        // Data starts after the NAL Unit Type byte and the FU Header byte
                        fragmentedNal.Write(payload[2..]);
                    }

                    if (fu_header_s == 0 && fu_header_e == 1)
                    {
                        // End part of Fragment
                        // Append this payload to the fragmented_nal
                        // Data starts after the NAL Unit Type byte and the FU Header byte
                        fragmentedNal.Write(payload[2..]);

                        // Add the NAL to the array of NAL units
                        nalUnits.Add(fragmentedNal.ToArray());
                    }
                }

                else if (nal_header_type == 29)
                {
                    _logger.LogDebug("Frag FU-B not supported");
                    fu_b++;
                }
                else
                {
                    _logger.LogDebug("Unknown NAL header {nalHeaderType} not supported", nal_header_type);
                }

            }

            // Output some statistics
            _logger.LogDebug("Norm={norm} ST-A={stapA} ST-B={stapB} M16={mtap16} M24={mtap24} FU-A={fuA} FU-B={fuB}",
                norm, stap_a, stap_b, mtap16, mtap24, fu_a, fu_b);

            // Output all the NALs that form one RTP Frame (one frame of video)
            return nalUnits;

        }
    }
}
