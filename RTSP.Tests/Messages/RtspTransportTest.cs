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
            RtspTransport testValue = RtspTransport.Parse("RTP/AVP/TCP;multicast;destination=test.example.com;ttl=234;ssrc=cd3b20a5");
            Assert.IsTrue(testValue.IsMulticast);
            Assert.AreEqual(RtspTransport.LowerTransportType.TCP, testValue.LowerTransport);
            Assert.AreEqual("test.example.com", testValue.Destination);
            Assert.AreEqual("cd3b20a5", testValue.SSrc);
            Assert.AreEqual("PLAY", testValue.Mode);
        }
    }
}
