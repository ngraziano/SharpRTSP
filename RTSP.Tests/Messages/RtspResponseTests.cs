using NUnit.Framework;

namespace Rtsp.Messages.Tests
{
    [TestFixture()]
    public class RtspResponseTests
    {
        [Test()]
        public void SetSession()
        {
            var testObject = new RtspResponse
            {
                Session = "12345"
            };

            Assert.That(testObject.Headers[RtspHeaderNames.Session], Is.EqualTo("12345"));
        }

        [Test()]
        public void SetSessionAndTimeout()
        {
            var testObject = new RtspResponse
            {
                Session = "12345",
                Timeout = 10
            };

            Assert.That(testObject.Headers[RtspHeaderNames.Session], Is.EqualTo("12345;timeout=10"));
        }

        [Test()]
        public void ReadSessionAndDefaultTimeout()
        {
            var testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345";

            Assert.Multiple(() =>
            {
                Assert.That(testObject.Session, Is.EqualTo("12345"));
                Assert.That(testObject.Timeout, Is.EqualTo(60));
            });
        }

        [Test()]
        public void ReadSessionAndTimeout()
        {
            var testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=33";

            Assert.Multiple(() =>
            {
                Assert.That(testObject.Session, Is.EqualTo("12345"));
                Assert.That(testObject.Timeout, Is.EqualTo(33));
            });
        }

        [Test()]
        public void ChangeTimeout()
        {
            var testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=29";
            testObject.Timeout = 33;

            Assert.Multiple(() =>
            {
                Assert.That(testObject.Session, Is.EqualTo("12345"));
                Assert.That(testObject.Timeout, Is.EqualTo(33));
                Assert.That(testObject.Headers[RtspHeaderNames.Session], Is.EqualTo("12345;timeout=33"));
            });
        }

        [Test()]
        public void ChangeSession()
        {
            var testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=33";

            testObject.Session = "456";

            Assert.Multiple(() =>
            {
                Assert.That(testObject.Session, Is.EqualTo("456"));
                Assert.That(testObject.Timeout, Is.EqualTo(33));
                Assert.That(testObject.Headers[RtspHeaderNames.Session], Is.EqualTo("456;timeout=33"));
            });
        }
    }
}