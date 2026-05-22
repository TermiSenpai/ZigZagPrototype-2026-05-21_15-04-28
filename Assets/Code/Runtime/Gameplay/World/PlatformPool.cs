using UnityEngine;
using UnityEngine.Pool;
using ZigZag.Runtime.Data;

namespace ZigZag.Runtime.Gameplay.World
{
    /// <summary>
    /// Thin wrapper around <see cref="ObjectPool{T}"/> for platform cubes. Hides the
    /// pool plumbing (create / get / release / destroy callbacks) so the
    /// <see cref="PathGenerator"/> only sees <see cref="Get"/> and <see cref="Release"/>.
    /// </summary>
    /// <remarks>
    /// ADR-002 in zigzag_architecture.md picks the built-in <c>UnityEngine.Pool</c>
    /// over a custom implementation. The pool prewarms <see cref="GameConfigSO.PlatformPoolInitialSize"/>
    /// instances in <c>Awake</c> so the first segments can be spawned without any
    /// <c>Instantiate</c> hitting runtime hot paths.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlatformPool : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Cube prefab spawned and returned by the pool.")]
        private GameObject _platformPrefab;

        [SerializeField, Tooltip("Source of the initial pool capacity.")]
        private GameConfigSO _config;

        private ObjectPool<GameObject> _pool;

        private void Awake()
        {
            Debug.Assert(_platformPrefab != null, $"{nameof(PlatformPool)} requires a platform prefab.", this);
            Debug.Assert(_config != null, $"{nameof(PlatformPool)} requires a {nameof(GameConfigSO)} reference.", this);
            if (_platformPrefab == null || _config == null) return;

            int capacity = _config.PlatformPoolInitialSize;
            _pool = new ObjectPool<GameObject>(
                createFunc: CreateInstance,
                actionOnGet: OnGet,
                actionOnRelease: OnRelease,
                actionOnDestroy: OnDestroyInstance,
                collectionCheck: false,
                defaultCapacity: capacity,
                maxSize: capacity * 2);

            Prewarm(capacity);
        }

        /// <summary>Borrows a cube from the pool. Caller is responsible for setting its transform.</summary>
        public GameObject Get()
        {
            return _pool != null ? _pool.Get() : null;
        }

        /// <summary>Returns a previously borrowed cube to the pool. The cube is deactivated.</summary>
        public void Release(GameObject cube)
        {
            if (_pool == null || cube == null) return;
            _pool.Release(cube);
        }

        private GameObject CreateInstance()
        {
            GameObject instance = Instantiate(_platformPrefab, transform);
            instance.SetActive(false);
            return instance;
        }

        private void OnGet(GameObject cube)
        {
            cube.SetActive(true);
        }

        private void OnRelease(GameObject cube)
        {
            cube.SetActive(false);
        }

        private void OnDestroyInstance(GameObject cube)
        {
            if (cube != null) Destroy(cube);
        }

        private void Prewarm(int count)
        {
            // Cycle each instance through Get/Release so the pool ends up holding `count`
            // ready-to-use cubes. Calling only Get would leave them as active; only
            // Release would underflow. The cubes are parented to this transform via
            // CreateInstance, keeping the hierarchy tidy.
            GameObject[] preheated = new GameObject[count];
            for (int i = 0; i < count; i++) preheated[i] = _pool.Get();
            for (int i = 0; i < count; i++) _pool.Release(preheated[i]);
        }
    }
}
