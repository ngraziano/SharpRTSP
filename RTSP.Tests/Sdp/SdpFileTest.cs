using NUnit.Framework;
using System.Diagnostics;
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
        public void Read1Strict()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test1.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.ReadStrict(testReader);

            // Check the reader have read everything
            Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Read1Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test1.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.ReadLoose(testReader);

            // Check the reader have read everything
            Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Read2Strict()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test2.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.ReadStrict(testReader);

            Assert.That(readenSDP.Version, Is.Zero);
            Assert.That(readenSDP.Origin, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(readenSDP.Origin.Username, Is.EqualTo("Teleste"));
                Assert.That(readenSDP.Origin.SessionId, Is.EqualTo("749719680"));
                Assert.That(readenSDP.Origin.SessionVersion, Is.EqualTo("2684264576"));
                Assert.That(readenSDP.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(readenSDP.Origin.AddressType, Is.EqualTo("IP4"));
                Assert.That(readenSDP.Origin.UnicastAddress, Is.EqualTo("172.16.200.193"));
                Assert.That(readenSDP.Session, Is.EqualTo("COD_9003-P2-0"));
                Assert.That(readenSDP.SessionInformation, Is.EqualTo("Teleste MPH H.264 Encoder - HK01121135"));
                Assert.That(readenSDP.Connection, Is.Not.Null);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(readenSDP.Connection.NumberOfAddress, Is.EqualTo(1), "Number of address");
                Assert.That(readenSDP.Connection, Is.InstanceOf<ConnectionIP4>());
                Assert.That((readenSDP.Connection as ConnectionIP4)?.Ttl, Is.EqualTo(16));
                Assert.That(readenSDP.Timings, Has.Count.EqualTo(1));
                //Assert.Fail("Timing not well implemented...");
                Assert.That(readenSDP.Medias, Has.Count.EqualTo(1));
            }
            Media media = readenSDP.Medias[0];
            Assert.That(media.Attributs, Has.Count.EqualTo(3));

            var rtpmaps = media.Attributs.Where(x => x.Key == AttributRtpMap.NAME).ToList();
            Assert.That(rtpmaps, Has.Count.EqualTo(1));
            Assert.That(rtpmaps[0].Value, Is.EqualTo("98 H264/90000"));
            Assert.That(rtpmaps[0], Is.InstanceOf<AttributRtpMap>());
            Assert.That((rtpmaps[0] as AttributRtpMap)?.PayloadNumber, Is.EqualTo(98));

            var fmtps = media.Attributs.Where(x => x.Key == AttributFmtp.NAME).ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(rtpmaps, Has.Count.EqualTo(1));
                Assert.That(fmtps[0].Value, Is.EqualTo("98 profile-level-id=42A01E; sprop-parameter-sets=Z01AH/QFgJP6,aP48gA==; packetization-mode=1;"));
                Assert.That(fmtps[0], Is.InstanceOf<AttributFmtp>());
                Assert.That((fmtps[0] as AttributFmtp)?.PayloadNumber, Is.EqualTo(98));
                Assert.That((fmtps[0] as AttributFmtp)?.FormatParameter, Is.EqualTo("profile-level-id=42A01E; sprop-parameter-sets=Z01AH/QFgJP6,aP48gA==; packetization-mode=1;"));

                // Check the reader have read everything
                Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void Read2Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test2.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.ReadLoose(testReader);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(readenSDP.Version, Is.Zero);
                Assert.That(readenSDP.Origin, Is.Not.Null);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(readenSDP.Origin.Username, Is.EqualTo("Teleste"));
                Assert.That(readenSDP.Origin.SessionId, Is.EqualTo("749719680"));
                Assert.That(readenSDP.Origin.SessionVersion, Is.EqualTo("2684264576"));
                Assert.That(readenSDP.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(readenSDP.Origin.AddressType, Is.EqualTo("IP4"));
                Assert.That(readenSDP.Origin.UnicastAddress, Is.EqualTo("172.16.200.193"));
                Assert.That(readenSDP.Session, Is.EqualTo("COD_9003-P2-0"));
                Assert.That(readenSDP.SessionInformation, Is.EqualTo("Teleste MPH H.264 Encoder - HK01121135"));
                Assert.That(readenSDP.Connection?.NumberOfAddress, Is.EqualTo(1), "Number of address");
                Assert.That(readenSDP.Connection, Is.InstanceOf<ConnectionIP4>());
                Assert.That((readenSDP.Connection as ConnectionIP4)?.Ttl, Is.EqualTo(16));
                Assert.That(readenSDP.Timings, Has.Count.EqualTo(1));
                //Assert.Fail("Timing not well implemented...");
                Assert.That(readenSDP.Medias, Has.Count.EqualTo(1));
            }
            Media media = readenSDP.Medias[0];
            Assert.That(media.Attributs, Has.Count.EqualTo(3));

            var rtpmaps = media.Attributs.Where(x => x.Key == AttributRtpMap.NAME).ToList();
            Assert.That(rtpmaps, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(rtpmaps[0].Value, Is.EqualTo("98 H264/90000"));
                Assert.That(rtpmaps[0], Is.InstanceOf<AttributRtpMap>());
                Assert.That((rtpmaps[0] as AttributRtpMap)?.PayloadNumber, Is.EqualTo(98));
            }

            var fmtps = media.Attributs.Where(x => x.Key == AttributFmtp.NAME).ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(rtpmaps, Has.Count.EqualTo(1));
                Assert.That(fmtps[0].Value, Is.EqualTo("98 profile-level-id=42A01E; sprop-parameter-sets=Z01AH/QFgJP6,aP48gA==; packetization-mode=1;"));
                Assert.That(fmtps[0], Is.InstanceOf<AttributFmtp>());
                Assert.That((fmtps[0] as AttributFmtp)?.PayloadNumber, Is.EqualTo(98));
                Assert.That((fmtps[0] as AttributFmtp)?.FormatParameter, Is.EqualTo("profile-level-id=42A01E; sprop-parameter-sets=Z01AH/QFgJP6,aP48gA==; packetization-mode=1;"));

                // Check the reader have read everything
                Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void Read3Strict()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test3.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.ReadStrict(testReader);

            // Check the reader have read everything
            Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Read3Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test3.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.ReadLoose(testReader);

            // Check the reader have read everything
            Assert.That(testReader.ReadToEnd(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Read4Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test4.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile readenSDP = SdpFile.ReadLoose(testReader);

            Assert.That(readenSDP.Origin, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(readenSDP.Version, Is.Zero);
                Assert.That(readenSDP.Origin.Username, Is.EqualTo("-"));
                Assert.That(readenSDP.Origin.SessionId, Is.EqualTo("1707291593123122"));
                Assert.That(readenSDP.Origin.SessionVersion, Is.EqualTo("1"));
                Assert.That(readenSDP.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(readenSDP.Origin.AddressType, Is.EqualTo("IP4"));
                Assert.That(readenSDP.Origin.UnicastAddress, Is.EqualTo("192.168.3.80"));
                Assert.That(readenSDP.Session, Is.EqualTo("profile1"));
                Assert.That(readenSDP.Url, Is.Null);
                Assert.That(readenSDP.Email, Is.EqualTo("admin@"));
                Assert.That(readenSDP.Attributs, Has.Count.EqualTo(2));
                Assert.That(readenSDP.Medias, Has.Count.EqualTo(2));
            }

            Media firstMedia = readenSDP.Medias[0];
            Assert.That(firstMedia.Bandwidths, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstMedia.Bandwidths[0].Type, Is.EqualTo("AS"));
                Assert.That(firstMedia.Bandwidths[0].Value, Is.EqualTo(5000));
            }
        }

        [Test]
        public void Read4Strict()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test4.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            Assert.That(
                () => SdpFile.ReadStrict(testReader),
                Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void Read5Strict()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test5.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            Assert.That(
                () => SdpFile.ReadStrict(testReader),
                Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void Read5Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test5.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile sdp = SdpFile.ReadLoose(testReader);

            Assert.That(sdp.Origin, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sdp.Version, Is.Zero);
                Assert.That(sdp.Session, Is.EqualTo("Session99"));
                Assert.That(sdp.Origin.Username, Is.EqualTo("-"));
                Assert.That(sdp.Origin.SessionId, Is.EqualTo("98969043"));
                Assert.That(sdp.Origin.SessionVersion, Is.EqualTo("98969053"));
                Assert.That(sdp.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(sdp.Origin.AddressType, Is.EqualTo("IP4"));
                Assert.That(sdp.Origin.UnicastAddress, Is.EqualTo("172.30.139.205"));
                Assert.That(sdp.Attributs, Has.Count.EqualTo(0));
                Assert.That(sdp.Medias, Has.Count.EqualTo(1));
                Assert.That(sdp.Medias[0].Attributs, Has.Count.EqualTo(5));
                Assert.That(sdp.Medias[0].Connections, Has.Count.EqualTo(1));
                Assert.That(sdp.Medias[0].Connections[0].Host, Is.EqualTo("0.0.0.0"));
            }
        }

        [Test]
        public void Read6Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test6.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile sdp = SdpFile.ReadLoose(testReader);

            Assert.That(sdp.Origin, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sdp.Version, Is.Zero);
                Assert.That(sdp.Session, Is.EqualTo("HIK Media Server V3.0.2"));
                Assert.That(sdp.Origin.Username, Is.EqualTo("-"));
                Assert.That(sdp.Origin.SessionId, Is.EqualTo("1109162014219182"));
                Assert.That(sdp.Origin.SessionVersion, Is.EqualTo("0"));
                Assert.That(sdp.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(sdp.Origin.AddressType, Is.EqualTo("IP4"));
                Assert.That(sdp.Origin.UnicastAddress, Is.EqualTo("0.0.0.0"));
                Assert.That(sdp.Connection?.Host, Is.EqualTo("0.0.0.0"));
                Assert.That(sdp.Attributs, Has.Count.EqualTo(2));
                Assert.That(sdp.Medias, Has.Count.EqualTo(1));
                Assert.That(sdp.Medias[0].Attributs, Has.Count.EqualTo(5));
            }
        }

        [Test]
        public void Read7Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test7.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile sdp = SdpFile.ReadLoose(testReader);

            Assert.That(sdp.Origin, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sdp.Version, Is.Zero);
                Assert.That(sdp.Session, Is.EqualTo("Session99"));
                Assert.That(sdp.Origin.Username, Is.EqualTo("-"));
                Assert.That(sdp.Origin.SessionId, Is.EqualTo("98969043"));
                Assert.That(sdp.Origin.SessionVersion, Is.EqualTo("98969053"));
                Assert.That(sdp.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(sdp.Origin.AddressType, Is.EqualTo("IP6"));
                Assert.That(sdp.Origin.UnicastAddress, Is.EqualTo("2201:056D::112E:144A:1E24"));
                Assert.That(sdp.Connection?.Host, Is.EqualTo("FF1E:03AD::7F2E:172A:1E24"));
                Assert.That(sdp.Attributs, Has.Count.EqualTo(0));
                Assert.That(sdp.Medias, Has.Count.EqualTo(1));
                Assert.That(sdp.Medias[0].Attributs, Has.Count.EqualTo(5));
            }
        }

        [Test]
        public void Read8Strict()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test8.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            Assert.That(
                () => SdpFile.ReadStrict(testReader),
                Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void Read8Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test8.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile sdp = SdpFile.ReadLoose(testReader);

            Assert.That(sdp.Origin, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sdp.Version, Is.Zero);
                Assert.That(sdp.Session, Is.EqualTo("Session99"));
                Assert.That(sdp.Origin.Username, Is.EqualTo("-"));
                Assert.That(sdp.Origin.SessionId, Is.EqualTo("98969043"));
                Assert.That(sdp.Origin.SessionVersion, Is.EqualTo("98969053"));
                Assert.That(sdp.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(sdp.Origin.AddressType, Is.EqualTo("IP6"));
                Assert.That(sdp.Origin.UnicastAddress, Is.EqualTo("2201:056D::112E:144A:1E24"));
                Assert.That(sdp.Connection?.Host, Is.EqualTo("FF1E:03AD::7F2E:172A:1E24"));
                Assert.That(sdp.Attributs, Has.Count.EqualTo(0));
                Assert.That(sdp.Medias, Has.Count.EqualTo(1));
                Assert.That(sdp.Medias[0].Attributs, Has.Count.EqualTo(5));
            }
        }

        [Test]
        public void Read9Strict()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test9.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            Assert.That(
                () => SdpFile.ReadStrict(testReader),
                Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void Read9Loose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.test9.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile sdp = SdpFile.ReadLoose(testReader);

            Assert.That(sdp.Origin, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sdp.Version, Is.Zero);
                Assert.That(sdp.Session, Is.EqualTo("Session99"));
                Assert.That(sdp.Origin.Username, Is.EqualTo("-"));
                Assert.That(sdp.Origin.SessionId, Is.EqualTo("98969043"));
                Assert.That(sdp.Origin.SessionVersion, Is.EqualTo("98969053"));
                Assert.That(sdp.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(sdp.Origin.AddressType, Is.EqualTo("IP4"));
                Assert.That(sdp.Origin.UnicastAddress, Is.EqualTo("0.0.0.0"));
                Assert.That(sdp.Connection?.Host, Is.EqualTo("FF1E:03AD::7F2E:172A:1E24"));
                Assert.That(sdp.Attributs, Has.Count.EqualTo(0));
                Assert.That(sdp.Medias, Has.Count.EqualTo(1));
                Assert.That(sdp.Medias[0].Attributs, Has.Count.EqualTo(5));
            }
        }

        [Test]
        public void ReadAStrict()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.testA.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            Assert.That(
                () => SdpFile.ReadStrict(testReader),
                Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void ReadALoose()
        {
            using var sdpFile = selfAssembly.GetManifestResourceStream("RTSP.Tests.Sdp.Data.testA.sdp");
            Debug.Assert(sdpFile != null, "Missing test file");
            using var testReader = new StreamReader(sdpFile);
            SdpFile sdp = SdpFile.ReadLoose(testReader);

            Assert.That(sdp.Origin, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sdp.Version, Is.Zero);
                Assert.That(sdp.Session, Is.EqualTo("Big Buck Bunny 60fps 4K - Official Blender Foundation Short Film"));
                Assert.That(sdp.Origin.Username, Is.EqualTo("-"));
                Assert.That(sdp.Origin.SessionId, Is.EqualTo("0"));
                Assert.That(sdp.Origin.SessionVersion, Is.EqualTo("0"));
                Assert.That(sdp.Origin.NetType, Is.EqualTo("IN"));
                Assert.That(sdp.Origin.AddressType, Is.EqualTo("IP4"));
                Assert.That(sdp.Origin.UnicastAddress, Is.EqualTo("127.0.0.1"));
                Assert.That(sdp.Connection, Is.Null);
                Assert.That(sdp.Attributs, Has.Count.EqualTo(1));
                Assert.That(sdp.Medias, Has.Count.EqualTo(2));
                Assert.That(sdp.Medias[0].Attributs, Has.Count.EqualTo(3));
                Assert.That(sdp.Medias[0].Attributs, Has.One.AssignableTo<AttributRtpMap>());
                Assert.That(sdp.Medias[0].Attributs, Has.One.AssignableTo<AttributFmtp>());
                Assert.That(sdp.Medias[1].Attributs, Has.Count.EqualTo(3));
                Assert.That(sdp.Medias[1].Attributs, Has.One.AssignableTo<AttributRtpMap>());
                Assert.That(sdp.Medias[1].Attributs, Has.One.AssignableTo<AttributFmtp>());
            }
        }
    }
}
