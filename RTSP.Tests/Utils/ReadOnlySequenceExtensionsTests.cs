using NUnit.Framework;
using System;
using System.Buffers;
using System.Linq;

namespace Rtsp.Utils.Tests
{
    [TestFixture()]
    public class ReadOnlySequenceExtensionsTests
    {
        public class TestSegment : ReadOnlySequenceSegment<byte>
        {
            public TestSegment(Memory<byte> memory)
            {
                Memory = memory;
            }

            public TestSegment Append(Memory<byte> memory)
            {
                var segment = new TestSegment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = segment;
                return segment;
            }
        }

        internal static ReadOnlySequence<byte> CreateSequence(params byte[][] buffers)
        {
            var firstSegment = new TestSegment(buffers[0]);
            var lastSegment = firstSegment;
            for (int i = 1; i < buffers.Length; i++)
            {
                lastSegment = lastSegment.Append(buffers[i]);
            }
            return new ReadOnlySequence<byte>(firstSegment, 0, lastSegment, lastSegment.Memory.Length);
        }


        [Test()]
        public void FindEndOfLineTestSimple()
        {
            var sequence = CreateSequence("\r\n"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.Empty);
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.Empty);
        }

        [Test()]
        public void FindEndOfLineTestEmpty()
        {
            var sequence = CreateSequence(""u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Null);
        }

        [Test()]
        public void FindEndOfLineNoCrLf()
        {
            var sequence = CreateSequence("azererze"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Null);
        }

        [Test()]
        public void FindEndOfLineNoCrLfMulti()
        {
            var sequence = CreateSequence("azererze"u8.ToArray(), "fdsfsdf"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Null);
        }

        [Test()]
        public void FindEndOfLineCrLfMulti()
        {
            var sequence = CreateSequence("azererze"u8.ToArray(), "fdsf\r\nsdf"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.EqualTo("azererzefdsf"u8.ToArray()));
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.EqualTo("sdf"u8.ToArray()));
        }

        [Test()]
        public void FindEndOfLineCrMulti()
        {
            var sequence = CreateSequence("azererze"u8.ToArray(), "fdsf\rsd\nf"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.EqualTo("azererzefdsf"u8.ToArray()));
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.EqualTo("sd\nf"u8.ToArray()));
        }

        [Test()]
        public void FindEndOfLineLfMulti()
        {
            var sequence = CreateSequence("azererze"u8.ToArray(), "fdsf\nsd\nf"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.EqualTo("azererzefdsf"u8.ToArray()));
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.EqualTo("sd\nf"u8.ToArray()));
        }

        [Test()]
        public void FindEndOfLineCrLfMultiSplit()
        {
            var sequence = CreateSequence("azererze\r"u8.ToArray(), "\nsdf"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.EqualTo("azererze"u8.ToArray()));
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.EqualTo("sdf"u8.ToArray()));
        }

        [Test()]
        public void FindEndOfLineCrMultiSplit()
        {
            var sequence = CreateSequence("azererze\r"u8.ToArray(), "sdf"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.EqualTo("azererze"u8.ToArray()));
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.EqualTo("sdf"u8.ToArray()));
        }

        [Test()]
        public void FindEndOfLineLnMultiSplit()
        {
            var sequence = CreateSequence("azererze\n"u8.ToArray(), "sdf"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.EqualTo("azererze"u8.ToArray()));
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.EqualTo("sdf"u8.ToArray()));
        }

        [Test()]
        public void FindEndOfLineFinalCrLnMultiSplit()
        {
            var sequence = CreateSequence("azererze"u8.ToArray(), "sdf\r\n"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.EqualTo("azererzesdf"u8.ToArray()));
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.Empty);
        }

        [Test()]
        public void FindEndOfLineFinalLnMultiSplit()
        {
            var sequence = CreateSequence("azererze"u8.ToArray(), "sdf\n"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Not.Null);
            var (endOfLinePos, startOfLinePos) = result.Value;
            Assert.That(sequence.Slice(sequence.Start, endOfLinePos).ToArray(), Is.EqualTo("azererzesdf"u8.ToArray()));
            Assert.That(sequence.Slice(startOfLinePos).ToArray(), Is.Empty);
        }

        [Test()]
        public void FindEndOfLineFinalCrMultiSplit()
        {
            var sequence = CreateSequence("azererze"u8.ToArray(), "sdf\r"u8.ToArray());
            var result = sequence.FindEndOfLine();
            Assert.That(result, Is.Null);
        }
    }
}