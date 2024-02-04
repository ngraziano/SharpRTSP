using NUnit.Framework;

namespace Rtsp.Messages.Tests
{
    [TestFixture]
    public class RtspMessageTest
    {
        //Put a name on test to permit VSNunit to handle them.
        [Test]
        [TestCase("OPTIONS * RTSP/1.0", RtspRequest.RequestType.OPTIONS, TestName = "GetRtspMessageRequest-OPTIONS")]
        [TestCase("SETUP rtsp://audio.example.com/twister/audio.en RTSP/1.0", RtspRequest.RequestType.SETUP, TestName = "GetRtspMessageRequest-SETUP")]
        [TestCase("PLAY rtsp://audio.example.com/twister/audio.en RTSP/1.0", RtspRequest.RequestType.PLAY, TestName = "GetRtspMessageRequest-PLAY")]
        public void GetRtspMessageRequest(string requestLine, RtspRequest.RequestType requestType)
        {
            RtspMessage oneMessage = RtspMessage.GetRtspMessage(requestLine);
            Assert.That(oneMessage, Is.InstanceOf<RtspRequest>());

            RtspRequest oneRequest = oneMessage as RtspRequest;
            Assert.That(oneRequest.RequestTyped, Is.EqualTo(requestType));
        }

        //Put a name on test to permit VSNunit to handle them.
        [Test]
        [TestCase("RTSP/1.0 551 Option not supported", 551, "Option not supported", TestName = "GetRtspMessageResponse-551")]
        public void GetRtspMessageResponse(string requestLine, int returnCode, string returnMessage)
        {
            RtspMessage oneMessage = RtspMessage.GetRtspMessage(requestLine);
            Assert.That(oneMessage, Is.InstanceOf<RtspResponse>());

            RtspResponse oneResponse = oneMessage as RtspResponse;
            Assert.Multiple(() =>
            {
                Assert.That(oneResponse.ReturnCode, Is.EqualTo(returnCode));
                Assert.That(oneResponse.ReturnMessage, Is.EqualTo(returnMessage));
            });
        }
    }
}
