using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Rtsp;

namespace Rtsp.Tests
{
    [TestFixture]
    public class RtspUtilsTest
    {
        [Test]
        public void RegisterUri()
        {
            RtspUtils.RegisterUri();

            // Check that rtsp is well registred
            Assert.IsTrue(Uri.CheckSchemeName("rtsp"));

            // Check that the default port is well defined.
            Uri testUri = new Uri("rtsp://exemple.com/test");
            Assert.AreEqual(554, testUri.Port);
        }
    }
}
