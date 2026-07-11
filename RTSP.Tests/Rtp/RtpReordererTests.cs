namespace RTSP.Tests.Rtp;

using NUnit.Framework;
using Rtsp.Rtp;
using System;
using System.Buffers.Binary;

[TestFixture]
public class RtpReordererTests
{
    private static RtpPacket CreatePacket(ushort seq, uint ssrc = 0, byte payloadValue = 0)
    {
        var data = new byte[12 + 1];
        data[0] = 0x80; // V=2
        data[1] = 0x60; // PT=96
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), seq);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), ssrc);
        data[12] = payloadValue;
        return new RtpPacket(data);
    }

    [Test]
    public void InOrderPackets()
    {
        using var reorderer = new RtpReorderer(10);

        var p0 = reorderer.Process(CreatePacket(100, payloadValue: 100));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(p0.IsWellFormed, Is.True);
            Assert.That(p0.SequenceNumber, Is.EqualTo(100));
            Assert.That(p0.Payload[0], Is.EqualTo(100));
        }

        var p1 = reorderer.Process(CreatePacket(101, payloadValue: 101));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(p1.IsWellFormed, Is.True);
            Assert.That(p1.SequenceNumber, Is.EqualTo(101));
            Assert.That(p1.Payload[0], Is.EqualTo(101));
        }
    }

    [Test]
    public void Reordering()
    {
        using var reorderer = new RtpReorderer(10);

        // Start with 100
        reorderer.Process(CreatePacket(100));

        // Receive 102 then 101
        var p102 = reorderer.Process(CreatePacket(102, payloadValue: 102));
        Assert.That(p102.IsWellFormed, Is.False); // Buffered

        var p101 = reorderer.Process(CreatePacket(101, payloadValue: 101));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(p101.IsWellFormed, Is.True);
            Assert.That(p101.SequenceNumber, Is.EqualTo(101));
            Assert.That(p101.Payload[0], Is.EqualTo(101));
        }

        // Depile 102
        var p102Next = reorderer.Process();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(p102Next.IsWellFormed, Is.True);
            Assert.That(p102Next.SequenceNumber, Is.EqualTo(102));
            Assert.That(p102Next.Payload[0], Is.EqualTo(102));
        }
    }

    [Test]
    public void ReorderingWithGaps()
    {
        using var reorderer = new RtpReorderer(10);

        // Receive 100, 102, 103, then 101
        reorderer.Process(CreatePacket(100));
        reorderer.Process(CreatePacket(102));
        reorderer.Process(CreatePacket(103));

        var p101 = reorderer.Process(CreatePacket(101));
        Assert.That(p101.SequenceNumber, Is.EqualTo(101));

        var p102 = reorderer.Process();
        Assert.That(p102.SequenceNumber, Is.EqualTo(102));

        var p103 = reorderer.Process();
        Assert.That(p103.SequenceNumber, Is.EqualTo(103));
    }

    [Test]
    public void Duplicates()
    {
        using var reorderer = new RtpReorderer(10);

        reorderer.Process(CreatePacket(100));
        var dup = reorderer.Process(CreatePacket(100));
        Assert.That(dup.IsWellFormed, Is.False);
    }

    [Test]
    public void PacketLossAndJump()
    {
        using var reorderer = new RtpReorderer(5);

        reorderer.Process(CreatePacket(100));

        // Receive 110 (Gap = 9, which is > MaxGap=5)
        var p110 = reorderer.Process(CreatePacket(110));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(p110.IsWellFormed, Is.True);
            Assert.That(p110.SequenceNumber, Is.EqualTo(110));
        }

        // Expecting 111 now
        var p111 = reorderer.Process(CreatePacket(111));
        Assert.That(p111.SequenceNumber, Is.EqualTo(111));
    }

    [Test]
    public void SsrcChange()
    {
        using var reorderer = new RtpReorderer(10);

        reorderer.Process(CreatePacket(100, 1234));

        // Buffer 105
        reorderer.Process(CreatePacket(105, 1234));

        // SSRC change should clear buffer and start fresh
        var p200 = reorderer.Process(CreatePacket(200, 5678));
        Assert.That(p200.SequenceNumber, Is.EqualTo(200));

        // Check if 105 is still there? It should be gone.
        var p105 = reorderer.Process();
        Assert.That(p105.IsWellFormed, Is.False);
    }

    [Test]
    public void WrapAround()
    {
        using var reorderer = new RtpReorderer(10);

        reorderer.Process(CreatePacket(65535));
        var p0 = reorderer.Process(CreatePacket(0));
        Assert.That(p0.SequenceNumber, Is.EqualTo(0));
    }
}