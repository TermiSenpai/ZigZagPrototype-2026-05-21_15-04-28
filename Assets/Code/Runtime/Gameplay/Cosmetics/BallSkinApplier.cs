using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    /// <summary>
    /// Lives on the ball. Listens for <see cref="_onSkinEquipped"/> and swaps the
    /// <c>MeshRenderer.sharedMaterial</c> to the equipped skin's material.
    /// </summary>
    /// <remarks>
    /// Uses <c>sharedMaterial</c> deliberately: accessing <c>.material</c> would
    /// instance the material at runtime (heap alloc + broken batching). All balls
    /// (this prototype has one) show the same skin at a time, so the shared slot
    /// is correct and cheap.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class BallSkinApplier : MonoBehaviour
    {
        [SerializeField, Tooltip("Catalog used to resolve a skin id into its material.")]
        private BallSkinCatalogSO _catalog;

        [SerializeField, Tooltip("Listened-to: payload is the newly equipped skin id.")]
        private StringGameEventSO _onSkinEquipped;

        private MeshRenderer _meshRenderer;

        private void Awake()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            Debug.Assert(_catalog != null, $"{nameof(BallSkinApplier)} requires a {nameof(BallSkinCatalogSO)} reference.", this);
            Debug.Assert(_onSkinEquipped != null, $"{nameof(BallSkinApplier)} requires {nameof(_onSkinEquipped)}.", this);
        }

        private void OnEnable()
        {
            if (_onSkinEquipped != null) _onSkinEquipped.Register(HandleSkinEquipped);
        }

        private void OnDisable()
        {
            if (_onSkinEquipped != null) _onSkinEquipped.Unregister(HandleSkinEquipped);
        }

        private void HandleSkinEquipped(string skinId)
        {
            BallSkinSO skin = _catalog != null ? _catalog.GetById(skinId) : null;
            if (skin == null || skin.Material == null) return;
            _meshRenderer.sharedMaterial = skin.Material;
        }
    }
}
