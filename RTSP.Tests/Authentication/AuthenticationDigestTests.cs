using NUnit.Framework;
using Rtsp;
using Rtsp.Messages;
using System.Net;
using System.Security;

namespace RTSP.Tests.Authentication
{
    public class AuthenticationDigestTests
    {
        // MD5, no Algorithm specified
        string authStringPLAY_MD5_noalg = "Digest username=\"user\", realm=\"SharpRTSPServer\", nonce=\"556284985\", uri=\"rtsp://192.168.26.76:8554/\", response=\"f3ce799d94cb45e14bf59e57cd41c749\"";
        string authStringPLAY_MD5_with_alg = "Digest username=\"user\", realm=\"SharpRTSPServer\", nonce=\"556284985\", uri=\"rtsp://192.168.26.76:8554/\", response=\"f3ce799d94cb45e14bf59e57cd41c749\", algorithm=\"MD5\"";
        string authStringPLAY_SHA256 = "Digest username=\"user\", realm=\"SharpRTSPServer\", nonce=\"729461183\", uri=\"rtsp://192.168.26.76:8554/\", response=\"9f6d99e827799b23b273aaf62f7eea84a720e25a205284a921f0d9aa14621dcc\", algorithm=\"SHA-256\"";
        string authStringPLAY_BadAlg = "Digest username=\"user\", realm=\"SharpRTSPServer\", nonce=\"729461183\", uri=\"rtsp://192.168.26.76:8554/\", response=\"9f6d99e827799b23b273aaf62f7eea84a720e25a205284a921f0d9aa14621dcc\", algorithm=\"BADVALUE\"";
        string realm = "SharpRTSPServer";
        string qop = "";

        [Test]
        public void IsValid_MD5_No_Alg_Test1()
        {
            var message = new RtspRequestPlay();
            message.Headers.Add("Authorization", authStringPLAY_MD5_noalg);

            string nonce = "556284985";
            var testObject = new AuthenticationDigest(new NetworkCredential("user", "password", realm), realm, nonce, qop, AuthenticationDigest.HashAlgorithm.MD5);
            var result = testObject.IsValid(message);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsValid_MD5_With_Alg_Test()
        {
            var message = new RtspRequestPlay();
            message.Headers.Add("Authorization", authStringPLAY_MD5_with_alg);

            string nonce = "556284985";
            var testObject = new AuthenticationDigest(new NetworkCredential("user", "password", realm), realm, nonce, qop, AuthenticationDigest.HashAlgorithm.MD5);
            var result = testObject.IsValid(message);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsValid_SHA256_Test()
        {
            var message = new RtspRequestPlay();
            message.Headers.Add("Authorization", authStringPLAY_SHA256);

            string nonce = "729461183";
            var testObject = new AuthenticationDigest(new NetworkCredential("user", "password", realm), realm, nonce, qop, AuthenticationDigest.HashAlgorithm.SHA256);
            var result = testObject.IsValid(message);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsValid_BadAlg_Test()
        {
            var message = new RtspRequestPlay();
            message.Headers.Add("Authorization", authStringPLAY_BadAlg);

            string nonce = "729461183";
            var testObject = new AuthenticationDigest(new NetworkCredential("user", "password", realm), realm, nonce, qop, AuthenticationDigest.HashAlgorithm.MD5);
            var result = testObject.IsValid(message);

            Assert.That(result, Is.False);
        }

        [Test]
        public void GetWWWAuthenticate_MD5()
        {
            var message = new RtspRequestPlay();
            message.Headers.Add("Authorization", authStringPLAY_BadAlg);

            string nonce = "11223344";
            var testObject = new AuthenticationDigest(new NetworkCredential("user", "password", realm), realm, nonce, qop, AuthenticationDigest.HashAlgorithm.MD5);
            var result = testObject.GetServerResponse();

            Assert.That(result.Contains("algorithm"), Is.False); // We don't add 'algorithm=MD5' as it is not required
        }

        [Test]
        public void GetWWWAuthenticate_SHA256()
        {
            var message = new RtspRequestPlay();
            message.Headers.Add("Authorization", authStringPLAY_BadAlg);

            string nonce = "11223344";
            var testObject = new AuthenticationDigest(new NetworkCredential("user", "password", realm), realm, nonce, qop, AuthenticationDigest.HashAlgorithm.SHA256);
            var result = testObject.GetServerResponse();

            Assert.That(result.Contains("algorithm") && result.Contains("SHA-256"), Is.True);
        }

    }
}
