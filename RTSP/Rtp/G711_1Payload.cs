using Rtsp.Onvif;
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
        // null: of arrays of ArrayPool<byte>.Shared, of no owner object of each buffer
        private readonly MemoryPool<byte>? _memoryPool;
        private readonly List<ReadOnlyMemory<byte>> _audioData = [];
        private readonly List<IMemoryOwner<byte>> _owners = [];
        private readonly List<byte[]> _arrays = [];

        public G711_1Payload(MemoryPool<byte>? memoryPool = null)
        {
            _memoryPool = memoryPool;
        }

        public RawMediaFrame ProcessPacket(RtpPacket packet)
        {
            // Look at the Header. This tells us the G711 mode being used

            // Mode Index (MI) is
            // 1 - R1 40 octets containg Layer 0 data
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

            // the parser's own lists, copied into the frame
            var audioDatas = _audioData;
            var owners = _owners;
            audioDatas.Clear();
            owners.Clear();
            _arrays.Clear();

            // Extract each audio frame and place in the audio_data List
            int frame_start = 1; // starts just after the MI header
            while (frame_start + sizeOfOneFrame < rtpPayload.Length)
            {
                // Return just the basic u-Law or A-Law audio (the Layer 0 audio)
                var memory = RawMediaFrame.RentBuffer(_memoryPool, 40, owners, _arrays);
                // only copy the Layer 0 data (the first 40 bytes)
                rtpPayload[frame_start..(frame_start + 40)].CopyTo(memory.Span);
                audioDatas.Add(memory);
                frame_start += sizeOfOneFrame;
            }
            var frame = RawMediaFrame.Rent(audioDatas, owners, _arrays, packet.Timestamp, RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _));
            audioDatas.Clear();
            owners.Clear();
            _arrays.Clear();
            return frame;
        }
    }
}
