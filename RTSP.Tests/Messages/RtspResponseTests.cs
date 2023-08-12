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

            Assert.AreEqual("12345", testObject.Headers[RtspHeaderNames.Session]);
        }

        [Test()]
        public void SetSessionAndTimeout()
        {
            var testObject = new RtspResponse
            {
                Session = "12345",
                Timeout = 10
            };

            Assert.AreEqual("12345;timeout=10", testObject.Headers[RtspHeaderNames.Session]);
        }

        [Test()]
        public void ReadSessionAndDefaultTimeout()
        {
            var testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345";

            Assert.AreEqual("12345", testObject.Session);
            Assert.AreEqual(60, testObject.Timeout);
        }

        [Test()]
        public void ReadSessionAndTimeout()
        {
            var testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=33";

            Assert.AreEqual("12345", testObject.Session);
            Assert.AreEqual(33, testObject.Timeout);
        }

        [Test()]
        public void ChangeTimeout()
        {
            var testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=29";
            testObject.Timeout = 33;

            Assert.AreEqual("12345", testObject.Session);
            Assert.AreEqual(33, testObject.Timeout);
            Assert.AreEqual("12345;timeout=33", testObject.Headers[RtspHeaderNames.Session]);
        }

        [Test()]
        public void ChangeSession()
        {
            var testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=33";

            testObject.Session = "456";

            Assert.AreEqual("456", testObject.Session);
            Assert.AreEqual(33, testObject.Timeout);
            Assert.AreEqual("456;timeout=33", testObject.Headers[RtspHeaderNames.Session]);
        }
    }
}