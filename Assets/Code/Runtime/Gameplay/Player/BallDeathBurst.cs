using UnityEngine;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.Cosmetics;

namespace ZigZag.Runtime.Gameplay.Player
{
    /// <summary>
    /// One-shot particle burst played when the ball falls off the path. Built
    /// procedurally in <see cref="Awake"/> so the prefab doesn't carry a
    /// per-instance <see cref="ParticleSystem"/> component, mirroring the
    /// pattern used by <c>Gem</c> for pickup feedback.
    /// </summary>
    /// <remarks>
    /// Lives on the same GameObject as <see cref="BallController"/> and
    /// subscribes to its C# <see cref="BallController.OnFell"/> event directly
    /// (same assembly — no SO channel needed). The burst is a child of this
    /// transform and simulates in world space, so it stays put at the impact
    /// point while the ball continues its visual fall through the
    /// <see cref="Data.GameConfigSO.FreezeFrameOnDeathSeconds"/> window.
    ///
    /// The burst tint follows the equipped skin via the same
    /// <see cref="_onSkinEquipped"/> channel used by <see cref="BallSkinApplier"/>
    /// and <c>BallTrailColorizer</c>, so the death feedback stays visually
    /// consistent with the ball and its trail. The inspector-authored
    /// <see cref="_burstColor"/> survives as the fallback used until the first
    /// skin-equipped event fires.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallController))]
    public sealed class BallDeathBurst : MonoBehaviour
    {
        [Header("Burst Tuning")]
        [SerializeField, Tooltip("Fallback tint used until a skin-equipped event arrives. White-to-orange contrasts cleanly against every skin and the cycling palette.")]
        private Color _burstColor = new Color(1f, 0.65f, 0.25f, 1f);

        [SerializeField, Range(8, 96), Tooltip("Number of particles emitted on death.")]
        private int _burstParticleCount = 36;

        [SerializeField, Range(0.1f, 2f), Tooltip("Lifetime of each particle in seconds.")]
        private float _burstLifetime = 0.65f;

        [SerializeField, Range(1f, 16f), Tooltip("Initial outward speed of each particle, units/second.")]
        private float _burstSpeed = 7f;

        [SerializeField, Range(0.05f, 0.6f), Tooltip("Starting size of each particle in world units.")]
        private float _burstParticleSize = 0.18f;

        [Header("Skin Sync (Optional)")]
        [SerializeField, Tooltip("Catalog used to resolve a skin id into its material. Leave empty to keep the authored burst color.")]
        private BallSkinCatalogSO _catalog;

        [SerializeField, Tooltip("Listened-to: payload is the newly equipped skin id. Leave empty to keep the authored burst color.")]
        private StringGameEventSO _onSkinEquipped;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private BallController _ball;
        private ParticleSystem _burst;

        // Shared across every BallDeathBurst instance (the prototype has one
        // ball, so in practice this is the only ball; statics still avoid the
        // per-instance Material alloc if anyone scenes-up more balls later).
        private static Material _sharedBurstMaterial;

        private void Awake()
        {
            _ball = GetComponent<BallController>();
            _burst = BuildDeathBurst();
        }

        private void OnEnable()
        {
            if (_ball != null) _ball.OnFell += HandleFell;
            if (_onSkinEquipped != null) _onSkinEquipped.Register(HandleSkinEquipped);
        }

        private void OnDisable()
        {
            if (_ball != null) _ball.OnFell -= HandleFell;
            if (_onSkinEquipped != null) _onSkinEquipped.Unregister(HandleSkinEquipped);
            if (_burst != null) _burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void HandleSkinEquipped(string skinId)
        {
            if (_burst == null || _catalog == null) return;
            BallSkinSO skin = _catalog.GetById(skinId);
            if (skin == null || skin.Material == null) return;

            Color tint = skin.Material.HasProperty(ColorProperty)
                ? skin.Material.GetColor(ColorProperty)
                : skin.Material.color;

            // MainModule is a struct wrapper; reassigning its startColor writes
            // back through to the underlying ParticleSystem.
            ParticleSystem.MainModule main = _burst.main;
            main.startColor = new Color(tint.r, tint.g, tint.b, 1f);
        }

        private void HandleFell()
        {
            if (_burst == null) return;
            // Snap the burst host to the ball's impact position so the burst
            // is anchored where the ball left the path, not where the ball
            // ends up after the freeze-frame.
            _burst.transform.position = transform.position;
            _burst.Play(true);
        }

        private ParticleSystem BuildDeathBurst()
        {
            GameObject host = new GameObject("DeathBurst");
            host.transform.SetParent(transform, worldPositionStays: false);
            host.transform.localPosition = Vector3.zero;

            ParticleSystem ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.25f;
            main.startLifetime = _burstLifetime;
            main.startSpeed = _burstSpeed;
            main.startSize = _burstParticleSize;
            main.startColor = _burstColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.gravityModifier = 0.6f;
            main.maxParticles = Mathf.Max(_burstParticleCount * 2, 64);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)_burstParticleCount)
            });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fade;

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = GetOrCreateBurstMaterial();
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ps;
        }

        private static Material GetOrCreateBurstMaterial()
        {
            if (_sharedBurstMaterial != null) return _sharedBurstMaterial;

            Shader shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Particles/Unlit");
            if (shader == null) shader = Shader.Find("Mobile/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning($"{nameof(BallDeathBurst)}: no suitable shader found for the death burst material; burst will render with the default error material.");
                return null;
            }

            _sharedBurstMaterial = new Material(shader) { name = "BallDeathBurst (runtime)" };
            return _sharedBurstMaterial;
        }
    }
}
