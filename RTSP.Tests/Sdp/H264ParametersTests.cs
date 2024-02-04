using NUnit.Framework;

namespace Rtsp.Sdp.Tests
{
    [TestFixture()]
    public class H264ParametersTests
    {
        [Test()]
        public void Parse()
        {
            var parsed = H264Parameters.Parse("profile-level-id=42A01E; sprop-parameter-sets=Z01AH/QFgJP6,aP48gA==; packetization-mode=1;");

            Assert.That(parsed, Has.Count.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(parsed["profile-level-id"], Is.EqualTo("42A01E"));
                Assert.That(parsed["packetization-mode"], Is.EqualTo("1"));
            });
            var sprop = parsed.SpropParameterSets;
            Assert.That(sprop, Has.Count.EqualTo(2));

            byte[] result1 = [0x67, 0x4D, 0x40, 0x1F, 0xF4, 0x05, 0x80, 0x93, 0xFA];
            Assert.That(sprop[0], Is.EqualTo(result1));
            byte[] result2 = [0x68, 0xFE, 0x3C, 0x80];
            Assert.That(sprop[1], Is.EqualTo(result2));
        }
    }
}