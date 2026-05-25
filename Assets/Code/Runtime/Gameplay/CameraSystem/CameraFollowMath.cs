using UnityEngine;

namespace ZigZag.Runtime.Gameplay.CameraSystem
{
    /// <summary>
    /// Pure projection helper used by <see cref="CameraFollow"/>. Given the camera and
    /// target world positions captured at init time (origins), the target's current
    /// position, the global forward axis and the Y plane the camera must stay on,
    /// returns the desired camera world position.
    /// </summary>
    /// <remarks>
    /// The desired position is the camera origin shifted only along the forward axis
    /// by the projection of the target's displacement onto that axis. Perpendicular
    /// (lateral) target motion intentionally contributes zero camera displacement —
    /// this reproduces the original Ketchapp ZigZag behavior where the camera
    /// advances "upward" in screen space and the ball visibly serpentines across it.
    /// </remarks>
    public static class CameraFollowMath
    {
        /// <summary>
        /// Returns the camera position the follower should approach this frame.
        /// </summary>
        /// <param name="cameraOrigin">Camera world position captured at init time. The Y component is ignored — see <paramref name="lockedY"/>.</param>
        /// <param name="targetOrigin">Target world position captured at init time, used as the reference for the displacement.</param>
        /// <param name="targetCurrent">Target world position this frame.</param>
        /// <param name="forwardAxis">Unit vector representing forward progress. The path uses <c>(-1, 0, 1).normalized</c>.</param>
        /// <param name="lockedY">Y world coordinate the camera must remain on; overrides <c>cameraOrigin.y</c>.</param>
        public static Vector3 ComputeDesiredPosition(
            Vector3 cameraOrigin,
            Vector3 targetOrigin,
            Vector3 targetCurrent,
            Vector3 forwardAxis,
            float lockedY)
        {
            Vector3 delta = targetCurrent - targetOrigin;
            float forwardProgress = Vector3.Dot(delta, forwardAxis);
            Vector3 forwardOffset = forwardProgress * forwardAxis;

            return new Vector3(
                cameraOrigin.x + forwardOffset.x,
                lockedY,
                cameraOrigin.z + forwardOffset.z);
        }
    }
}
