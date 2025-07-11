using System;
using System.Collections.Generic;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Builders;

namespace Rtsp.Messages.Tests
{
    [TestFixture]
    public class RtspMessageTest
    {
        //Put a name on test to permit VSNunit to handle them.
        [Test]
        [TestCase("OPTIONS * RTSP/1.0", RtspRequest.RequestType.OPTIONS, TestName = "GetRtspMessageRequest-OPTIONS")]
        [TestCase("SETUP rtsp://audio.example.com/twister/audio.en RTSP/1.0", RtspRequest.RequestType.SETUP,
            TestName = "GetRtspMessageRequest-SETUP")]
        [TestCase("PLAY rtsp://audio.example.com/twister/audio.en RTSP/1.0", RtspRequest.RequestType.PLAY,
            TestName = "GetRtspMessageRequest-PLAY")]
        public void GetRtspMessageRequest(string requestLine, RtspRequest.RequestType requestType)
        {
            RtspMessage oneMessage = RtspMessage.GetRtspMessage(requestLine);
            Assert.That(oneMessage, Is.InstanceOf<RtspRequest>());

            var oneRequest = oneMessage as RtspRequest;
            Assert.That(oneRequest, Is.Not.Null);
            Assert.That(oneRequest.RequestTyped, Is.EqualTo(requestType));
        }

        //Put a name on test to permit VSNunit to handle them.
        [Test]
        [TestCase("RTSP/1.0 551 Option not supported", 551, "Option not supported",
            TestName = "GetRtspMessageResponse-551")]
        public void GetRtspMessageResponse(string requestLine, int returnCode, string returnMessage)
        {
            RtspMessage oneMessage = RtspMessage.GetRtspMessage(requestLine);
            Assert.That(oneMessage, Is.InstanceOf<RtspResponse>());

            var oneResponse = oneMessage as RtspResponse;
            Assert.That(oneResponse, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(oneResponse.ReturnCode, Is.EqualTo(returnCode));
                Assert.That(oneResponse.ReturnMessage, Is.EqualTo(returnMessage));
            }
        }

        [Test]
        public void SeqWrite()
        {
            RtspMessage oneMessage = new()
            {
                CSeq = 123
            };

            Assert.That(oneMessage.Headers["CSeq"], Is.EqualTo("123"));
        }
#if NET8_0_OR_GREATER
        [Test]
        [GenericTestCase<RtspRequestOptions>(RtspRequest.RequestType.OPTIONS)]
        [GenericTestCase<RtspRequestDescribe>(RtspRequest.RequestType.DESCRIBE)]
        [GenericTestCase<RtspRequestSetup>(RtspRequest.RequestType.SETUP)]
        [GenericTestCase<RtspRequestPlay>(RtspRequest.RequestType.PLAY)]
        [GenericTestCase<RtspRequestPause>(RtspRequest.RequestType.PAUSE)]
        [GenericTestCase<RtspRequestTeardown>(RtspRequest.RequestType.TEARDOWN)]
        [GenericTestCase<RtspRequestGetParameter>(RtspRequest.RequestType.GET_PARAMETER)]
        [GenericTestCase<RtspRequestSetParameter>(RtspRequest.RequestType.SET_PARAMETER)]
        [GenericTestCase<RtspRequestAnnounce>(RtspRequest.RequestType.ANNOUNCE)]
        [GenericTestCase<RtspRequestRecord>(RtspRequest.RequestType.RECORD)]
        [GenericTestCase<RtspRequestRedirect>(RtspRequest.RequestType.REDIRECT)]
        public void CheckRequestType<T>(RtspRequest.RequestType expectedType) where T : RtspRequest, new()
        {
            RtspRequest onMessage = new T();
            Assert.That(onMessage.RequestTyped, Is.EqualTo(expectedType));
        }

        [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
        private class GenericTestCaseAttribute<T>(params object[] arguments) : TestCaseAttribute(arguments), ITestBuilder
        {
            IEnumerable<TestMethod> ITestBuilder.BuildFrom(IMethodInfo method, Test? suite)
            {
                var testedMethod = method.IsGenericMethodDefinition ? method.MakeGenericMethod(typeof(T)) : method;
                return BuildFrom(testedMethod, suite);
            }
        }
#endif
    }
}