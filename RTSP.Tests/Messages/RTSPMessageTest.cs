using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RTSP.Messages;

namespace RTSP.Messages.Tests
{
    [TestFixture]
    public class RTSPMessageTest
    {
        //Put a name on test to permit VSNunit to handle them.
        [Test]
        [TestCase("OPTIONS * RTSP/1.0", RTSPRequest.RequestType.OPTIONS, TestName = "GetRTSPMessageRequest-OPTIONS")]
        [TestCase("SETUP rtsp://audio.example.com/twister/audio.en RTSP/1.0", RTSPRequest.RequestType.SETUP, TestName = "GetRTSPMessageRequest-SETUP")]
        [TestCase("PLAY rtsp://audio.example.com/twister/audio.en RTSP/1.0", RTSPRequest.RequestType.PLAY, TestName = "GetRTSPMessageRequest-PLAY")]
        public void GetRTSPMessageRequest(string requestLine,RTSPRequest.RequestType requestType)
        {
            RTSPMessage oneMessage = RTSPMessage.GetRTSPMessage(requestLine);
            Assert.IsInstanceOf<RTSPRequest>(oneMessage);
            
            RTSPRequest oneRequest = oneMessage as RTSPRequest;
            Assert.AreEqual(requestType, oneRequest.RequestTyped);
        }

    }
}
