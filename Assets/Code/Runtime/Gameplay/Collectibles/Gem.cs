using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Collectibles
{
    /// <summary>
    /// A pickup that rewards the player with <see cref="Value"/> points. The instance
    /// is pooled; it returns itself to the pool on collection rather than being
    /// destroyed.
    /// </summary>
    /// <remarks>
    /// Trigger detection relies on a kinematic <c>Rigidbody</c> on the gem prefab —
    /// the ball is Rigidbody-free (ADR-001) so the gem must be the "moving" side of
    /// the contact for Unity to dispatch <c>OnTriggerEnter</c>.
    ///
    /// The owning <see cref="GemPool"/> is injected via <see cref="Initialize"/>
    /// (called by <c>GemSpawner</c>) so this script never has to call back into the
    /// pool component by reference.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Gem : MonoBehaviour
    {
        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised with the gem's point value when the ball enters its trigger.")]
        private IntGameEventSO _onGemCollected;

        [SerializeField, Tooltip("Tag of the GameObject considered the ball. Pickup ignores any other collider.")]
        private string _ballTag = "Player";

        public int Value { get; private set; }

        private GemPool _owningPool;
        private bool _collected;

        private void Awake()
        {
            Debug.Assert(_onGemCollected != null, $"{nameof(Gem)} requires {nameof(_onGemCollected)}.", this);
            if (_onGemCollected == null)
            {
                Debug.LogError($"{nameof(Gem)} requires {nameof(_onGemCollected)}; disabling component.", this);
                enabled = false;
                return;
            }

            Rigidbody rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            Collider col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        /// <summary>
        /// Called by <c>GemSpawner</c> after taking the gem out of the pool. Sets the
        /// reward value and the pool to release back to. Idempotent across reuses.
        /// </summary>
        public void Initialize(int value, GemPool owningPool)
        {
            Value = value;
            _owningPool = owningPool;
            _collected = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_collected) return;
            if (!other.CompareTag(_ballTag)) return;

            _collected = true;
            _onGemCollected.Raise(Value);

            if (_owningPool != null)
            {
                _owningPool.Release(gameObject);
            }
            else
            {
                Debug.LogError($"{nameof(Gem)} collected without an owning pool; deactivating to avoid a zombie instance. Did GemSpawner forget to call Initialize?", this);
                gameObject.SetActive(false);
            }
        }
    }
}
