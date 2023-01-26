using NUnit.Framework;
using Rtsp.Rtp;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RTSP.Tests.Rtp
{
    public class JpegPayloadTests
    {
        private readonly Assembly selfAssembly = Assembly.GetExecutingAssembly();

        private RtpPacket ReadPacket(string resourceName)
        {
            using var rtpFile = selfAssembly.GetManifestResourceStream($"RTSP.Tests.Rtp.Data.{resourceName}.rtp");
            Debug.Assert(rtpFile != null, "Missing test file");
            using var testReader = new StreamReader(rtpFile);
            byte[] buffer = new byte[16 * 1024];
            using var ms = new MemoryStream();
            int read;
            while ((read = rtpFile!.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }
            return new RtpPacket(ms.ToArray());
        }

        private byte[] ReadBytes(string resourceName)
        {
            using var rtpFile = selfAssembly.GetManifestResourceStream($"RTSP.Tests.Rtp.Data.{resourceName}");
            Debug.Assert(rtpFile != null, "Missing test file");
            using var testReader = new StreamReader(rtpFile);
            byte[] buffer = new byte[16 * 1024];
            using var ms = new MemoryStream();
            int read;
            while ((read = rtpFile!.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }

        [Test]
        public void Read1()
        {
            JPEGPayload jpegPayloadParser = new();

            var r0 = jpegPayloadParser.ProcessPacket(ReadPacket("jpeg_0"));
            var r1 = jpegPayloadParser.ProcessPacket(ReadPacket("jpeg_1"));
            var r2 = jpegPayloadParser.ProcessPacket(ReadPacket("jpeg_2"));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(r0.Data.ToArray(), Is.Empty);
                Assert.That(r1.Data.ToArray(), Is.Empty);
                Assert.That(r2.Data.ToArray(), Is.Not.Empty);
            }

            var jpeg = r2.Data.First;
            var expected = ReadBytes("img_jpg_0.jpg");

            Assert.That(jpeg.ToArray(), Is.EqualTo(expected));
        }
    }
}
