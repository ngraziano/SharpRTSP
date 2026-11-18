#if NET
using NUnit.Framework;
using Rtsp.Rtp;
using System;
using System.Linq;

namespace RTSP.Tests
{
    /// <summary>
    /// The payload parsers allocate nothing of each packet and frame, once warmed up, of frames disposed, but the owner
    /// <see cref="System.Buffers.MemoryPool{T}.Shared"/> makes of each buffer rented: every codec, of its single and its
    /// fragmented or aggregated packets.
    /// </summary>
    public class RtpPayloadAllocationTests
    {
        // an RTP packet of a payload as it is, of no padding
        private static byte[] Rtp(bool marker, params byte[] payload)
        {
            var p = new byte[12 + payload.Length];
            p[0] = 0x80;
            p[1] = (byte)((marker ? 0x80 : 0) | 96);
            payload.CopyTo(p, 12);
            return p;
        }

        // a payload of a header and 100 bytes of data
        private static byte[] With(params byte[] header) => header.Concat(new byte[100]).ToArray();

        // what MemoryPool<byte>.Shared makes of each rent: an owner of the array, of 24 bytes on 64 bits
        private const int MostBytesABuffer = 32;

        private static void AssertNothingAllocated(string name, IPayloadProcessor parser, byte[][] frame, byte[]? prime = null)
        {
            if (prime != null)
                parser.ProcessPacket(new RtpPacket(prime)).Dispose();
            // the frames counted, and checked after: an assertion inside allocates of its own
            int framesOut = 0, buffers = 0;
            long Run(int frames)
            {
                framesOut = 0;
                buffers = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < frames; i++)
                {
                    for (int p = 0; p < frame.Length; p++)
                    {
                        using var result = parser.ProcessPacket(new RtpPacket(frame[p]));
                        if (result.Any())
                        {
                            framesOut++;
                            buffers += ((System.Collections.Generic.ICollection<ReadOnlyMemory<byte>>)result.Data).Count;
                        }
                    }
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Run(200);
            long allocated = Run(2000);
            Assert.That(framesOut, Is.EqualTo(2000), name + ": a frame of each last packet alone");
            Assert.That(allocated, Is.LessThanOrEqualTo((long)buffers * MostBytesABuffer), name);
        }

        [Test]
        public void H264() => Assert.Multiple(() =>
        {
            AssertNothingAllocated("H264 single NAL", new H264Payload(), [Rtp(true, With(0x65))]);
            AssertNothingAllocated("H264 FU-A", new H264Payload(),
                [Rtp(false, With(0x7C, 0x85)), Rtp(false, With(0x7C, 0x05)), Rtp(false, With(0x7C, 0x05)), Rtp(true, With(0x7C, 0x45))]);
            AssertNothingAllocated("H264 STAP-A", new H264Payload(), [Rtp(true, 0x78, 0x00, 0x03, 0x67, 0x42, 0x00, 0x00, 0x02, 0x68, 0xCE)]);
        });

        [Test]
        public void H265() => Assert.Multiple(() =>
        {
            AssertNothingAllocated("H265 single NAL", new H265Payload(false), [Rtp(true, With(1 << 1, 1))]);
            AssertNothingAllocated("H265 FU", new H265Payload(false),
                [Rtp(false, With(49 << 1, 1, 0x80 | 19)), Rtp(false, With(49 << 1, 1, 19)), Rtp(true, With(49 << 1, 1, 0x40 | 19))]);
        });

        [Test]
        public void H266() => Assert.Multiple(() =>
        {
            AssertNothingAllocated("H266 single NAL", new H266Payload(false), [Rtp(true, With(0, (1 << 3) | 1))]);
            AssertNothingAllocated("H266 FU", new H266Payload(false),
                [Rtp(false, With(0, (29 << 3) | 1, 0x80 | 1)), Rtp(false, With(0, (29 << 3) | 1, 1)), Rtp(true, With(0, (29 << 3) | 1, 0x60 | 1))]);
        });

        // one OBU of a frame, of no size field as RTP has them, after a sequence header
        [Test]
        public void AV1() => AssertNothingAllocated("AV1", new AV1Payload(), [Rtp(true, With(0x10, 6 << 3))], prime: Rtp(true, With(0x10, 1 << 3)));

        [Test]
        public void Audio() => Assert.Multiple(() =>
        {
            // one access unit of 100 bytes: an AU-headers-length of 16 bits, and a header of its size
            AssertNothingAllocated("AAC", new AACPayload("1210"), [Rtp(true, With(0x00, 0x10, 100 >> 5, (100 << 3) & 0xFF))]);
            AssertNothingAllocated("AMR", new AMRPayload(), [Rtp(true, With(0xF0))]);
            AssertNothingAllocated("G711", new G711Payload(), [Rtp(true, With())]);
            // mode 1: frames of 40 bytes
            AssertNothingAllocated("G711.1", new G711_1Payload(), [Rtp(true, With(0x01))]);
        });

        [Test]
        public void Others() => Assert.Multiple(() =>
        {
            // a frame of one packet of type 1, of the default tables (Q under 128), 80x64
            AssertNothingAllocated("JPEG", new JPEGPayload(), [Rtp(true, With(0, 0, 0, 0, 1, 50, 10, 8))]);
            AssertNothingAllocated("MP2T", new MP2TransportPayload(), [Rtp(true, With())]);
            AssertNothingAllocated("Raw", new RawPayload(), [Rtp(true, With())]);
        });
    }
}
#endif
