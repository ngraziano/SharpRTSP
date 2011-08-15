using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Rtsp.Messages.Tests
{
    [TestFixture]
    public class RtspTransportTest
    {
        [Test]
        public void DefaultValue()
        {
            RtspTransport testValue = new RtspTransport();
            Assert.IsTrue(testValue.IsMulticast);
            Assert.AreEqual(RtspTransport.LowerTransportType.UDP, testValue.LowerTransport);
            Assert.AreEqual("PLAY", testValue.Mode);
        }


    }
}
