using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Rtsp.Sdp.Tests
{
    [TestFixture]
    public class SdpFileTest
    {
        private readonly Assembly selfAssembly = Assembly.GetExecutingAssembly();

        [Test]
        public void Read1()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test1.sdp");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.Read(testReader);

            // Check the reader have read everything
            Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Read2()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test2.sdp");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.Read(testReader);

            Assert.Multiple(() =>
            {
                Assert.That(readenSDP.Version, Is.EqualTo(0));
                Assert.That(readenSDP.Origin.Username, Is.EqualTo("Teleste"));
                Assert.That(readenSDP.Origin.SessionId, Is.EqualTo("749719680"));
                Assert.That(readenSDP.Origin.SessionVersion, Is.EqualTo("2684264576"));
                Assert.That(readenSDP.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(readenSDP.Origin.AddressType, Is.EqualTo("IP4"));
                Assert.That(readenSDP.Origin.UnicastAddress, Is.EqualTo("172.16.200.193"));
                Assert.That(readenSDP.Session, Is.EqualTo("COD_9003-P2-0"));
                Assert.That(readenSDP.SessionInformation, Is.EqualTo("Teleste MPH H.264 Encoder - HK01121135"));
                Assert.That(readenSDP.Connection.NumberOfAddress, Is.EqualTo(1), "Number of address");
                Assert.That(readenSDP.Connection, Is.InstanceOf<ConnectionIP4>());
                Assert.That((readenSDP.Connection as ConnectionIP4).Ttl, Is.EqualTo(16));
                Assert.That(readenSDP.Timings, Has.Count.EqualTo(1));
                //Assert.Fail("Timing not well implemented...");
                Assert.That(readenSDP.Medias, Has.Count.EqualTo(1));
            });
            Media media = readenSDP.Medias[0];
            Assert.That(media.Attributs, Has.Count.EqualTo(3));

            var rtpmaps = media.Attributs.Where(x => x.Key == AttributRtpMap.NAME).ToList();
            Assert.That(rtpmaps, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(rtpmaps[0].Value, Is.EqualTo("98 H264/90000"));
                Assert.That(rtpmaps[0], Is.InstanceOf<AttributRtpMap>());
                Assert.That((rtpmaps[0] as AttributRtpMap).PayloadNumber, Is.EqualTo(98));
            });

            var fmtps = media.Attributs.Where(x => x.Key == AttributFmtp.NAME).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(rtpmaps, Has.Count.EqualTo(1));
                Assert.That(fmtps[0].Value, Is.EqualTo("98 profile-level-id=42A01E; sprop-parameter-sets=Z01AH/QFgJP6,aP48gA==; packetization-mode=1;"));
                Assert.That(fmtps[0], Is.InstanceOf<AttributFmtp>());
                Assert.That((fmtps[0] as AttributFmtp).PayloadNumber, Is.EqualTo(98));
                Assert.That((fmtps[0] as AttributFmtp).FormatParameter, Is.EqualTo("profile-level-id=42A01E; sprop-parameter-sets=Z01AH/QFgJP6,aP48gA==; packetization-mode=1;"));

                // Check the reader have read everything
                Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
            });
        }
        [Test]
        public void Read3()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test3.sdp");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.Read(testReader);

            // Check the reader have read everything
            Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
        }
    }
}
