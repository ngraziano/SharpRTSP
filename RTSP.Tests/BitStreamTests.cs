using NUnit.Framework;

namespace Rtsp.Tests
{
    [TestFixture]
    public class BitStreamTests
    {
        [Test]
        public void AddValueTest()
        {
            BitStream bitstream = new();

            bitstream.AddValue(0xA, 4);
            bitstream.AddValue(0xB, 4);
            bitstream.AddValue(0xC, 4);
            bitstream.AddValue(0xD, 4);
            bitstream.AddValue(0xE, 4);
            var vals = bitstream.ToArray();
            Assert.That(vals, Is.EqualTo(new byte[] { 0xAB, 0xCD, 0xE0 }));
        }

        [Test]
        public void ReadTest()
        {
            BitStream bitstream = new();
            bitstream.AddHexString("ABCDEF1234567890");
            Assert.That(bitstream.Read(8), Is.EqualTo(0xAB));
            Assert.That(bitstream.Read(4), Is.EqualTo(0xC));
            Assert.That(bitstream.Read(4), Is.EqualTo(0xD));
            Assert.That(bitstream.Read(8), Is.EqualTo(0xEF));
            Assert.That(bitstream.Read(16), Is.EqualTo(0x1234));
            Assert.That(bitstream.Read(2), Is.EqualTo(1));
            Assert.That(bitstream.Read(2), Is.EqualTo(1));
        }

        [Test]
        public void ReadTestLowerCase()
        {
            BitStream bitstream = new();
            bitstream.AddHexString("abcdef1234567890");
            Assert.That(bitstream.Read(8), Is.EqualTo(0xAB));
            Assert.That(bitstream.Read(4), Is.EqualTo(0xC));
            Assert.That(bitstream.Read(4), Is.EqualTo(0xD));
            Assert.That(bitstream.Read(8), Is.EqualTo(0xEF));
            Assert.That(bitstream.Read(16), Is.EqualTo(0x1234));
            Assert.That(bitstream.Read(2), Is.EqualTo(1));
            Assert.That(bitstream.Read(2), Is.EqualTo(1));
        }
    }
}