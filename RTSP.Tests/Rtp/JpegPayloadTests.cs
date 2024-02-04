using NUnit.Framework;
using Rtsp.Rtp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RTSP.Tests.Rtp
{
    public class JpegPayloadTests
    {
        private readonly Assembly selfAssembly = Assembly.GetExecutingAssembly();


        private RtpPacket ReadPacket(string resourceName)
        {
            using var rtpFile = selfAssembly.GetManifestResourceStream($"RTSP.Tests.Rtp.Data.{resourceName}.rtp");
            using var testReader = new StreamReader(rtpFile);
            byte[] buffer = new byte[16 * 1024];
            using var ms = new MemoryStream();
            int read;
            while ((read = rtpFile.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }
            return new RtpPacket(ms.ToArray());
        }

        private byte[] ReadBytes(string resourceName)
        {
            using var rtpFile = selfAssembly.GetManifestResourceStream($"RTSP.Tests.Rtp.Data.{resourceName}");
            using var testReader = new StreamReader(rtpFile);
            byte[] buffer = new byte[16 * 1024];
            using var ms = new MemoryStream();
            int read;
            while ((read = rtpFile.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }

        [Test]
        public void Read1()
        {
            JPEGPayload jpegPayloadParser = new();

            var r0 = jpegPayloadParser.ProcessRTPPacket(ReadPacket("jpeg_0"));
            var r1 = jpegPayloadParser.ProcessRTPPacket(ReadPacket("jpeg_1"));
            var r2 = jpegPayloadParser.ProcessRTPPacket(ReadPacket("jpeg_2"));

            Assert.Multiple(() =>
            {
                Assert.That(r0, Is.Empty);
                Assert.That(r1, Is.Empty);
                Assert.That(r2, Has.Count.EqualTo(1));
            });

            var jpeg = r2[0];
            var expected = ReadBytes("img_jpg_0.jpg");

            Assert.That(jpeg.ToArray(), Is.EqualTo(expected));
        }
    }
}
