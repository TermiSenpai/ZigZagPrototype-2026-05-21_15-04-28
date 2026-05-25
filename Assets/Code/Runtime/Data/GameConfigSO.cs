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
        [SerializeField, Tooltip("World position of the first cube of the generated path, paired with an initial -X segment direction. The ball spawn point should sit above a static platform whose corner is adjacent to this cube.")]
        private Vector3 _pathStartPosition = new Vector3(-2f, -3f, 3f);

        [SerializeField, Tooltip("Mirror-image alternate start, paired with an initial +Z segment direction. Each run randomly picks between this and PathStartPosition so the path doesn't always extend in the same direction from the spawn platform. Must project to the same value as PathStartPosition on the global forward axis (-X+Z diagonal) so the score baseline is unaffected.")]
        private Vector3 _pathStartPositionAlternate = new Vector3(-3f, -3f, 2f);

        [SerializeField, Tooltip("Size of a single platform cube in world units. X and Z determine the spacing between consecutive cubes; Y is purely visual.")]
        private Vector3 _cubeSize = new Vector3(1f, 5f, 1f);

        [SerializeField, Tooltip("Minimum cubes per segment (inclusive). Each segment's length is picked at random in [min, max].")]
        private int _segmentMinLength = 1;

        [SerializeField, Tooltip("Maximum cubes per segment (inclusive). After this many cubes the path direction must flip.")]
        private int _segmentMaxLength = 5;

        [SerializeField, Tooltip("Soft cap (in world units) on how far the path may drift perpendicular to global forward on the side the starting direction pushes the path toward. Larger than the cross-side cap so the zigzag opens up wider on the side it's heading into. Set to 0 to disable the bias on this side.")]
        private float _driftCapAlongStartAxis = 4f;

        [SerializeField, Tooltip("Soft cap on perpendicular drift on the side opposite to the one the starting direction pushes toward. Smaller than the start-side cap to keep the zigzag visually contained on the trailing side. Set to 0 to disable the bias on this side.")]
        private float _driftCapAlongCrossAxis = 3f;

        [SerializeField, Tooltip("Distance ahead of the ball, measured along the global forward axis (-X +Z diagonal), that the generator keeps populated.")]
        private float _aheadBuffer = 30f;

        [SerializeField, Tooltip("Distance behind the ball before cubes are released back to the pool.")]
        private float _behindBuffer = 10f;

        [SerializeField, Tooltip("Generation seed. 0 = different seed every run (uses Environment.TickCount); any other value = deterministic, same path every Retry. Use a non-zero value when bug-hunting a specific run.")]
        private int _generationSeed = 0;

        [Header("Gems")]
        [SerializeField, Range(0f, 1f), Tooltip("Probability per finalized segment that a gem is placed on one of its cubes.")]
        private float _gemSpawnProbability = 0.3f;

        [SerializeField, Tooltip("Coins awarded per gem collected. Powerups may temporarily override this multiplier at runtime — see GDD §5.5.")]
        private int _gemValue = 1;

        [SerializeField, Tooltip("Vertical offset above a cube's center where a gem sits. Pick a value clear of both the cube top and the ball radius so collection is reliable.")]
        private float _gemHeightAboveCubeCenter = 3.2f;

        [Header("Score")]
        [SerializeField, Tooltip("Points per unit of forward progress (measured along the global forward axis, -X+Z diagonal).")]
        private int _distanceMultiplier = 1;

        [Header("Game Feel")]
        [SerializeField, Tooltip("Duration (real seconds) of the hit-stop applied when the ball falls. Time.timeScale is set to 0 for this long before the GameOver panel appears. 0 disables the freeze.")]
        private float _freezeFrameOnDeathSeconds = 0.1f;

        [Header("Pooling")]
        [SerializeField, Tooltip("Number of platform cubes the pool prewarms on Awake. The pool grows up to twice this value if pressure spikes.")]
        private int _platformPoolInitialSize = 50;

        [SerializeField, Tooltip("Number of gem instances the gem pool prewarms on Awake.")]
        private int _gemPoolInitialSize = 20;

        public float InitialSpeed => _initialSpeed;
        public float Acceleration => _acceleration;
        public float MaxSpeed => _maxSpeed;
        public float FallSpeed => _fallSpeed;
        public float FallThreshold => _fallThreshold;
        public float GroundCheckDistance => _groundCheckDistance;
        public LayerMask GroundLayerMask => _groundLayerMask;
        public float CameraFollowSmoothTime => _cameraFollowSmoothTime;
        public Vector3 PathStartPosition => _pathStartPosition;
        public Vector3 PathStartPositionAlternate => _pathStartPositionAlternate;
        public Vector3 CubeSize => _cubeSize;
        public int SegmentMinLength => _segmentMinLength;
        public int SegmentMaxLength => _segmentMaxLength;
        public float DriftCapAlongStartAxis => _driftCapAlongStartAxis;
        public float DriftCapAlongCrossAxis => _driftCapAlongCrossAxis;
        public float AheadBuffer => _aheadBuffer;
        public float BehindBuffer => _behindBuffer;
        public int GenerationSeed => _generationSeed;
        public int PlatformPoolInitialSize => _platformPoolInitialSize;
        public float GemSpawnProbability => _gemSpawnProbability;
        public int GemValue => _gemValue;
        public float GemHeightAboveCubeCenter => _gemHeightAboveCubeCenter;
        public int DistanceMultiplier => _distanceMultiplier;
        public int GemPoolInitialSize => _gemPoolInitialSize;
        public float FreezeFrameOnDeathSeconds => _freezeFrameOnDeathSeconds;

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
            if (_driftCapAlongStartAxis < 0f) _driftCapAlongStartAxis = 0f;
            if (_driftCapAlongCrossAxis < 0f) _driftCapAlongCrossAxis = 0f;
            if (_aheadBuffer < 1f) _aheadBuffer = 1f;
            if (_behindBuffer < 0f) _behindBuffer = 0f;
            if (_platformPoolInitialSize < 1) _platformPoolInitialSize = 1;
            if (_cubeSize.x <= 0f) _cubeSize.x = 0.01f;
            if (_cubeSize.y <= 0f) _cubeSize.y = 0.01f;
            if (_cubeSize.z <= 0f) _cubeSize.z = 0.01f;
            if (_gemValue < 0) _gemValue = 0;
            if (_distanceMultiplier < 0) _distanceMultiplier = 0;
            if (_gemPoolInitialSize < 1) _gemPoolInitialSize = 1;
            if (_gemHeightAboveCubeCenter < 0f) _gemHeightAboveCubeCenter = 0f;
            if (_freezeFrameOnDeathSeconds < 0f) _freezeFrameOnDeathSeconds = 0f;
        }
#endif
    }
}
