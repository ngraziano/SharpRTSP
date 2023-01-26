using Rtsp.Onvif;
using Rtsp.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace Rtsp.Rtp
{
    /// <summary>
    /// This class handles the G711.1 Payload
    /// </summary>
    public class G711_1Payload : IPayloadProcessor
    {
        private readonly MemoryPool<byte>? _memoryPool;

        public G711_1Payload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool;
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            // Look at the Header. This tells us the G711 mode being used

            // Mode Index (MI) is
            // 1 - R1 40 octets containing Layer 0 data
            // 2 - R2a 50 octets containing Layer 0 plus Layer 1 data
            // 3 - R2b 50 octets containing Layer 0 plus Layer 2 data
            // 4 - R3 60 octets containing Layer 0 plus Layer 1 plus Layer 2 data

            var rtpPayload = packet.Payload;
            byte modeIndex = (byte)(rtpPayload[0] & 0x07);
            int sizeOfOneFrame = modeIndex switch
            {
                1 => 40,
                2 => 50,
                3 => 50,
                4 => 60,
                _ => 0,
            };
            if (sizeOfOneFrame == 0)
            {
                // ERROR
                return RawMediaFrame.Empty;
            }

            var memoryPool = new PooledSequence(_memoryPool);
            // Extract each audio frame and place in the audio_data List
            int frame_start = 1; // starts just after the MI header
            while (frame_start + sizeOfOneFrame < rtpPayload.Length)
            {
                // Return just the basic u-Law or A-Law audio (the Layer 0 audio)
                var memory = memoryPool.GetMemory(40);
                // only copy the Layer 0 data (the first 40 bytes)
                rtpPayload[frame_start..(frame_start + 40)].CopyTo(memory.Span);
                frame_start += sizeOfOneFrame;
            }
            return new(memoryPool.GetReadOnlySequence(), memoryPool)
            {
                ClockTimestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _),
                RtpTimestamp = packet.Timestamp,
            };
        }
    }
}
