using NUnit.Framework;
using Rtsp.Onvif;
using System;

namespace Rtsp.Tests.Onvif
{
    [TestFixture]
    public class RtpPacketOnvifUtilsTests
    {


        [Test]
        public void ProcessSimpleRTPTimestampExtensionTest()
        {
            byte[] extension = [
                0xAB, 0xAC, 0x00, 0x03,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                ];

            var extensionSpan = new ReadOnlySpan<byte>(extension);
            var timestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(extensionSpan, out int headerPosition);
            Assert.That(timestamp, Is.EqualTo(new DateTime(1900, 01, 01)));
            extensionSpan = extensionSpan[headerPosition..];
            Assert.That(extensionSpan.Length, Is.EqualTo(0));
        }


        [Test]
        public void ProcessSimpleRTPTimestampExtensionTestOtherExtension()
        {
            byte[] extension = [
                0xAA, 0xAA, 0x00, 0x00
                ];

            var timestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(extension, out int headerPosition);
            Assert.Multiple(() =>
            {
                Assert.That(timestamp, Is.EqualTo(DateTime.MinValue));
                Assert.That(headerPosition, Is.EqualTo(0));
            });
        }
    }
}
