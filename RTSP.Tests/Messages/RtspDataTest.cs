using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Rtsp;
using Rtsp.Messages;
using NSubstitute;

namespace Rtsp.Messages.Tests
{
    [TestFixture]
    public class RtspDataTest
    {
        [Test]
        public void Clone()
        {
            RtspData testObject = new RtspData();
            testObject.Channel = 1234;
            testObject.Data = new byte[] { 45, 63, 36, 42, 65, 00, 99 };
            testObject.SourcePort = new RtspListener(Substitute.For<IRtspTransport>());
            RtspData cloneObject = testObject.Clone() as RtspData;

            Assert.IsNotNull(cloneObject);
            Assert.AreEqual(testObject.Channel, cloneObject.Channel);
            Assert.AreEqual(testObject.Data, cloneObject.Data);
            Assert.AreSame(testObject.SourcePort, cloneObject.SourcePort);
        }
    }
}
