using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    /// <summary>
    /// Keeps a <see cref="TrailRenderer"/>'s color aligned with the currently
    /// equipped <see cref="BallSkinSO"/>. Lives on the ball next to
    /// <see cref="BallSkinApplier"/>; both react to the same skin-equipped
    /// channel so the swap is atomic from the player's perspective.
    /// </summary>
    /// <remarks>
    /// The trail color is sampled from the skin's <see cref="Material"/>:
    /// <c>_Color</c> (Built-in RP) is the only property read. The end color is
    /// the same hue with alpha 0 so the tail fades smoothly. Width, time and
    /// other curve parameters are authored on the <see cref="TrailRenderer"/>
    /// component itself in the Inspector and are not touched here.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BallTrailColorizer : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Trail renderer whose start/end color tracks the equipped skin. Authored on the ball GameObject; widths and curves are configured directly on the component.")]
        private TrailRenderer _trail;

        [SerializeField, Tooltip("Catalog used to resolve a skin id into its material.")]
        private BallSkinCatalogSO _catalog;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: payload is the newly equipped skin id.")]
        private StringGameEventSO _onSkinEquipped;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private void Awake()
        {
            Debug.Assert(_trail != null, $"{nameof(BallTrailColorizer)} requires a {nameof(TrailRenderer)} reference.", this);
            Debug.Assert(_catalog != null, $"{nameof(BallTrailColorizer)} requires a {nameof(BallSkinCatalogSO)} reference.", this);
            Debug.Assert(_onSkinEquipped != null, $"{nameof(BallTrailColorizer)} requires {nameof(_onSkinEquipped)}.", this);
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
            if (_trail == null || _catalog == null) return;
            BallSkinSO skin = _catalog.GetById(skinId);
            if (skin == null || skin.Material == null) return;

            Color tint = skin.Material.HasProperty(ColorProperty)
                ? skin.Material.GetColor(ColorProperty)
                : skin.Material.color;

            Color start = new Color(tint.r, tint.g, tint.b, 1f);
            Color end = new Color(tint.r, tint.g, tint.b, 0f);
            _trail.startColor = start;
            _trail.endColor = end;
        }
    }
}
