using UnityEngine;
using UnityEngine.Pool;
using ZigZag.Runtime.Data;

namespace ZigZag.Runtime.Gameplay.Collectibles
{
    /// <summary>
    /// Object pool for gem instances. Direct twin of <c>PlatformPool</c>: prewarms
    /// <see cref="GameConfigSO.GemPoolInitialSize"/> instances in <c>Awake</c> and
    /// exposes a minimal <see cref="Get"/> / <see cref="Release"/> surface so
    /// <c>GemSpawner</c> never sees the underlying <see cref="ObjectPool{T}"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GemPool : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Gem prefab spawned and returned by the pool. Must carry a Gem component, a kinematic Rigidbody and a trigger Collider.")]
        private GameObject _gemPrefab;

        [SerializeField, Tooltip("Source of the initial pool capacity.")]
        private GameConfigSO _config;

        private ObjectPool<GameObject> _pool;

        private void Awake()
        {
            Debug.Assert(_gemPrefab != null, $"{nameof(GemPool)} requires a gem prefab.", this);
            Debug.Assert(_config != null, $"{nameof(GemPool)} requires a {nameof(GameConfigSO)} reference.", this);
            if (_gemPrefab == null || _config == null) return;

            int capacity = _config.GemPoolInitialSize;
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

        /// <summary>Borrows a gem from the pool. Caller is responsible for setting transform and calling <c>Gem.Initialize</c>.</summary>
        public GameObject Get()
        {
            return _pool != null ? _pool.Get() : null;
        }

        /// <summary>Returns a previously borrowed gem to the pool. The instance is deactivated.</summary>
        public void Release(GameObject gem)
        {
            if (_pool == null || gem == null) return;
            _pool.Release(gem);
        }

        private GameObject CreateInstance()
        {
            GameObject instance = Instantiate(_gemPrefab, transform);
            instance.SetActive(false);
            return instance;
        }

        private void OnGet(GameObject gem)
        {
            gem.SetActive(true);
        }

        private void OnRelease(GameObject gem)
        {
            gem.SetActive(false);
        }

        private void OnDestroyInstance(GameObject gem)
        {
            if (gem != null) Destroy(gem);
        }

        private void Prewarm(int count)
        {
            GameObject[] preheated = new GameObject[count];
            for (int i = 0; i < count; i++) preheated[i] = _pool.Get();
            for (int i = 0; i < count; i++) _pool.Release(preheated[i]);
        }
    }
}
