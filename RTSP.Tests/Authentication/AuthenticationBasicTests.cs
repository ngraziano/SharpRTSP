using NUnit.Framework;
using Rtsp;
using Rtsp.Messages;
using System.Net;

namespace RTSP.Tests.Authentication
{
    public class AuthenticationBasicTests
    {
        [Test]
        public void GetResponseTest()
        {
            const string wanted = "Basic dXNlcm5hbWVAZXhhbXBsZS5jb206UGFzc3dvcmRAIVhZWg==";
            var testObject = new AuthenticationBasic(new NetworkCredential("username@example.com", "Password@!XYZ"));

            var header = testObject.GetResponse(0, "rtsp://test/uri", "GET_PARAMETER", null);

            Assert.That(header, Is.EqualTo(wanted));
        }

        [Test]
        public void IsValidTest()
        {
            var message = new RtspMessage();
            message.Headers.Add("Authorization", "Basic dXNlcm5hbWVAZXhhbXBsZS5jb206UGFzc3dvcmRAIVhZWg==");
            var testObject = new AuthenticationBasic(new NetworkCredential("username@example.com", "Password@!XYZ"));

            var result = testObject.IsValid(message);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsValidCaseUserNameTest()
        {
            var message = new RtspMessage();
            message.Headers.Add("Authorization", "Basic dXNlcm5hbWVAZXhhbXBsZS5jb206UGFzc3dvcmRAIVhZWg==");
            var testObject = new AuthenticationBasic(new NetworkCredential("USERNAME@example.com", "Password@!XYZ"));

            var result = testObject.IsValid(message);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsValidWrongCasePasswordTest()
        {
            var message = new RtspMessage();
            message.Headers.Add("Authorization", "Basic dXNlcm5hbWVAZXhhbXBsZS5jb206UGFzc3dvcmRAIVhZWg==");
            var testObject = new AuthenticationBasic(new NetworkCredential("username@example.com", "password@!XYZ"));

            var result = testObject.IsValid(message);

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsValidMisingPasswordTest()
        {
            var message = new RtspMessage();
            message.Headers.Add("Authorization", "Basic dXNlcm5hbWVAZXhhbXBsZS5jb20=");
            var testObject = new AuthenticationBasic(new NetworkCredential("username@example.com", "password@!XYZ"));

            var result = testObject.IsValid(message);

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsValidInvalidBase64Test()
        {
            var message = new RtspMessage();
            message.Headers.Add("Authorization", "Basic invalid$$$$");
            var testObject = new AuthenticationBasic(new NetworkCredential("username@example.com", "password@!XYZ"));

            var result = testObject.IsValid(message);

            Assert.That(result, Is.False);
        }
    }
}
