using System.Collections;
using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Collectibles
{
    /// <summary>
    /// A pickup that rewards the player with <see cref="Value"/> coins (banked by
    /// <c>CoinsWallet</c> as persistent currency, not added to the run score). The
    /// instance is pooled; it returns itself to the pool on collection rather than
    /// being destroyed.
    /// </summary>
    /// <remarks>
    /// Trigger detection relies on a kinematic <c>Rigidbody</c> on the gem prefab —
    /// the ball is Rigidbody-free (ADR-001) so the gem must be the "moving" side of
    /// the contact for Unity to dispatch <c>OnTriggerEnter</c>.
    ///
    /// The owning <see cref="GemPool"/> is injected via <see cref="Initialize"/>
    /// (called by <c>GemSpawner</c>) so this script never has to call back into the
    /// pool component by reference.
    ///
    /// Game feel: on collection the gem hides its mesh and disables its collider
    /// immediately, plays a one-shot particle burst built procedurally in
    /// <see cref="Awake"/>, and only returns to the pool once the burst has had
    /// time to fade. The burst is a child GameObject so it inherits the gem's
    /// world position at pickup time; it simulates in world space so the particles
    /// do not follow the gem when it is recycled.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class Gem : MonoBehaviour
    {
        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised with the gem's point value when the ball enters its trigger.")]
        private IntGameEventSO _onGemCollected;

        [SerializeField, Tooltip("Tag of the GameObject considered the ball. Pickup ignores any other collider.")]
        private string _ballTag = "Player";

        [Header("Pickup Burst")]
        [SerializeField, Tooltip("Tint applied to the pickup particle burst. Alpha is driven by the lifetime fade and is ignored here.")]
        private Color _burstColor = new Color(1f, 0.92f, 0.4f, 1f);

        [SerializeField, Range(4, 64), Tooltip("Number of particles emitted in the pickup burst.")]
        private int _burstParticleCount = 18;

        [SerializeField, Range(0.05f, 1.5f), Tooltip("Particle lifetime in seconds. Also the delay before the gem returns to its pool, so the burst is never cut off.")]
        private float _burstLifetime = 0.45f;

        [SerializeField, Range(0.5f, 12f), Tooltip("Initial outward speed of each particle, in units/second.")]
        private float _burstSpeed = 4.5f;

        [SerializeField, Range(0.02f, 0.5f), Tooltip("Starting size of each particle in world units.")]
        private float _burstParticleSize = 0.14f;

        public int Value { get; private set; }

        private GemPool _owningPool;
        private bool _collected;

        private MeshRenderer _renderer;
        private Collider _collider;
        private ParticleSystem _burst;
        private Coroutine _releaseRoutine;

        // Shared across every Gem so the burst material isn't reallocated per pickup
        // and per instance. Created lazily on the first Awake that needs it.
        private static Material _sharedBurstMaterial;

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

            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;

            _renderer = GetComponent<MeshRenderer>();
            _burst = BuildPickupBurst();
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

            // Defensive: a gem could in theory be re-pulled from the pool while the
            // previous release coroutine is still pending. Cancel it and restore the
            // visible state so the next pickup is clean.
            if (_releaseRoutine != null)
            {
                StopCoroutine(_releaseRoutine);
                _releaseRoutine = null;
            }
            if (_renderer != null) _renderer.enabled = true;
            if (_collider != null) _collider.enabled = true;
        }

        private void OnDisable()
        {
            if (_releaseRoutine != null)
            {
                StopCoroutine(_releaseRoutine);
                _releaseRoutine = null;
            }
            if (_burst != null)
            {
                _burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_collected) return;
            if (!other.CompareTag(_ballTag)) return;

            _collected = true;
            _onGemCollected.Raise(Value);

            // Hide the gem instantly so the visual "pop" is the burst, not the gem
            // lingering for a frame. The collider goes off too — a second trigger on
            // the same gem during the burst cooldown would re-raise the coin event.
            if (_renderer != null) _renderer.enabled = false;
            if (_collider != null) _collider.enabled = false;

            if (_burst != null && isActiveAndEnabled)
            {
                _burst.Play(true);
                _releaseRoutine = StartCoroutine(ReleaseAfterBurst());
                return;
            }

            ReleaseToPool();
        }

        private IEnumerator ReleaseAfterBurst()
        {
            yield return new WaitForSeconds(_burstLifetime);
            _releaseRoutine = null;
            ReleaseToPool();
        }

        private void ReleaseToPool()
        {
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

        /// <summary>
        /// Builds a one-shot particle burst as a child GameObject so the gem prefab
        /// stays untouched. Configured entirely in code: sphere shape, world-space
        /// simulation, soft alpha fade, single burst on <c>Play</c>. Uses Unity's
        /// built-in <c>Default-Particle.mat</c> so no asset reference is required.
        /// </summary>
        private ParticleSystem BuildPickupBurst()
        {
            GameObject host = new GameObject("PickupBurst");
            host.transform.SetParent(transform, worldPositionStays: false);
            host.transform.localPosition = Vector3.zero;

            ParticleSystem ps = host.AddComponent<ParticleSystem>();

            // AddComponent leaves the system playing under its default settings; force
            // it idle before applying our own configuration so we never see a stray
            // burst on the first frame after instantiation.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.2f;
            main.startLifetime = _burstLifetime;
            main.startSpeed = _burstSpeed;
            main.startSize = _burstParticleSize;
            main.startColor = _burstColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // Local scaling so the prefab's 0.4 scale doesn't shrink the burst.
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.gravityModifier = 0.4f;
            main.maxParticles = Mathf.Max(_burstParticleCount * 2, 32);

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
            shape.radius = 0.05f;

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

        /// <summary>
        /// Returns a shared, lazily-created material for the pickup burst. Prefers
        /// the Built-in RP's particle shaders; falls back to <c>Sprites/Default</c>
        /// which is guaranteed to exist in every Unity install. Cached statically so
        /// every gem points to the same material — one allocation per session, no
        /// per-pickup churn.
        /// </summary>
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
                Debug.LogWarning($"{nameof(Gem)}: no suitable shader found for the pickup burst material; burst will render with the default error material.");
                return null;
            }

            _sharedBurstMaterial = new Material(shader) { name = "GemPickupBurst (runtime)" };
            return _sharedBurstMaterial;
        }
    }
}
