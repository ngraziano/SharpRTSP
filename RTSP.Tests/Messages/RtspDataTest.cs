using NSubstitute;
using NUnit.Framework;

namespace Rtsp.Messages.Tests
{
    [TestFixture]
    public class RtspDataTest
    {
        [Test]
        public void Clone()
        {
            RtspData testObject = new()
            {
                Channel = 1234,
                Data = new byte[] { 45, 63, 36, 42, 65, 00, 99 },
                SourcePort = new RtspListener(Substitute.For<IRtspTransport>())
            };
            RtspData cloneObject = testObject.Clone() as RtspData;

            Assert.That(cloneObject,Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(cloneObject.Channel, Is.EqualTo(testObject.Channel));
                Assert.That(cloneObject.Data, Is.EqualTo(testObject.Data));
                Assert.That(cloneObject.SourcePort, Is.SameAs(testObject.SourcePort));
            });
        }
    }
}
