using NUnit.Framework;
using UnityEngine;
using ZigZag.Runtime.Gameplay.CameraSystem;

namespace ZigZag.Tests.EditMode.Gameplay.CameraSystem
{
    /// <summary>
    /// Tests the pure projection used by <see cref="CameraFollow"/>: the desired camera
    /// world position must equal the camera origin shifted only along the global forward
    /// axis by the target's forward-projected displacement, with Y locked to the captured
    /// value. Lateral (perpendicular-to-forward) target motion must produce zero camera
    /// displacement — that is the whole point of the change.
    /// </summary>
    [TestFixture]
    public sealed class CameraFollowMathTests
    {
        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;
        private static readonly Vector3 CameraOrigin = new Vector3(10f, 8f, -4f);
        private static readonly Vector3 TargetOrigin = new Vector3(-2f, 0.65f, 3f);
        private const float LockedY = 8f;
        private const float Tolerance = 1e-4f;

        [Test]
        public void ComputeDesiredPosition_TargetAtOrigin_ReturnsCameraOriginWithLockedY()
        {
            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, TargetOrigin, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetMovedAlongPositiveZ_AdvancesCameraAlongForward()
        {
            // +Z motion has a positive component along forward (-1,0,1)/√2.
            // Δ = (0,0,7); progress = 7/√2 ≈ 4.9497.
            // forwardOffset = progress * forward = (-3.5, 0, 3.5).
            Vector3 targetNow = TargetOrigin + new Vector3(0f, 0f, 7f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x - 3.5f).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z + 3.5f).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetMovedAlongNegativeX_AdvancesCameraAlongForward()
        {
            // -X motion also has a positive component along (-1,0,1)/√2.
            // Δ = (-7,0,0); progress = 7/√2 ≈ 4.9497.
            // forwardOffset = (-3.5, 0, 3.5).
            Vector3 targetNow = TargetOrigin + new Vector3(-7f, 0f, 0f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x - 3.5f).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z + 3.5f).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetMovedPerpendicularToForward_LeavesCameraAtOrigin()
        {
            // Perpendicular-to-forward direction is (1,0,1)/√2 (the OTHER ball diagonal).
            // Any motion along it has zero dot with forward → zero camera displacement.
            Vector3 perpendicular = new Vector3(1f, 0f, 1f).normalized;
            Vector3 targetNow = TargetOrigin + perpendicular * 10f;

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetMovedDiagonally_AccumulatesBothAxes()
        {
            // Δ = (-7,0,7); progress = 14/√2 ≈ 9.8995.
            // forwardOffset = (-7, 0, 7).
            Vector3 targetNow = TargetOrigin + new Vector3(-7f, 0f, 7f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x - 7f).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z + 7f).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_TargetYChangesDuringFall_DoesNotAffectCameraXZ()
        {
            // The ball drops in Y when it falls off the path. forward is XZ-only
            // (its Y component is 0), so a Y-only delta must not move the camera at all.
            Vector3 targetNow = TargetOrigin + new Vector3(0f, -5f, 0f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, LockedY);

            Assert.That(desired.x, Is.EqualTo(CameraOrigin.x).Within(Tolerance));
            Assert.That(desired.y, Is.EqualTo(LockedY).Within(Tolerance));
            Assert.That(desired.z, Is.EqualTo(CameraOrigin.z).Within(Tolerance));
        }

        [Test]
        public void ComputeDesiredPosition_LockedYOverridesCameraOriginY()
        {
            // The caller decides the Y plane the camera stays on. cameraOrigin.y is
            // not what we want to read — we want the explicitly-locked Y. Use a
            // distinct value to prove the function ignores cameraOrigin.y.
            const float explicitLockedY = 42f;
            Vector3 targetNow = TargetOrigin + new Vector3(0f, 0f, 7f);

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                CameraOrigin, TargetOrigin, targetNow, GlobalForward, explicitLockedY);

            Assert.That(desired.y, Is.EqualTo(explicitLockedY).Within(Tolerance));
        }
    }
}
