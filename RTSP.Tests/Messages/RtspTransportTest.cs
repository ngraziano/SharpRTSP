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
            RtspTransport testValue = new RtspTransport();
            Assert.IsTrue(testValue.IsMulticast);
            Assert.AreEqual(RtspTransport.LowerTransportType.UDP, testValue.LowerTransport);
            Assert.AreEqual("PLAY", testValue.Mode);
        }

        [Test]
        public void ParseMinimal()
        {
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP");
            Assert.IsTrue(testValue.IsMulticast);
            Assert.AreEqual(RtspTransport.LowerTransportType.UDP, testValue.LowerTransport);
            Assert.AreEqual("PLAY", testValue.Mode);
        }

        [Test]
        public void Parse1()
        {
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP/UDP;destination");
            Assert.IsTrue(testValue.IsMulticast);
            Assert.AreEqual(RtspTransport.LowerTransportType.UDP, testValue.LowerTransport);
            Assert.AreEqual("PLAY", testValue.Mode);
        }

        [Test]
        public void Parse2()
        {
            // not realistic
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP/TCP;multicast;destination=test.example.com;ttl=234;ssrc=cd3b20a5;mode=RECORD");
            Assert.IsTrue(testValue.IsMulticast);
            Assert.AreEqual(RtspTransport.LowerTransportType.TCP, testValue.LowerTransport);
            Assert.AreEqual("test.example.com", testValue.Destination);
            Assert.AreEqual("cd3b20a5", testValue.SSrc);
            Assert.AreEqual("RECORD", testValue.Mode);
        }

        [Test]
        public void Parse3()
        {
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP/TCP;interleaved=3-4");
            Assert.IsFalse(testValue.IsMulticast);
            Assert.AreEqual(RtspTransport.LowerTransportType.TCP, testValue.LowerTransport);
            Assert.AreEqual(3, testValue.Interleaved.First);
            Assert.IsTrue(testValue.Interleaved.IsSecondPortPresent);
            Assert.AreEqual(4, testValue.Interleaved.Second);
        }


        [Test]
        public void Parse4()
        {
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP;unicast;destination=1.2.3.4;source=3.4.5.6;server_port=5000-5001;client_port=5003-5004");
            Assert.IsFalse(testValue.IsMulticast);
            Assert.AreEqual(RtspTransport.LowerTransportType.UDP, testValue.LowerTransport);
            Assert.AreEqual("1.2.3.4", testValue.Destination);
            Assert.AreEqual("3.4.5.6", testValue.Source);
            Assert.AreEqual(5000, testValue.ServerPort.First);
            Assert.AreEqual(5001, testValue.ServerPort.Second);
            Assert.AreEqual(5003, testValue.ClientPort.First);
            Assert.AreEqual(5004, testValue.ClientPort.Second);


        }



        [Test]
        public void ToStringTCP()
        {
            RtspTransport transport = new RtspTransport()
            {
                LowerTransport = RtspTransport.LowerTransportType.TCP,
                Interleaved = new PortCouple(0, 1),

            };
            Assert.AreEqual("RTP/AVP/TCP;unicast;interleaved=0-1", transport.ToString());
        }


        [Test]
        public void ToStringUDPUnicast()
        {
            RtspTransport transport = new RtspTransport()
            {
                LowerTransport = RtspTransport.LowerTransportType.UDP,
                IsMulticast = false,
                ClientPort = new PortCouple(5000, 5001),
                ServerPort = new PortCouple(5002, 5003),
                Destination = "1.2.3.4"

            };
            Assert.AreEqual("RTP/AVP/UDP;unicast;destination=1.2.3.4;client_port=5000-5001;server_port=5002-5003", transport.ToString());
        }

    }
}
