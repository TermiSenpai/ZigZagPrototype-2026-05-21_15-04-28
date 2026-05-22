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
    /// (gems, powerups...) will be added as their systems come online.
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

        [Header("Path Generation")]
        [SerializeField, Tooltip("World position of the first cube of the generated path. The ball spawn point should sit above this position.")]
        private Vector3 _pathStartPosition = new Vector3(-2f, -3f, 3f);

        [SerializeField, Tooltip("Size of a single platform cube in world units. X and Z determine the spacing between consecutive cubes; Y is purely visual.")]
        private Vector3 _cubeSize = new Vector3(1f, 5f, 1f);

        [SerializeField, Tooltip("Minimum cubes per segment (inclusive). Each segment's length is picked at random in [min, max].")]
        private int _segmentMinLength = 1;

        [SerializeField, Tooltip("Maximum cubes per segment (inclusive). After this many cubes the path direction must flip.")]
        private int _segmentMaxLength = 5;

        [SerializeField, Tooltip("Distance ahead of the ball, measured along the global forward axis (-X +Z diagonal), that the generator keeps populated.")]
        private float _aheadBuffer = 30f;

        [SerializeField, Tooltip("Distance behind the ball before cubes are released back to the pool.")]
        private float _behindBuffer = 10f;

        [SerializeField, Tooltip("Generation seed. 0 = different seed every run (uses Environment.TickCount); any other value = deterministic, same path every Retry. Use a non-zero value when bug-hunting a specific run.")]
        private int _generationSeed = 0;

        [Header("Pooling")]
        [SerializeField, Tooltip("Number of platform cubes the pool prewarms on Awake. The pool grows up to twice this value if pressure spikes.")]
        private int _platformPoolInitialSize = 50;

        public float InitialSpeed => _initialSpeed;
        public float Acceleration => _acceleration;
        public float MaxSpeed => _maxSpeed;
        public float FallSpeed => _fallSpeed;
        public float FallThreshold => _fallThreshold;
        public float GroundCheckDistance => _groundCheckDistance;
        public LayerMask GroundLayerMask => _groundLayerMask;
        public float CameraFollowSmoothTime => _cameraFollowSmoothTime;
        public Vector3 PathStartPosition => _pathStartPosition;
        public Vector3 CubeSize => _cubeSize;
        public int SegmentMinLength => _segmentMinLength;
        public int SegmentMaxLength => _segmentMaxLength;
        public float AheadBuffer => _aheadBuffer;
        public float BehindBuffer => _behindBuffer;
        public int GenerationSeed => _generationSeed;
        public int PlatformPoolInitialSize => _platformPoolInitialSize;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_initialSpeed < 0f) _initialSpeed = 0f;
            if (_acceleration < 0f) _acceleration = 0f;
            if (_maxSpeed < _initialSpeed) _maxSpeed = _initialSpeed;
            if (_fallSpeed < 0f) _fallSpeed = 0f;
            if (_groundCheckDistance < 0f) _groundCheckDistance = 0f;
            if (_cameraFollowSmoothTime < 0f) _cameraFollowSmoothTime = 0f;
            if (_segmentMinLength < 1) _segmentMinLength = 1;
            if (_segmentMaxLength < _segmentMinLength) _segmentMaxLength = _segmentMinLength;
            if (_aheadBuffer < 1f) _aheadBuffer = 1f;
            if (_behindBuffer < 0f) _behindBuffer = 0f;
            if (_platformPoolInitialSize < 1) _platformPoolInitialSize = 1;
            if (_cubeSize.x <= 0f) _cubeSize.x = 0.01f;
            if (_cubeSize.y <= 0f) _cubeSize.y = 0.01f;
            if (_cubeSize.z <= 0f) _cubeSize.z = 0.01f;
        }
#endif
    }
}
