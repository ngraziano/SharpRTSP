using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace Rtsp.Utils.Tests
{
    [TestFixture()]
    public class PooledSequenceTests
    {
        [Test]
        public void GetReadOnlySequence_ShouldReturnEmpty_WhenNoMemoryRequested()
        {
            using var buffer = new PooledSequence();

            var sequence = buffer.GetReadOnlySequence();

            Assert.That(sequence.IsEmpty, Is.True, "Sequence should be empty when no memory has been requested");
            Assert.That(buffer.Length, Is.EqualTo(0), "Length should be 0 when no memory has been requested");
        }

        [Test]
        public void GetReadOnlySequence_ShouldMatchMemoryRequestsAndTotalLength()
        {
            using var buffer = new PooledSequence();

            var sizes = new[] { 3, 0, 5, 2 };
            var expectedSegments = new byte[sizes.Length][];
            var expectedTotalLength = 0;

            for (int i = 0; i < sizes.Length; i++)
            {
                var size = sizes[i];
                var mem = buffer.GetMemory(size);
                var data = Enumerable.Range(i + 1, size).Select(x => (byte)x).ToArray();
                data.CopyTo(mem);
                expectedSegments[i] = data;
                expectedTotalLength += size;
            }

            var sequence = buffer.GetReadOnlySequence();

            // Direct comparison between buffer.Length and sequence.Length
            Assert.That(sequence.Length, Is.EqualTo(buffer.Length), "Sequence length should match buffer.Length");

            // Validate total length
            Assert.That(sequence.Length, Is.EqualTo(expectedTotalLength), "Sequence length mismatch");
            Assert.That(buffer.Length, Is.EqualTo(expectedTotalLength), "Buffer.Length mismatch");

            // Validate segment structure
            var segments = new List<ReadOnlyMemory<byte>>();
            var position = sequence.Start;

            while (sequence.TryGet(ref position, out var memory))
            {
                segments.Add(memory);
            }

            Assert.That(segments.Count, Is.EqualTo(sizes.Length), "Segment count mismatch");

            for (int i = 0; i < sizes.Length; i++)
            {
                var segment = segments[i];
                var expected = expectedSegments[i];

                Assert.That(segment.Length, Is.EqualTo(expected.Length), $"Segment {i} length mismatch");
                Assert.That(segment.ToArray(), Is.EqualTo(expected), $"Segment {i} data mismatch");
            }
        }

        [Test]
        public void Clear_ShouldDisposeBuffersAndResetState()
        {
            using var buffer = new PooledSequence();

            // Add some buffers
            buffer.GetMemory(4).Span.Fill(1);
            buffer.GetMemory(6).Span.Fill(2);

            Assert.That(buffer.Length, Is.EqualTo(10), "Precondition failed: Length should be 10");

            // Clear the buffer
            buffer.Clear();

            // Validate state after clearing
            Assert.That(buffer.Length, Is.EqualTo(0), "Length should be reset to 0 after Clear()");
            Assert.That(buffer.GetReadOnlySequence().IsEmpty, Is.True, "Sequence should be empty after Clear()");

            // Add new memory after clearing
            var mem = buffer.GetMemory(3);
            mem.Span.Fill(9);

            Assert.That(buffer.Length, Is.EqualTo(3), "Length should reflect new memory after Clear()");
            Assert.That(buffer.GetReadOnlySequence().ToArray(), Is.EqualTo(new byte[] { 9, 9, 9 }));
        }

        [Test]
        public void Clone_ShouldCreateIndependentCopyWithSameContentAndLength()
        {
            using var original = new PooledSequence();

            // Write distinct data to original buffer
            var mem1 = original.GetMemory(3);
            var mem2 = original.GetMemory(2);
            mem1.Span[0] = 10;
            mem1.Span[1] = 20;
            mem1.Span[2] = 30;
            mem2.Span[0] = 40;
            mem2.Span[1] = 50;

            var originalData = original.GetReadOnlySequence().ToArray();
            var originalLength = original.Length;

            // Clone the buffer
            using var clone = original.Clone();

            // Validate content and length
            Assert.That(clone.Length, Is.EqualTo(originalLength), "Clone should have same length as original");
            Assert.That(clone.GetReadOnlySequence().ToArray(), Is.EqualTo(originalData), "Clone should have same content as original");

            // Modify original and ensure clone is unaffected
            mem1.Span.Fill(99);
            var modifiedOriginal = original.GetReadOnlySequence().ToArray();
            var cloneData = clone.GetReadOnlySequence().ToArray();

            Assert.That(cloneData, Is.Not.EqualTo(modifiedOriginal), "Clone should not reflect changes to original");
        }

        [Test]
        public void CopyTo_ShouldCopyAllDataIntoDestinationSpan()
        {
            using var buffer = new PooledSequence();

            buffer.GetMemory(3).Span.Fill(1);
            buffer.GetMemory(2).Span.Fill(2);

            var destination = new byte[5];
            buffer.CopyTo(destination);

            Assert.That(destination, Is.EqualTo(new byte[] { 1, 1, 1, 2, 2 }));
        }

        [Test]
        public void CopyTo_ShouldThrowIfDestinationTooSmall()
        {
            using var buffer = new PooledSequence();
            buffer.GetMemory(4).Span.Fill(9);

            var destination = new byte[3]; // Too small

            Assert.Throws<ArgumentException>(() => buffer.CopyTo(destination));
        }

        [Test]
        public void CopyTo_ShouldThrowIfDisposed()
        {
            var buffer = new PooledSequence();
            buffer.GetMemory(2);
            buffer.Dispose();

            var destination = new byte[2];
            Assert.Throws<ObjectDisposedException>(() => buffer.CopyTo(destination));
        }
    }
}
