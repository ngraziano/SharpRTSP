using NUnit.Framework;
using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Messages.Tests
{
    [TestFixture()]
    public class RtspResponseTests
    {
        [Test()]
        public void SetSession()
        {
            RtspResponse testObject = new RtspResponse();

            testObject.Session = "12345";

            Assert.AreEqual("12345", testObject.Headers[RtspHeaderNames.Session]);

        }

        [Test()]
        public void SetSessionAndTimeout()
        {
            RtspResponse testObject = new RtspResponse();

            testObject.Session = "12345";
            testObject.Timeout = 10;

            Assert.AreEqual("12345;timeout=10", testObject.Headers[RtspHeaderNames.Session]);

        }

        [Test()]
        public void ReadSessionAndDefaultTimeout()
        {
            RtspResponse testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345";

            Assert.AreEqual("12345", testObject.Session);
            Assert.AreEqual(60, testObject.Timeout);

        }


        [Test()]
        public void ReadSessionAndTimeout()
        {
            RtspResponse testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=33";
            
            Assert.AreEqual("12345", testObject.Session);
            Assert.AreEqual(33, testObject.Timeout);

        }


        [Test()]
        public void ChangeTimeout()
        {
            RtspResponse testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=29";
            testObject.Timeout = 33;

            Assert.AreEqual("12345", testObject.Session);
            Assert.AreEqual(33, testObject.Timeout);
            Assert.AreEqual("12345;timeout=33", testObject.Headers[RtspHeaderNames.Session]);

        }


        [Test()]
        public void ChangeSession()
        {
            RtspResponse testObject = new RtspResponse();

            testObject.Headers[RtspHeaderNames.Session] = "12345;timeout=33";

            testObject.Session = "456";

            Assert.AreEqual("456", testObject.Session);
            Assert.AreEqual(33, testObject.Timeout);
            Assert.AreEqual("456;timeout=33", testObject.Headers[RtspHeaderNames.Session]);

        }

    }
}