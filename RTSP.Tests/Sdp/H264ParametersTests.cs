using NUnit.Framework;
using Rtsp.Sdp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Sdp.Tests
{
    [TestFixture()]
    public class H264ParametersTests
    {
        [Test()]
        public void Parse()
        {
            var parsed = H264Parameters.Parse("profile-level-id=42A01E; sprop-parameter-sets=Z01AH/QFgJP6,aP48gA==; packetization-mode=1;");


            Assert.AreEqual(3, parsed.Count);
            Assert.AreEqual("42A01E", parsed["profile-level-id"]);
            Assert.AreEqual("1", parsed["packetization-mode"]);
            var sprop = parsed.SpropParameterSets;
            Assert.AreEqual(2, sprop.Count);

            byte[] result1 = { 0x67, 0x4D, 0x40, 0x1F, 0xF4, 0x05, 0x80, 0x93, 0xFA };
            Assert.AreEqual(result1, sprop[0]);
            byte[] result2 = { 0x68, 0xFE, 0x3C, 0x80 };
            Assert.AreEqual(result2, sprop[1]);


        }
        
    }
}