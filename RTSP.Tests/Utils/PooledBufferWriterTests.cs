using NUnit.Framework;
using Rtsp.Utils;
using System;

namespace RTSP.Tests.Utils;

[TestFixture]
public class PooledBufferWriterTests
{
    [Test]
    public void WriteSingleByte_ShouldIncreaseLength()
    {
        using var writer = new PooledBufferWriter();
        writer.Write((byte)42);

        Assert.That(writer.Length, Is.EqualTo(1));

        Span<byte> buffer = new byte[1];
        writer.CopyTo(buffer);
        Assert.That(buffer[0], Is.EqualTo(42));
    }

    [Test]
    public void WriteSpan_ShouldCopyAllBytes()
    {
        using var writer = new PooledBufferWriter();
        byte[] data = [1, 2, 3, 4, 5];
        writer.Write(data);

        Assert.That(writer.Length, Is.EqualTo(data.Length));

        Span<byte> buffer = new byte[data.Length];
        writer.CopyTo(buffer);
        Assert.That(buffer.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void GetMemoryAndAdvance_ShouldTrackLength()
    {
        using var writer = new PooledBufferWriter();
        var span = writer.GetSpan(10);
        span[0] = 99;
        writer.Advance(1);

        Assert.That(writer.Length, Is.EqualTo(1));

        Span<byte> buffer = new byte[1];
        writer.CopyTo(buffer);
        Assert.That(buffer[0], Is.EqualTo(99));
    }

    [Test]
    public void AdvanceBeyondCapacity_ShouldThrow()
    {
        using var writer = new PooledBufferWriter();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var span = writer.GetSpan(10);
            writer.Advance(span.Length + 1);
        });
    }

    [Test]
    public void CopyTo_WithTooSmallDestination_ShouldThrow()
    {
        using var writer = new PooledBufferWriter();
        writer.Write((byte)1);
        Span<byte> smallBuffer = [];

        Assert.Throws<ArgumentException>(() => writer.CopyTo([]));
    }

    [Test]
    public void Clear_ShouldResetLengthAndAllowReuse()
    {
        using var writer = new PooledBufferWriter();
        writer.Write([1, 2, 3]);
        writer.Clear();

        Assert.That(writer.Length, Is.EqualTo(0));

        writer.Write((byte)9);
        Span<byte> buffer = new byte[1];
        writer.CopyTo(buffer);
        Assert.That(buffer[0], Is.EqualTo(9));
    }

    [Test]
    public void Dispose_ShouldNotThrowAndAllowReuse()
    {
        using var writer = new PooledBufferWriter();
        writer.Write((byte)7);
        writer.Dispose();

        Assert.That(writer.Length, Is.EqualTo(0));

        writer.Write((byte)8);
        Span<byte> buffer = new byte[1];
        writer.CopyTo(buffer);
        Assert.That(buffer[0], Is.EqualTo(8));
    }

    [Test]
    public void WriteLargeSpan_ShouldHandleMultipleSegments()
    {
        using var writer = new PooledBufferWriter();
        byte[] data = new byte[10000];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 256);

        writer.Write(data);

        Assert.That(writer.Length, Is.EqualTo(data.Length));

        Span<byte> buffer = new byte[data.Length];
        writer.CopyTo(buffer);
        Assert.That(buffer.ToArray(), Is.EqualTo(data));
    }
}
