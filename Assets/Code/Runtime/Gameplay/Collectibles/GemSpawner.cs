using System.Collections.Generic;
using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.World;

namespace ZigZag.Runtime.Gameplay.Collectibles
{
    /// <summary>
    /// Decides whether a freshly finalized segment receives a gem, and places it on
    /// a randomly chosen cube of that segment. Called by <c>PathGenerator</c> at the
    /// moment a segment reaches its target length — never on a per-frame basis.
    /// </summary>
    /// <remarks>
    /// Uses its own <see cref="System.Random"/> seeded from
    /// <see cref="GameConfigSO.GenerationSeed"/>, reset on every <c>_onGameReset</c>
    /// just like the path generator. Same seed → same gem layout on every Retry,
    /// independent of (but reproducible alongside) the path layout.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GemSpawner : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of gem spawn probability, gem value and height offset.")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Pool gem instances are taken from.")]
        private GemPool _pool;

        [Header("Event Channels")]
        [SerializeField, Tooltip("Listened-to: reseeds the placement RNG so each run is reproducible from the seed.")]
        private GameEventSO _onGameReset;

        private System.Random _random;

        // TODO: prune collected gems from this list during play if endurance runs
        // become a thing — today it only resets between runs.
        private readonly List<GameObject> _activeGems = new List<GameObject>(32);

        // Parallel to _activeGems: the cube each gem was placed on. The gem follows
        // its cube's Y in LateUpdate so it falls with the cube without being parented
        // to it — parenting a (45,0,45)-rotated gem under a non-uniformly-scaled cube
        // (1,5,1) produces a sheared world transform that Unity can't represent in a
        // local Vector3 scale, which is what made gems render thin.
        private readonly List<GameObject> _supportCubes = new List<GameObject>(32);

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(GemSpawner)} requires a {nameof(GameConfigSO)} reference.", this);
            Debug.Assert(_pool != null, $"{nameof(GemSpawner)} requires a {nameof(GemPool)} reference.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(GemSpawner)} requires {nameof(_onGameReset)}.", this);

            if (_config != null) _random = CreateRandom();
        }

        private void OnEnable()
        {
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
        }

        /// <summary>
        /// Drives each active gem's Y to track its supporting cube so the gem visibly
        /// falls when the cube's <c>PlatformFaller</c> collapses it. Runs in
        /// <c>LateUpdate</c> so it reads the cube's post-fall-step position and the
        /// gem renders at the correct height the same frame.
        /// </summary>
        private void LateUpdate()
        {
            if (_config == null) return;
            float offset = _config.GemHeightAboveCubeCenter;

            for (int i = 0; i < _activeGems.Count; i++)
            {
                GameObject gem = _activeGems[i];
                GameObject cube = _supportCubes[i];
                if (gem == null || cube == null) continue;
                // Stop tracking once either side is inactive — the cube may have been
                // recycled (SetActive(false)) and reused at a new position elsewhere.
                if (!gem.activeSelf || !cube.activeSelf) continue;

                Vector3 p = gem.transform.position;
                p.y = cube.transform.position.y + offset;
                gem.transform.position = p;
            }
        }

        /// <summary>
        /// Rolls against <see cref="GameConfigSO.GemSpawnProbability"/> and, if it
        /// passes, places one gem on a uniformly random cube of <paramref name="segment"/>.
        /// Safe to call with a null or empty segment — does nothing in that case.
        /// </summary>
        public void TryPopulateSegment(Segment segment)
        {
            if (segment == null || segment.CubeCount == 0) return;
            if (_config == null || _pool == null || _random == null) return;

            if (_random.NextDouble() >= _config.GemSpawnProbability) return;

            int cubeIndex = _random.Next(0, segment.CubeCount);
            IReadOnlyList<GameObject> cubes = segment.Cubes;
            GameObject cube = cubes[cubeIndex];
            if (cube == null) return;

            GameObject gemGo = _pool.Get();
            if (gemGo == null) return;

            // Position only — preserve the prefab's rotation (octahedron look comes from
            // the prefab's (45, 0, 45) Euler angles, which the pool round-trips intact).
            gemGo.transform.position = cube.transform.position + Vector3.up * _config.GemHeightAboveCubeCenter;

            Gem gem = gemGo.GetComponent<Gem>();
            if (gem != null) gem.Initialize(_config.GemValue, _pool);
            _activeGems.Add(gemGo);
            _supportCubes.Add(cube);
        }

        /// <summary>
        /// Releases any active gem whose projected position along <paramref name="globalForward"/>
        /// is more than <paramref name="behindBuffer"/> units behind <paramref name="ballPosition"/>.
        /// Called by <c>PathGenerator</c> after recycling cubes, so orphan gems on
        /// pool-released cubes return to the pool instead of floating off-screen.
        /// </summary>
        /// <remarks>
        /// The loop walks <see cref="_activeGems"/> back-to-front so swap-removal stays
        /// O(1) per release. <c>_pool.Release</c> deactivates the gem; the
        /// <see cref="HandleGameReset"/> filter on <c>activeSelf</c> still works for
        /// the surviving entries because they remain active.
        /// </remarks>
        public void ReleaseGemsBehind(Vector3 ballPosition, Vector3 globalForward, float behindBuffer)
        {
            if (_pool == null) return;

            for (int i = _activeGems.Count - 1; i >= 0; i--)
            {
                GameObject g = _activeGems[i];
                if (g == null)
                {
                    RemoveTrackingAt(i);
                    continue;
                }
                if (!g.activeSelf)
                {
                    // Already collected (Gem.OnTriggerEnter released it). Prune.
                    RemoveTrackingAt(i);
                    continue;
                }

                float behindDistance = -Vector3.Dot(g.transform.position - ballPosition, globalForward);
                if (behindDistance <= behindBuffer) continue;

                _pool.Release(g);
                RemoveTrackingAt(i);
            }
        }

        private void HandleGameReset()
        {
            for (int i = 0; i < _activeGems.Count; i++)
            {
                GameObject g = _activeGems[i];
                // Skip collected gems — they were already released by Gem.OnTriggerEnter
                // and the pool deactivates released instances, so activeSelf is false.
                if (g != null && g.activeSelf) _pool.Release(g);
            }
            _activeGems.Clear();
            _supportCubes.Clear();

            if (_config != null) _random = CreateRandom();
        }

        private void RemoveTrackingAt(int i)
        {
            _activeGems.RemoveAt(i);
            _supportCubes.RemoveAt(i);
        }

        private System.Random CreateRandom()
        {
            // Matches PathGenerator's sentinel: 0 = fresh seed each run via TickCount,
            // anything else = deterministic. Same int → reproducible gem layout. The
            // RNG instance is independent of PathGenerator's, so the two systems do
            // not consume each other's random sequence.
            int seed = _config.GenerationSeed != 0 ? _config.GenerationSeed : System.Environment.TickCount;
            return new System.Random(seed);
        }
    }
}
