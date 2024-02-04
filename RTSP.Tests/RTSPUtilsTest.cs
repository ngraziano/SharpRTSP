using NUnit.Framework;
using System;

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
            Assert.That(Uri.CheckSchemeName("rtsp"), Is.True);

            // Check that the default port is well defined.
            Uri testUri = new("rtsp://exemple.com/test");
            Assert.That(testUri.Port, Is.EqualTo(554));
        }
    }
}
