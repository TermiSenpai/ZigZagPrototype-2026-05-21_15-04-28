using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Scoring
{
    /// <summary>
    /// Accumulates the current run's score (gem points + distance progress), persists
    /// the all-time best to <see cref="PlayerPrefs"/>, and broadcasts both via SO event
    /// channels so the UI never holds a reference to this component.
    /// </summary>
    /// <remarks>
    /// Distance is computed every frame from the ball's transform but only raises
    /// <c>SO_OnScoreChanged</c> when the integer total moves — gem pickups are rare
    /// enough to always raise. The origin for distance is the path start position
    /// taken from <see cref="GameConfigSO.PathStartPosition"/>, so the progress axis
    /// matches what the <c>PathGenerator</c> uses for its buffers.
    ///
    /// Persistence: <c>PlayerPrefs.GetInt("BestScore", 0)</c> in <c>Awake</c>;
    /// <c>SetInt + Save</c> in <see cref="SaveBestIfHigher"/> (called on GameOver).
    /// ADR-003 in zigzag_architecture.md picks PlayerPrefs over a file-based store
    /// because a single int per device is the whole persistence story for the prototype.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScoreManager : MonoBehaviour
    {
        private const string BestScorePrefKey = "BestScore";

        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of distance multiplier and path start position.")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Transform of the ball; distance progress is computed from this.")]
        private Transform _ballTransform;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: each raise adds the payload to current score.")]
        private IntGameEventSO _onGemCollected;

        [SerializeField, Tooltip("Listened-to: starts distance tracking.")]
        private GameEventSO _onGameStarted;

        [SerializeField, Tooltip("Listened-to: stops distance tracking and persists best if improved.")]
        private GameEventSO _onGameOver;

        [SerializeField, Tooltip("Listened-to: resets current score back to zero (best is preserved).")]
        private GameEventSO _onGameReset;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised whenever the integer score changes.")]
        private IntGameEventSO _onScoreChanged;

        [SerializeField, Tooltip("Raised on boot (loaded best) and whenever the best is overwritten.")]
        private IntGameEventSO _onBestScoreChanged;

        private static readonly Vector3 GlobalForward = new Vector3(-1f, 0f, 1f).normalized;

        public int CurrentScore { get; private set; }
        public int BestScore { get; private set; }

        private int _gemScore;
        private int _distanceScore;
        private bool _isTracking;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(ScoreManager)} requires a {nameof(GameConfigSO)} reference.", this);
            Debug.Assert(_ballTransform != null, $"{nameof(ScoreManager)} requires a ball Transform reference.", this);
            Debug.Assert(_onGemCollected != null, $"{nameof(ScoreManager)} requires {nameof(_onGemCollected)}.", this);
            Debug.Assert(_onGameStarted != null, $"{nameof(ScoreManager)} requires {nameof(_onGameStarted)}.", this);
            Debug.Assert(_onGameOver != null, $"{nameof(ScoreManager)} requires {nameof(_onGameOver)}.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(ScoreManager)} requires {nameof(_onGameReset)}.", this);
            Debug.Assert(_onScoreChanged != null, $"{nameof(ScoreManager)} requires {nameof(_onScoreChanged)}.", this);
            Debug.Assert(_onBestScoreChanged != null, $"{nameof(ScoreManager)} requires {nameof(_onBestScoreChanged)}.", this);

            BestScore = PlayerPrefs.GetInt(BestScorePrefKey, 0);
        }

        private void OnEnable()
        {
            if (_onGemCollected != null) _onGemCollected.Register(HandleGemCollected);
            if (_onGameStarted != null) _onGameStarted.Register(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Register(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            if (_onGemCollected != null) _onGemCollected.Unregister(HandleGemCollected);
            if (_onGameStarted != null) _onGameStarted.Unregister(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Unregister(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
        }

        private void Start()
        {
            // Broadcast loaded best so the menu/HUD can paint it before any run starts.
            _onBestScoreChanged.Raise(BestScore);
            _onScoreChanged.Raise(CurrentScore);
        }

        private void Update()
        {
            if (!_isTracking || _config == null || _ballTransform == null) return;

            int newDistanceScore = ScoreCalculator.ComputeDistanceScore(
                _ballTransform.position,
                _config.PathStartPosition,
                GlobalForward,
                _config.DistanceMultiplier);

            if (newDistanceScore == _distanceScore) return;

            _distanceScore = newDistanceScore;
            RecomputeAndBroadcast();
        }

        private void HandleGemCollected(int gemValue)
        {
            _gemScore += gemValue;
            RecomputeAndBroadcast();
        }

        private void HandleGameStarted()
        {
            _isTracking = true;
        }

        private void HandleGameOver()
        {
            _isTracking = false;
            SaveBestIfHigher();
        }

        private void HandleGameReset()
        {
            _isTracking = false;
            _gemScore = 0;
            _distanceScore = 0;
            RecomputeAndBroadcast();
        }

        private void RecomputeAndBroadcast()
        {
            int total = _gemScore + _distanceScore;
            if (total == CurrentScore) return;
            CurrentScore = total;
            _onScoreChanged.Raise(CurrentScore);
        }

        private void SaveBestIfHigher()
        {
            if (CurrentScore <= BestScore) return;
            BestScore = CurrentScore;
            PlayerPrefs.SetInt(BestScorePrefKey, BestScore);
            PlayerPrefs.Save();
            _onBestScoreChanged.Raise(BestScore);
        }
    }
}
