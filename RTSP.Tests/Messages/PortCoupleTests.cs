using NUnit.Framework;
using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Messages.Tests
{
    [TestFixture()]
    public class PortCoupleTests
    {
        [Test()]
        public void PortCoupleOnePort()
        {
            var pc = new PortCouple(1212);
            Assert.AreEqual(1212, pc.First);
            Assert.IsFalse(pc.IsSecondPortPresent);
        }

        [Test()]
        public void PortCoupleTwoPort()
        {
            var pc = new PortCouple(1212,1215);
            Assert.AreEqual(1212, pc.First);
            Assert.IsTrue(pc.IsSecondPortPresent);
            Assert.AreEqual(1215,pc.Second);
        }
        

        [Test()]
        public void ParseOnePort()
        {
            var pc = PortCouple.Parse("1212");
            Assert.AreEqual(1212, pc.First);
            Assert.IsFalse(pc.IsSecondPortPresent);
        }

        [Test()]
        public void ParseTwoPort()
        {
            var pc = PortCouple.Parse("1212-1215");
            Assert.AreEqual(1212, pc.First);
            Assert.IsTrue(pc.IsSecondPortPresent);
            Assert.AreEqual(1215, pc.Second);
        }

        [Test()]
        public void ToStringOnePort()
        {
            var pc = new PortCouple(1212);
            Assert.AreEqual("1212", pc.ToString());
        }

        [Test()]
        public void ToStringTwoPort()
        {
            var pc = new PortCouple(1212,1215);
            Assert.AreEqual("1212-1215", pc.ToString());
        }
    }
}