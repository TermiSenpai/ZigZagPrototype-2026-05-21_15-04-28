using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Cosmetics
{
    /// <summary>
    /// Owns the look of the ball's <see cref="TrailRenderer"/>: assigns a
    /// guaranteed-valid runtime material in <see cref="Awake"/>, applies the
    /// project's authored width / time / vertex-distance defaults, and keeps
    /// the trail's start/end color aligned with the currently equipped
    /// <see cref="BallSkinSO"/>. Lives on the ball next to
    /// <see cref="BallSkinApplier"/>; both react to the same skin-equipped
    /// channel so the swap is atomic from the player's perspective.
    /// </summary>
    /// <remarks>
    /// The colorizer is authoritative over the trail's material because the
    /// alternative — picking <c>Default-Line.mat</c> manually in the inspector —
    /// is fragile: an empty slot or a material whose shader is missing renders
    /// as the magenta "no shader" placeholder. Building a tiny shared material
    /// from a guaranteed shader (same fallback cascade as <c>Gem</c> and
    /// <c>BallDeathBurst</c>) removes that failure mode entirely.
    ///
    /// The skin tint is sampled from the skin's <see cref="Material"/>:
    /// <c>_Color</c> (Built-in RP) is the only property read; the polyfill
    /// <c>Material.color</c> is used as a fallback for shaders without that
    /// property (URP/HDRP). The trail's end color is the same hue with alpha
    /// 0 so the tail fades smoothly through vertex-color interpolation.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BallTrailColorizer : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Trail renderer driven by this component. Its material, widths, time and vertex distance are overwritten in Awake.")]
        private TrailRenderer _trail;

        [SerializeField, Tooltip("Catalog used to resolve a skin id into its material.")]
        private BallSkinCatalogSO _catalog;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: payload is the newly equipped skin id.")]
        private StringGameEventSO _onSkinEquipped;

        [Header("Trail Appearance")]
        [SerializeField, Range(0.05f, 2f), Tooltip("How long (seconds) a trail vertex lives before fading out. Shorter reads as 'fast', longer as 'drag'.")]
        private float _trailTime = 0.25f;

        [SerializeField, Range(0.02f, 1f), Tooltip("Trail width at the ball end. Matches the ball's visual radius; the original Unity sphere primitive at scale 1 has radius 0.5, so values around 0.2 read cleanly without dominating the screen.")]
        private float _trailStartWidth = 0.2f;

        [SerializeField, Range(0f, 1f), Tooltip("Trail width at the fading end. 0 tapers to a point.")]
        private float _trailEndWidth = 0f;

        [SerializeField, Range(0.01f, 0.5f), Tooltip("Minimum distance between trail vertices. Smaller = smoother curves but more geometry.")]
        private float _trailMinVertexDistance = 0.05f;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        // Shared across every BallTrailColorizer instance so the runtime
        // material is allocated once per session — same pattern as Gem and
        // BallDeathBurst use for their burst materials.
        private static Material _sharedTrailMaterial;

        private void Awake()
        {
            Debug.Assert(_trail != null, $"{nameof(BallTrailColorizer)} requires a {nameof(TrailRenderer)} reference.", this);
            Debug.Assert(_catalog != null, $"{nameof(BallTrailColorizer)} requires a {nameof(BallSkinCatalogSO)} reference.", this);
            Debug.Assert(_onSkinEquipped != null, $"{nameof(BallTrailColorizer)} requires {nameof(_onSkinEquipped)}.", this);

            ApplyTrailMaterial();
            ApplyTrailAppearance();
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

        private void ApplyTrailMaterial()
        {
            if (_trail == null) return;
            Material mat = GetOrCreateTrailMaterial();
            if (mat == null) return;
            // sharedMaterial: a single Material allocation feeds every trail
            // instance and avoids per-renderer .material instancing.
            _trail.sharedMaterial = mat;
        }

        private void ApplyTrailAppearance()
        {
            if (_trail == null) return;
            _trail.time = _trailTime;
            _trail.startWidth = _trailStartWidth;
            _trail.endWidth = _trailEndWidth;
            _trail.minVertexDistance = _trailMinVertexDistance;
            _trail.autodestruct = false;
            _trail.emitting = true;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
        }

        private static Material GetOrCreateTrailMaterial()
        {
            if (_sharedTrailMaterial != null) return _sharedTrailMaterial;

            Shader shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Particles/Unlit");
            if (shader == null) shader = Shader.Find("Mobile/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning($"{nameof(BallTrailColorizer)}: no suitable shader found for the trail material; trail will render with the default error material.");
                return null;
            }

            _sharedTrailMaterial = new Material(shader) { name = "BallTrail (runtime)" };
            return _sharedTrailMaterial;
        }
    }
}
