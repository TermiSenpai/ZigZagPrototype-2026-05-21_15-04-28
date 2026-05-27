using NUnit.Framework;
using UnityEngine;
using ZigZag.Runtime.Gameplay.World;

namespace ZigZag.Tests.EditMode.Gameplay.World
{
    [TestFixture]
    public sealed class SegmentTests
    {
        [Test]
        public void NewSegment_HasZeroCubes()
        {
            // Arrange + Act
            var segment = new Segment(Vector3.forward);

            // Assert
            Assert.AreEqual(0, segment.CubeCount);
        }

        [Test]
        public void NewSegment_StoresDirection()
        {
            var segment = new Segment(Vector3.right);

            Assert.AreEqual(Vector3.right, segment.Direction);
        }

        [Test]
        public void NewSegment_FallTriggerIndexStartsAtZero()
        {
            var segment = new Segment(Vector3.forward);

            Assert.AreEqual(0, segment.FallTriggerIndex);
        }

        [Test]
        public void AdvanceFallTrigger_IncrementsIndexByOne()
        {
            var segment = new Segment(Vector3.forward);

            segment.AdvanceFallTrigger();

            Assert.AreEqual(1, segment.FallTriggerIndex);
        }

        [Test]
        public void AdvanceFallTrigger_CalledThreeTimes_IndexIsThree()
        {
            var segment = new Segment(Vector3.forward);

            segment.AdvanceFallTrigger();
            segment.AdvanceFallTrigger();
            segment.AdvanceFallTrigger();

            Assert.AreEqual(3, segment.FallTriggerIndex);
        }
    }
}
