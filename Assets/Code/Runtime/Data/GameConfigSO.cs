using UnityEngine;

namespace ZigZag.Runtime.Data
{
    /// <summary>
    /// Single source of truth for tunable gameplay parameters. Fields are private and
    /// serialized; reads happen through get-only properties so consumers cannot mutate
    /// the configuration at runtime (encapsulation rule, CLAUDE.md §5).
    /// </summary>
    /// <remarks>
    /// Only the subset required for the current iteration is exposed. New sections
    /// (path generation, gems, powerups, camera...) will be added as their systems
    /// come online.
    /// </remarks>
    [CreateAssetMenu(fileName = "SO_GameConfig", menuName = "ZigZag/Game Config")]
    public sealed class GameConfigSO : ScriptableObject
    {
        [Header("Movement")]
        [SerializeField, Tooltip("Ball speed at the start of a run, in units per second.")]
        private float _initialSpeed = 5f;

        [SerializeField, Tooltip("Speed gain applied each second while grounded.")]
        private float _acceleration = 0.05f;

        [SerializeField, Tooltip("Hard cap on the ball's forward speed.")]
        private float _maxSpeed = 12f;

        [Header("Falling")]
        [SerializeField, Tooltip("Downward speed applied once the ball leaves the path.")]
        private float _fallSpeed = 9.8f;

        [SerializeField, Tooltip("Y position below which the ball is considered to have fallen.")]
        private float _fallThreshold = -2f;

        [Header("Ground Check")]
        [SerializeField, Tooltip("Length of the downward raycast that probes for ground beneath the ball.")]
        private float _groundCheckDistance = 0.55f;

        [SerializeField, Tooltip("Layers considered solid ground for the ball.")]
        private LayerMask _groundLayerMask = ~0;

        [Header("Camera")]
        [SerializeField, Tooltip("SmoothDamp approach time used by the camera follow. 0 = snap, higher = laggier.")]
        private float _cameraFollowSmoothTime = 0.15f;

        public float InitialSpeed => _initialSpeed;
        public float Acceleration => _acceleration;
        public float MaxSpeed => _maxSpeed;
        public float FallSpeed => _fallSpeed;
        public float FallThreshold => _fallThreshold;
        public float GroundCheckDistance => _groundCheckDistance;
        public LayerMask GroundLayerMask => _groundLayerMask;
        public float CameraFollowSmoothTime => _cameraFollowSmoothTime;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_initialSpeed < 0f) _initialSpeed = 0f;
            if (_acceleration < 0f) _acceleration = 0f;
            if (_maxSpeed < _initialSpeed) _maxSpeed = _initialSpeed;
            if (_fallSpeed < 0f) _fallSpeed = 0f;
            if (_groundCheckDistance < 0f) _groundCheckDistance = 0f;
            if (_cameraFollowSmoothTime < 0f) _cameraFollowSmoothTime = 0f;
        }
#endif
    }
}
