using UnityEngine;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Audio
{
    /// <summary>
    /// Passive presentation-layer listener that plays one-shot SFX in response to
    /// ScriptableObject event channels. Has no direct references to gameplay
    /// actors — it knows the channels and nothing else, so it can be enabled,
    /// disabled or replaced without touching the rest of the game.
    /// </summary>
    /// <remarks>
    /// Architecture §7.15 and ADR-004: audio is a pure subscriber. Adding a new
    /// SFX hook means a new channel field + a new <c>PlayOneShot</c> handler,
    /// never a code change in the system that triggers it.
    ///
    /// One <see cref="AudioSource"/> is grabbed in <c>Awake</c>; SFX go through
    /// <see cref="AudioSource.PlayOneShot(AudioClip, float)"/> so overlapping
    /// events (e.g. a flip during a gem pickup) do not cut each other off.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioManager : MonoBehaviour
    {
        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Raised by BallController whenever the ball flips axis.")]
        private GameEventSO _onDirectionChanged;

        [SerializeField, Tooltip("Raised by Gem on pickup. Payload (coin value) is unused here — only the trigger matters.")]
        private IntGameEventSO _onGemCollected;

        [SerializeField, Tooltip("Raised by GameStateMachine after the death freeze-frame elapses.")]
        private GameEventSO _onGameOver;

        [Header("Clips")]
        [SerializeField, Tooltip("Short click played each time the ball flips direction. ~50ms recommended.")]
        private AudioClip _directionFlipClip;

        [SerializeField, Tooltip("Tinkling pickup sound played when a gem is collected. ~200ms recommended.")]
        private AudioClip _gemCollectedClip;

        [SerializeField, Tooltip("Heavy impact played when the ball falls and the run ends. ~300ms recommended.")]
        private AudioClip _gameOverClip;

        [Header("Volume")]
        [SerializeField, Range(0f, 1f), Tooltip("Per-clip volume scale for the direction flip SFX.")]
        private float _directionFlipVolume = 0.7f;

        [SerializeField, Range(0f, 1f), Tooltip("Per-clip volume scale for the gem pickup SFX.")]
        private float _gemCollectedVolume = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("Per-clip volume scale for the death SFX.")]
        private float _gameOverVolume = 1f;

        private AudioSource _source;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
        }

        private void OnEnable()
        {
            if (_onDirectionChanged != null) _onDirectionChanged.Register(HandleDirectionChanged);
            if (_onGemCollected != null) _onGemCollected.Register(HandleGemCollected);
            if (_onGameOver != null) _onGameOver.Register(HandleGameOver);
        }

        private void OnDisable()
        {
            if (_onDirectionChanged != null) _onDirectionChanged.Unregister(HandleDirectionChanged);
            if (_onGemCollected != null) _onGemCollected.Unregister(HandleGemCollected);
            if (_onGameOver != null) _onGameOver.Unregister(HandleGameOver);
        }

        private void HandleDirectionChanged() => PlayOneShot(_directionFlipClip, _directionFlipVolume);
        private void HandleGemCollected(int _) => PlayOneShot(_gemCollectedClip, _gemCollectedVolume);
        private void HandleGameOver() => PlayOneShot(_gameOverClip, _gameOverVolume);

        private void PlayOneShot(AudioClip clip, float volume)
        {
            if (clip == null) return;
            _source.PlayOneShot(clip, volume);
        }
    }
}
