using NUnit.Framework;

namespace Rtsp.Messages.Tests
{
    [TestFixture()]
    public class PortCoupleTests
    {
        [Test()]
        public void PortCoupleOnePort()
        {
            var pc = new PortCouple(1212);
            Assert.That(pc.First, Is.EqualTo(1212));
            Assert.That(pc.IsSecondPortPresent, Is.False);
        }

        [Test()]
        public void PortCoupleTwoPort()
        {
            var pc = new PortCouple(1212, 1215);
            Assert.That(pc.First, Is.EqualTo(1212));
            Assert.That(pc.IsSecondPortPresent, Is.True);
            Assert.That(pc.Second, Is.EqualTo(1215));
        }

        [Test()]
        public void ParseOnePort()
        {
            var pc = PortCouple.Parse("1212");
            Assert.That(pc.First, Is.EqualTo(1212));
            Assert.That(pc.IsSecondPortPresent, Is.False);
        }

        [Test()]
        public void ParseTwoPort()
        {
            var pc = PortCouple.Parse("1212-1215");
            Assert.That(pc.First, Is.EqualTo(1212));
            Assert.That(pc.IsSecondPortPresent, Is.True);
            Assert.That(pc.Second, Is.EqualTo(1215));
        }

        [Test()]
        public void ToStringOnePort()
        {
            var pc = new PortCouple(1212);
            Assert.That(pc.ToString(), Is.EqualTo("1212"));
        }

        [Test()]
        public void ToStringTwoPort()
        {
            var pc = new PortCouple(1212, 1215);
            Assert.That(pc.ToString(), Is.EqualTo("1212-1215"));
        }

        [Test()]
        public void ToStringOneEqualPort()
        {
            var pc = PortCouple.Parse("1212-1212");
            Assert.That(pc.ToString(), Is.EqualTo("1212"));
        }
    }
}