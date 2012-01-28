using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rtsp.Sdp;
using NUnit.Framework;
using System.IO;

namespace Rtsp.Sdp.Tests
{
    [TestFixture]
    public class SdpFileTest
    {
        [Test]
        public void Read()
        {
            string sdpFile = string.Empty;
            sdpFile += "v=0\r\n";
            sdpFile += "o=bob 2808844564 2808844564 IN IP4 host.biloxi.example.com\r\n";
            sdpFile += "s=\r\n";
            sdpFile += "c=IN IP4 host.biloxi.example.com\r\n";
            sdpFile += "t=0 0\r\n";
            sdpFile += "m=audio 49172 RTP/AVP 0 8\r\n";
            sdpFile += "a=rtpmap:0 PCMU/8000\r\n";
            sdpFile += "a=rtpmap:8 PCMA/8000\r\n";
            sdpFile += "m=video 0 RTP/AVP 31\r\n";
            sdpFile += "a=rtpmap:31 H261/90000\r\n";
            using (TextReader testReader = new StringReader(sdpFile))
            {
                SdpFile readenSDP = SdpFile.Read(testReader);

                // Check the reader have read everything
                Assert.AreEqual(string.Empty, testReader.ReadToEnd());
            }

        }
    }
}
