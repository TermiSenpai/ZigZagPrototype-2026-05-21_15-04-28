using System.Collections.Generic;
using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.Collectibles;

namespace ZigZag.Runtime.Gameplay.World
{
    /// <summary>
    /// Spawns and recycles platform segments around the ball. Each segment is a run of
    /// cubes along a single world axis (<c>-X</c> or <c>+Z</c>), alternating each time
    /// a segment ends. Spawning is bounded by <see cref="GameConfigSO.AheadBuffer"/>
    /// and recycling by <see cref="GameConfigSO.BehindBuffer"/>, both measured along
    /// the global forward axis <c>(-1, 0, 1)/√2</c> — the diagonal between the two
    /// ball directions, which is the natural "progress" direction of the path.
    /// </summary>
    /// <remarks>
    /// Determinism: uses <see cref="System.Random"/> with the configured seed, not
    /// <see cref="UnityEngine.Random"/> (which is process-global and would leak state
    /// across systems). Same seed → identical path on every run.
    ///
    /// Pre-population: the path is populated on <c>Start</c> so the menu can show a
    /// real path behind the UI. Generation toggles on/off via the lifecycle event
    /// channels — the path is rebuilt from scratch on <see cref="HandleGameReset"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PathGenerator : MonoBehaviour
    {
        private const int MaxCubesSpawnedPerFrame = 20;
        private const int InitializationSafetyLimit = 500;

        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of tunable values (path start, cube size, buffers, seed).")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Pool the generator borrows platform cubes from.")]
        private PlatformPool _pool;

        [SerializeField, Tooltip("Transform of the ball; the generator measures ahead/behind distances relative to this.")]
        private Transform _ballTransform;

        [SerializeField, Tooltip("Optional. If wired, finalized segments are offered to this spawner for gem placement.")]
        private GemSpawner _gemSpawner;

        [Header("Event Channels")]
        [SerializeField, Tooltip("Resumes generation when the run starts.")]
        private GameEventSO _onGameStarted;

        [SerializeField, Tooltip("Pauses generation when the ball falls.")]
        private GameEventSO _onGameOver;

        [SerializeField, Tooltip("Clears the active path and rebuilds it from the configured start position.")]
        private GameEventSO _onGameReset;

        private static readonly Vector3 AlongNegativeX = new Vector3(-1f, 0f, 0f);
        private static readonly Vector3 AlongPositiveZ = new Vector3(0f, 0f, 1f);
        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;
        private static readonly Vector3 GlobalPerpendicular = new Vector3(1f, 0f, 1f).normalized;

        private readonly Queue<Segment> _segments = new Queue<Segment>(16);
        private System.Random _random;
        private Vector3 _currentDirection;
        private Vector3 _runStartPosition;
        private float _driftCapPositivePerp;
        private float _driftCapNegativePerp;
        private Segment _currentSegment;
        private int _currentSegmentTargetLength;
        private Vector3 _lastCubePosition;
        private bool _isGenerating;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(PathGenerator)} requires a {nameof(GameConfigSO)} reference.", this);
            Debug.Assert(_pool != null, $"{nameof(PathGenerator)} requires a {nameof(PlatformPool)} reference.", this);
            Debug.Assert(_ballTransform != null, $"{nameof(PathGenerator)} requires a ball Transform reference.", this);
            Debug.Assert(_onGameStarted != null, $"{nameof(PathGenerator)} requires {nameof(_onGameStarted)}.", this);
            Debug.Assert(_onGameOver != null, $"{nameof(PathGenerator)} requires {nameof(_onGameOver)}.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(PathGenerator)} requires {nameof(_onGameReset)}.", this);

            if (_config != null) _random = CreateRandom();
        }

        private void OnEnable()
        {
            if (_onGameStarted != null) _onGameStarted.Register(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Register(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            if (_onGameStarted != null) _onGameStarted.Unregister(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Unregister(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
        }

        private void Start()
        {
            // Pre-populate so the menu shows a real path under the bola, not empty space.
            InitializePath();
        }

        private void Update()
        {
            if (!_isGenerating) return;
            EnsureAhead();
            TriggerFalls();
            RecycleBehind();
        }

        private void HandleGameStarted()
        {
            _isGenerating = true;
        }

        private void HandleGameOver()
        {
            _isGenerating = false;
        }

        private void HandleGameReset()
        {
            _isGenerating = false;
            ClearAllSegments();
            if (_config != null) _random = CreateRandom();
            InitializePath();
        }

        /// <summary>
        /// Coin-flips the run's starting cube + segment direction between the two
        /// configured options. The pair is fixed: primary start with -X, alternate
        /// start with +Z — they are mirror images across the global forward axis,
        /// so distance scoring (which uses <see cref="GameConfigSO.PathStartPosition"/>
        /// as its origin) sees the same forward projection either way.
        /// Also assigns the asymmetric perpendicular drift caps: the side that the
        /// starting direction pushes the path toward gets the larger cap, the
        /// opposite side gets the smaller one — so the path opens up more on the
        /// side it's heading into and stays tighter on the other.
        /// </summary>
        /// <remarks>
        /// On the perpendicular axis <c>(1,0,1)/√2</c>: a -X step contributes
        /// <c>-1/√2</c> (pushes -perp) and a +Z step contributes <c>+1/√2</c>
        /// (pushes +perp). The pairing (start direction → "big-cap side") follows
        /// directly from those signs.
        /// </remarks>
        private void PickRunStart()
        {
            bool useAlternate = _random.Next(2) == 1;
            if (useAlternate)
            {
                _runStartPosition = _config.PathStartPositionAlternate;
                _currentDirection = AlongPositiveZ;
                _driftCapPositivePerp = _config.DriftCapAlongStartAxis;
                _driftCapNegativePerp = _config.DriftCapAlongCrossAxis;
            }
            else
            {
                _runStartPosition = _config.PathStartPosition;
                _currentDirection = AlongNegativeX;
                _driftCapNegativePerp = _config.DriftCapAlongStartAxis;
                _driftCapPositivePerp = _config.DriftCapAlongCrossAxis;
            }
        }

        private System.Random CreateRandom()
        {
            // Sentinel: seed == 0 → pick a fresh seed every run so each retry feels new.
            // Any other value → deterministic, same path every time (useful for debugging).
            // CLAUDE.md §2 forbids gameplay non-determinism in general; this is the one
            // approved escape hatch and it only affects which seed is used, not the
            // gameplay simulation itself, which remains deterministic from the seed.
            int seed = _config.GenerationSeed != 0 ? _config.GenerationSeed : System.Environment.TickCount;
            return new System.Random(seed);
        }

        private void InitializePath()
        {
            if (_config == null || _pool == null) return;

            PickRunStart();
            StartNewSegment(isFirstSegment: true);

            int safety = InitializationSafetyLimit;
            while (DistanceAhead() < _config.AheadBuffer && safety-- > 0)
            {
                SpawnNextCubeOrStartNewSegment();
            }

            if (safety <= 0)
            {
                Debug.LogError($"{nameof(PathGenerator)}: initialization hit the safety limit. Check ball position and ahead buffer.", this);
            }
        }

        private void EnsureAhead()
        {
            if (_config == null) return;

            int spawnedThisFrame = 0;
            while (DistanceAhead() < _config.AheadBuffer && spawnedThisFrame < MaxCubesSpawnedPerFrame)
            {
                SpawnNextCubeOrStartNewSegment();
                spawnedThisFrame++;
            }
        }

        /// <summary>
        /// Tells each cube the ball has just passed to start its collapse animation.
        /// Walks the queue from the oldest segment forward, using each segment's
        /// <see cref="Segment.FallTriggerIndex"/> as a watermark so a cube is only
        /// triggered once and we don't rescan the whole path every frame. Stops as
        /// soon as a still-ahead cube is found — forward offset along the path is
        /// monotonic, so anything past that point is also still ahead.
        /// </summary>
        private void TriggerFalls()
        {
            if (_ballTransform == null || _config == null) return;

            float threshold = _config.PlatformFallStartBehind;
            if (threshold <= 0f) return;

            Vector3 ballPosition = _ballTransform.position;

            // Queue<T>.GetEnumerator returns a struct enumerator — no allocation per frame.
            foreach (Segment segment in _segments)
            {
                IReadOnlyList<GameObject> cubes = segment.Cubes;
                bool reachedStillAheadCube = false;

                while (segment.FallTriggerIndex < cubes.Count)
                {
                    GameObject cube = cubes[segment.FallTriggerIndex];
                    // Y component of GlobalForward is 0, so a cube that has already started
                    // falling still reports the same forward offset as before — safe to use
                    // its live position here.
                    float forwardOffset = Vector3.Dot(cube.transform.position - ballPosition, GlobalForward);
                    if (forwardOffset > -threshold)
                    {
                        reachedStillAheadCube = true;
                        break;
                    }

                    PlatformFaller faller = cube.GetComponent<PlatformFaller>();
                    if (faller != null) faller.Begin();
                    segment.AdvanceFallTrigger();
                }

                if (reachedStillAheadCube) return;
            }
        }

        private void RecycleBehind()
        {
            if (_config == null) return;

            // Always keep the current segment around even if the ball is somehow far past it.
            while (_segments.Count > 1)
            {
                Segment oldest = _segments.Peek();
                float behindDistance = -Vector3.Dot(oldest.LastCubePosition - _ballTransform.position, GlobalForward);
                if (behindDistance <= _config.BehindBuffer) break;

                _segments.Dequeue();
                ReleaseSegmentCubes(oldest);
            }

            // Sweep any gems left behind by recycled cubes back to the pool. Done here
            // (not inside the segment loop) so we run the gem check at most once per
            // frame regardless of how many segments were dequeued.
            if (_gemSpawner != null)
            {
                _gemSpawner.ReleaseGemsBehind(_ballTransform.position, GlobalForward, _config.BehindBuffer);
            }
        }

        private void SpawnNextCubeOrStartNewSegment()
        {
            if (_currentSegment == null)
            {
                StartNewSegment(isFirstSegment: true);
                return;
            }

            if (_currentSegment.CubeCount >= _currentSegmentTargetLength)
            {
                // Hand the just-finalized segment to the gem spawner before reassigning.
                if (_gemSpawner != null) _gemSpawner.TryPopulateSegment(_currentSegment);

                FlipDirection();
                StartNewSegment(isFirstSegment: false);
                return;
            }

            Vector3 step = GetSpawnStep(_currentDirection);
            SpawnCubeAt(_lastCubePosition + step);
        }

        private void StartNewSegment(bool isFirstSegment)
        {
            _currentSegment = new Segment(_currentDirection);
            _segments.Enqueue(_currentSegment);

            Vector3 firstCubePosition = isFirstSegment
                ? _runStartPosition
                : _lastCubePosition + GetSpawnStep(_currentDirection);

            _currentSegmentTargetLength = PickSegmentLength(firstCubePosition);

            SpawnCubeAt(firstCubePosition);
        }

        /// <summary>
        /// Picks a length for the segment about to be spawned, shrinking it so the
        /// last cube's perpendicular drift stays within the cap on the side this
        /// segment is heading toward (<see cref="_driftCapPositivePerp"/> if it pushes
        /// +perp, <see cref="_driftCapNegativePerp"/> if it pushes -perp). The two
        /// caps are asymmetric and swap based on the run's starting side — see
        /// <see cref="PickRunStart"/> — so the path zigzags wider on the side the
        /// starting direction heads into and tighter on the other.
        /// </summary>
        private int PickSegmentLength(Vector3 segmentStartPosition)
        {
            int minLen = _config.SegmentMinLength;
            int maxLen = _config.SegmentMaxLength;
            int desired = _random.Next(minLen, maxLen + 1);

            Vector3 step = GetSpawnStep(_currentDirection);
            float perCubeLateral = Vector3.Dot(step, GlobalPerpendicular);
            if (Mathf.Approximately(perCubeLateral, 0f)) return desired;

            bool driftsPositive = perCubeLateral > 0f;
            float cap = driftsPositive ? _driftCapPositivePerp : _driftCapNegativePerp;
            if (cap <= 0f) return desired;

            float startLateral = Vector3.Dot(segmentStartPosition - _runStartPosition, GlobalPerpendicular);
            float signedBound = driftsPositive ? cap : -cap;
            float headroom = driftsPositive ? (signedBound - startLateral) : (startLateral - signedBound);
            if (headroom <= 0f) return minLen;

            int allowed = 1 + Mathf.FloorToInt(headroom / Mathf.Abs(perCubeLateral));
            return Mathf.Clamp(Mathf.Min(desired, allowed), minLen, maxLen);
        }

        private void SpawnCubeAt(Vector3 position)
        {
            GameObject cube = _pool.Get();
            if (cube == null) return;

            cube.transform.position = position;
            _currentSegment.AddCube(cube);
            _lastCubePosition = position;
        }

        private void FlipDirection()
        {
            // Vector3 equality on these static readonly axis vectors is byte-stable; no
            // floating-point drift concerns here.
            _currentDirection = _currentDirection == AlongNegativeX ? AlongPositiveZ : AlongNegativeX;
        }

        private void ClearAllSegments()
        {
            while (_segments.Count > 0)
            {
                Segment s = _segments.Dequeue();
                ReleaseSegmentCubes(s);
            }
            _currentSegment = null;
        }

        private void ReleaseSegmentCubes(Segment segment)
        {
            // For-loop with index avoids the IEnumerator allocation that a foreach over
            // IReadOnlyList<T> would incur (CLAUDE.md §8).
            IReadOnlyList<GameObject> cubes = segment.Cubes;
            for (int i = 0; i < cubes.Count; i++)
            {
                _pool.Release(cubes[i]);
            }
        }

        private float DistanceAhead()
        {
            if (_ballTransform == null) return float.MaxValue;
            return Vector3.Dot(_lastCubePosition - _ballTransform.position, GlobalForward);
        }

        private Vector3 GetSpawnStep(Vector3 direction)
        {
            return new Vector3(direction.x * _config.CubeSize.x, 0f, direction.z * _config.CubeSize.z);
        }
    }
}
