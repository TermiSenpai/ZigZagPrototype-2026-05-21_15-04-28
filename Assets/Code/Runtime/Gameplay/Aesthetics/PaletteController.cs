using System.Collections;
using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.World;

namespace ZigZag.Runtime.Gameplay.Aesthetics
{
    /// <summary>
    /// Listens to score changes; every <see cref="PaletteRulesSO.ScoreThresholdStep"/>
    /// points, swaps the platform material color and the camera clear color to a fresh
    /// complementary pair, lerping over <see cref="PaletteRulesSO.TransitionSeconds"/>.
    /// Resets to the initial palette on game reset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PaletteController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Camera whose background clear color is driven by the palette.")]
        private Camera _camera;

        [SerializeField, Tooltip("Platform pool whose RuntimeMaterial is recolored on each palette swap.")]
        private PlatformPool _platformPool;

        [SerializeField, Tooltip("Palette configuration: threshold step, transition duration, HSV ranges, initial colors, shader property name.")]
        private PaletteRulesSO _rules;

        [SerializeField, Tooltip("Source of GenerationSeed — mirrors the seed used by PathGenerator so palette randomness is deterministic when seed != 0.")]
        private GameConfigSO _config;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: integer score that drives threshold detection.")]
        private IntGameEventSO _onScoreChanged;

        [SerializeField, Tooltip("Listened-to: snaps colors back to the initial palette and re-seeds the RNG.")]
        private GameEventSO _onGameReset;

        [SerializeField, Tooltip("Listened-to: defensively resets _lastThresholdReached to 0 in case event order differs from HandleGameReset.")]
        private GameEventSO _onGameStarted;

        // ── Runtime state ──────────────────────────────────────────────────────────

        private System.Random _rng;
        private int _lastThresholdReached;

        // Sentinel -1f means "no previous hue" so the sampler skips the min-distance
        // check on the very first call.
        private float _currentPrimaryHue = -1f;

        private Coroutine _transition;
        private Color _currentPlatformColor;
        private Color _currentCameraColor;

        // Cached Shader.PropertyToID — NOT static because the property name comes from
        // the SO and could differ per project configuration.
        private int _colorPropertyId;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            Debug.Assert(_camera != null, $"{nameof(PaletteController)} requires {nameof(_camera)}.", this);
            Debug.Assert(_platformPool != null, $"{nameof(PaletteController)} requires {nameof(_platformPool)}.", this);
            Debug.Assert(_rules != null, $"{nameof(PaletteController)} requires {nameof(_rules)}.", this);
            Debug.Assert(_config != null, $"{nameof(PaletteController)} requires {nameof(_config)}.", this);
            Debug.Assert(_onScoreChanged != null, $"{nameof(PaletteController)} requires {nameof(_onScoreChanged)}.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(PaletteController)} requires {nameof(_onGameReset)}.", this);
            Debug.Assert(_onGameStarted != null, $"{nameof(PaletteController)} requires {nameof(_onGameStarted)}.", this);

            _colorPropertyId = Shader.PropertyToID(_rules.ColorShaderProperty);
            _rng = CreateRandom();
            _currentPlatformColor = _rules.InitialPlatformColor;
            _currentCameraColor = _rules.InitialCameraColor;
            ApplyColors(_currentPlatformColor, _currentCameraColor);
        }

        private void OnEnable()
        {
            if (_onScoreChanged != null) _onScoreChanged.Register(HandleScoreChanged);
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
            if (_onGameStarted != null) _onGameStarted.Register(HandleGameStarted);
        }

        private void OnDisable()
        {
            if (_onScoreChanged != null) _onScoreChanged.Unregister(HandleScoreChanged);
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
            if (_onGameStarted != null) _onGameStarted.Unregister(HandleGameStarted);

            if (_transition != null)
            {
                StopCoroutine(_transition);
                _transition = null;
            }

            if (_rules != null)
            {
                _currentPlatformColor = _rules.InitialPlatformColor;
                _currentCameraColor = _rules.InitialCameraColor;
                ApplyColors(_currentPlatformColor, _currentCameraColor);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private System.Random CreateRandom()
        {
            // seed == 0 → fresh seed every run (System.Environment.TickCount); any other value → deterministic.
            int seed = _config != null && _config.GenerationSeed != 0
                ? _config.GenerationSeed
                : System.Environment.TickCount;
            return new System.Random(seed);
        }

        // ── Event handlers ─────────────────────────────────────────────────────────

        private void HandleScoreChanged(int score)
        {
            if (_rules == null) return;
            int newThreshold = score / _rules.ScoreThresholdStep;
            if (newThreshold <= _lastThresholdReached) return;
            _lastThresholdReached = newThreshold;
            TriggerSwap();
        }

        private void HandleGameStarted()
        {
            _lastThresholdReached = 0;
        }

        private void HandleGameReset()
        {
            if (_rules == null) return;
            if (_transition != null)
            {
                StopCoroutine(_transition);
                _transition = null;
            }
            _lastThresholdReached = 0;
            _currentPrimaryHue = -1f;
            _rng = CreateRandom();
            _currentPlatformColor = _rules.InitialPlatformColor;
            _currentCameraColor = _rules.InitialCameraColor;
            ApplyColors(_currentPlatformColor, _currentCameraColor);
        }

        // ── Palette swap ───────────────────────────────────────────────────────────

        private void TriggerSwap()
        {
            if (_platformPool == null || _platformPool.RuntimeMaterial == null || _camera == null || _rules == null) return;
            var (platform, camera, primaryHue) = PaletteSampler.Sample(_rng, _rules, _currentPrimaryHue);
            _currentPrimaryHue = primaryHue;
            if (_transition != null)
            {
                StopCoroutine(_transition);
            }
            _transition = StartCoroutine(LerpRoutine(_currentPlatformColor, _currentCameraColor, platform, camera, _rules.TransitionSeconds));
        }

        private IEnumerator LerpRoutine(Color fromPlatform, Color fromCamera, Color toPlatform, Color toCamera, float duration)
        {
            if (duration <= 0f)
            {
                ApplyColors(toPlatform, toCamera);
                _currentPlatformColor = toPlatform;
                _currentCameraColor = toCamera;
                _transition = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                Color p = Color.Lerp(fromPlatform, toPlatform, u);
                Color c = Color.Lerp(fromCamera, toCamera, u);
                ApplyColors(p, c);
                _currentPlatformColor = p;
                _currentCameraColor = c;
                yield return null;
            }

            // Exact landing on the target to defeat float drift.
            ApplyColors(toPlatform, toCamera);
            _currentPlatformColor = toPlatform;
            _currentCameraColor = toCamera;
            _transition = null;
        }

        private void ApplyColors(Color platform, Color camera)
        {
            if (_platformPool != null && _platformPool.RuntimeMaterial != null)
            {
                _platformPool.RuntimeMaterial.SetColor(_colorPropertyId, platform);
            }
            if (_camera != null)
            {
                _camera.backgroundColor = camera;
            }
        }
    }
}
