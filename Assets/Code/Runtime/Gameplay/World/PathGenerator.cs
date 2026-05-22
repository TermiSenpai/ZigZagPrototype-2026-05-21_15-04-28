using System.Collections.Generic;
using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;

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

        private readonly Queue<Segment> _segments = new Queue<Segment>(16);
        private System.Random _random;
        private Vector3 _currentDirection;
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

            _currentDirection = AlongNegativeX;
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
            _currentSegmentTargetLength = _random.Next(_config.SegmentMinLength, _config.SegmentMaxLength + 1);

            Vector3 firstCubePosition = isFirstSegment
                ? _config.PathStartPosition
                : _lastCubePosition + GetSpawnStep(_currentDirection);

            SpawnCubeAt(firstCubePosition);
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
