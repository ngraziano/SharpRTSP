using NUnit.Framework;

namespace Rtsp.Messages.Tests
{
    [TestFixture]
    public class RtspTransportTest
    {
        /*Transport           =    "Transport" ":"
                            1\#transport-spec
    transport-spec      =    transport-protocol/profile[/lower-transport]
                            *parameter
    transport-protocol  =    "RTP"
    profile             =    "AVP"
    lower-transport     =    "TCP" | "UDP"
    parameter           =    ( "unicast" | "multicast" )
                       |    ";" "destination" [ "=" address ]
                       |    ";" "interleaved" "=" channel [ "-" channel ]
                       |    ";" "append"
                       |    ";" "ttl" "=" ttl
                       |    ";" "layers" "=" 1*DIGIT
                       |    ";" "port" "=" port [ "-" port ]
                       |    ";" "client_port" "=" port [ "-" port ]
                       |    ";" "server_port" "=" port [ "-" port ]
                       |    ";" "ssrc" "=" ssrc
                       |    ";" "mode" = <"> 1\#mode <">
    ttl                 =    1*3(DIGIT)
    port                =    1*5(DIGIT)
    ssrc                =    8*8(HEX)
    channel             =    1*3(DIGIT)
    address             =    host
    mode                =    <"> *Method <"> | Method

    */

        [Test]
        public void DefaultValue()
        {
            RtspTransport testValue = new();
            Assert.That(testValue.IsMulticast, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(testValue.LowerTransport, Is.EqualTo(RtspTransport.LowerTransportType.UDP));
                Assert.That(testValue.Mode, Is.EqualTo("PLAY"));
            });
        }

        [Test]
        public void ParseMinimal()
        {
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP");
            Assert.That(testValue.IsMulticast, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(testValue.LowerTransport, Is.EqualTo(RtspTransport.LowerTransportType.UDP));
                Assert.That(testValue.Mode, Is.EqualTo("PLAY"));
            });
        }

        [Test]
        public void Parse1()
        {
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP/UDP;destination");
            Assert.That(testValue.IsMulticast, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(testValue.LowerTransport, Is.EqualTo(RtspTransport.LowerTransportType.UDP));
                Assert.That(testValue.Mode, Is.EqualTo("PLAY"));
            });
        }

        [Test]
        public void Parse2()
        {
            // not realistic
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP/TCP;multicast;destination=test.example.com;ttl=234;ssrc=cd3b20a5;mode=RECORD");
            Assert.That(testValue.IsMulticast, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(testValue.LowerTransport, Is.EqualTo(RtspTransport.LowerTransportType.TCP));
                Assert.That(testValue.Destination, Is.EqualTo("test.example.com"));
                Assert.That(testValue.SSrc, Is.EqualTo("cd3b20a5"));
                Assert.That(testValue.Mode, Is.EqualTo("RECORD"));
            });
        }

        [Test]
        public void Parse3()
        {
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP/TCP;interleaved=3-4");
            Assert.That(testValue.IsMulticast, Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(testValue.LowerTransport, Is.EqualTo(RtspTransport.LowerTransportType.TCP));
                Assert.That(testValue.Interleaved.First, Is.EqualTo(3));
            });
            Assert.That(testValue.Interleaved.IsSecondPortPresent, Is.True);
            Assert.That(testValue.Interleaved.Second, Is.EqualTo(4));
        }

        [Test]
        public void Parse4()
        {
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP;unicast;destination=1.2.3.4;source=3.4.5.6;server_port=5000-5001;client_port=5003-5004");
            Assert.That(testValue.IsMulticast, Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(testValue.LowerTransport, Is.EqualTo(RtspTransport.LowerTransportType.UDP));
                Assert.That(testValue.Destination, Is.EqualTo("1.2.3.4"));
                Assert.That(testValue.Source, Is.EqualTo("3.4.5.6"));
                Assert.That(testValue.ServerPort.First, Is.EqualTo(5000));
                Assert.That(testValue.ServerPort.Second, Is.EqualTo(5001));
                Assert.That(testValue.ClientPort.First, Is.EqualTo(5003));
                Assert.That(testValue.ClientPort.Second, Is.EqualTo(5004));
            });
        }

        [Test]
        public void ToStringTCP()
        {
            RtspTransport transport = new()
            {
                LowerTransport = RtspTransport.LowerTransportType.TCP,
                Interleaved = new PortCouple(0, 1),
            };
            Assert.That(transport.ToString(), Is.EqualTo("RTP/AVP/TCP;unicast;interleaved=0-1"));
        }

        [Test]
        public void ToStringUDPUnicast()
        {
            RtspTransport transport = new()
            {
                LowerTransport = RtspTransport.LowerTransportType.UDP,
                IsMulticast = false,
                ClientPort = new PortCouple(5000, 5001),
                ServerPort = new PortCouple(5002, 5003),
                Destination = "1.2.3.4"
            };
            Assert.That(transport.ToString(), Is.EqualTo("RTP/AVP/UDP;unicast;destination=1.2.3.4;client_port=5000-5001;server_port=5002-5003"));
        }
    }
}
