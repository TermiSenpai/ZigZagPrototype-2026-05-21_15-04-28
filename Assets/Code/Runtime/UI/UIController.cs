using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.UI
{
    /// <summary>
    /// Switches Menu, HUD and GameOver panels on and off in response to lifecycle
    /// event channels. Knows nothing about <c>GameStateMachine</c>; the only
    /// outbound signal is the retry request raised when the Retry button is
    /// clicked, which the state machine listens to.
    /// </summary>
    /// <remarks>
    /// The Retry button wires its <c>onClick</c> to <see cref="OnRetryButtonClicked"/>
    /// through the inspector — that one UnityEvent call is acceptable because the
    /// alternative (a direct reference from UI to Core) would create a circular
    /// asmdef dependency, and the call cost is paid once per click, not per frame.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UIController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField, Tooltip("Root GameObject of the main menu. Active in Menu state.")]
        private GameObject _menuPanel;

        [SerializeField, Tooltip("Root GameObject of the in-game HUD. Active in Playing state.")]
        private GameObject _hudPanel;

        [SerializeField, Tooltip("Root GameObject of the Game Over panel. Active in GameOver state.")]
        private GameObject _gameOverPanel;

        [Header("Score Display")]
        [SerializeField, Tooltip("HUD text showing the current run's distance score during Playing.")]
        private TextMeshProUGUI _hudScoreText;

        [SerializeField, Tooltip("GameOver panel text showing the final distance score of the just-ended run.")]
        private TextMeshProUGUI _gameOverFinalScoreText;

        [SerializeField, Tooltip("GameOver and Menu text showing the persisted best distance score.")]
        private TextMeshProUGUI _bestScoreText;

        [SerializeField, Tooltip("GameObject toggled active when the just-ended run beat the previous best. Leave null if not used.")]
        private GameObject _newRecordBadge;

        [Header("Coins Display")]
        [SerializeField, Tooltip("HUD text showing coins collected during the current run (session counter, resets on retry).")]
        private TextMeshProUGUI _hudCoinsText;

        [SerializeField, Tooltip("GameOver panel text showing the player's total persistent coin wallet.")]
        [FormerlySerializedAs("_gameOverSessionCoinsText")]
        private TextMeshProUGUI _gameOverTotalCoinsText;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Fires when the run starts; switches Menu → HUD.")]
        private GameEventSO _onGameStarted;

        [SerializeField, Tooltip("Fires on death; switches HUD → GameOver.")]
        private GameEventSO _onGameOver;

        [SerializeField, Tooltip("Fires on retry; switches GameOver → HUD.")]
        private GameEventSO _onGameReset;

        [SerializeField, Tooltip("Listened-to: refreshes the HUD distance score text.")]
        private IntGameEventSO _onScoreChanged;

        [SerializeField, Tooltip("Listened-to: refreshes the best distance score text.")]
        private IntGameEventSO _onBestScoreChanged;

        [SerializeField, Tooltip("Listened-to: refreshes the HUD coin wallet text.")]
        private IntGameEventSO _onCoinsChanged;

        [SerializeField, Tooltip("Listened-to: refreshes the GameOver \"+N coins\" text.")]
        private IntGameEventSO _onSessionCoinsChanged;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised when the Retry button is clicked. The state machine listens.")]
        private GameEventSO _onRetryRequested;

        private int _lastKnownBest;
        private bool _newBestSeenInThisRun;

        private void Awake()
        {
            Debug.Assert(_menuPanel != null, $"{nameof(UIController)} requires a Menu panel.", this);
            Debug.Assert(_hudPanel != null, $"{nameof(UIController)} requires a HUD panel.", this);
            Debug.Assert(_gameOverPanel != null, $"{nameof(UIController)} requires a GameOver panel.", this);
            Debug.Assert(_onGameStarted != null, $"{nameof(UIController)} requires {nameof(_onGameStarted)}.", this);
            Debug.Assert(_onGameOver != null, $"{nameof(UIController)} requires {nameof(_onGameOver)}.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(UIController)} requires {nameof(_onGameReset)}.", this);
            Debug.Assert(_onRetryRequested != null, $"{nameof(UIController)} requires {nameof(_onRetryRequested)}.", this);
            Debug.Assert(_hudScoreText != null, $"{nameof(UIController)} requires {nameof(_hudScoreText)}.", this);
            Debug.Assert(_gameOverFinalScoreText != null, $"{nameof(UIController)} requires {nameof(_gameOverFinalScoreText)}.", this);
            Debug.Assert(_bestScoreText != null, $"{nameof(UIController)} requires {nameof(_bestScoreText)}.", this);
            Debug.Assert(_onScoreChanged != null, $"{nameof(UIController)} requires {nameof(_onScoreChanged)}.", this);
            Debug.Assert(_onBestScoreChanged != null, $"{nameof(UIController)} requires {nameof(_onBestScoreChanged)}.", this);
            Debug.Assert(_hudCoinsText != null, $"{nameof(UIController)} requires {nameof(_hudCoinsText)}.", this);
            Debug.Assert(_gameOverTotalCoinsText != null, $"{nameof(UIController)} requires {nameof(_gameOverTotalCoinsText)}.", this);
            Debug.Assert(_onCoinsChanged != null, $"{nameof(UIController)} requires {nameof(_onCoinsChanged)}.", this);
            Debug.Assert(_onSessionCoinsChanged != null, $"{nameof(UIController)} requires {nameof(_onSessionCoinsChanged)}.", this);
        }

        private void OnEnable()
        {
            if (_onGameStarted != null) _onGameStarted.Register(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Register(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
            if (_onScoreChanged != null) _onScoreChanged.Register(HandleScoreChanged);
            if (_onBestScoreChanged != null) _onBestScoreChanged.Register(HandleBestScoreChanged);
            if (_onCoinsChanged != null) _onCoinsChanged.Register(HandleCoinsChanged);
            if (_onSessionCoinsChanged != null) _onSessionCoinsChanged.Register(HandleSessionCoinsChanged);
        }

        private void OnDisable()
        {
            if (_onGameStarted != null) _onGameStarted.Unregister(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Unregister(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
            if (_onScoreChanged != null) _onScoreChanged.Unregister(HandleScoreChanged);
            if (_onBestScoreChanged != null) _onBestScoreChanged.Unregister(HandleBestScoreChanged);
            if (_onCoinsChanged != null) _onCoinsChanged.Unregister(HandleCoinsChanged);
            if (_onSessionCoinsChanged != null) _onSessionCoinsChanged.Unregister(HandleSessionCoinsChanged);
        }

        private void Start()
        {
            ShowMenu();
        }

        /// <summary>
        /// Invoked by the Retry button's <c>onClick</c> (configured in the inspector).
        /// Raises the retry-requested channel; the state machine handles the rest.
        /// </summary>
        public void OnRetryButtonClicked()
        {
            if (_onRetryRequested != null) _onRetryRequested.Raise();
        }

        private void HandleScoreChanged(int newScore)
        {
            if (_hudScoreText != null) _hudScoreText.text = $"Score: {newScore}";
            if (_gameOverFinalScoreText != null) _gameOverFinalScoreText.text = $"Score: {newScore}";
        }

        private void HandleBestScoreChanged(int newBest)
        {
            bool wasNewRecord = newBest > _lastKnownBest;
            _lastKnownBest = newBest;
            if (_bestScoreText != null) _bestScoreText.text = $"Best: {newBest}";

            if (!wasNewRecord) return;
            _newBestSeenInThisRun = true;

            // If we got here AFTER HandleGameOver (one of the two valid orderings),
            // the panel is already up; light the badge now.
            if (_newRecordBadge != null && _gameOverPanel != null && _gameOverPanel.activeSelf)
            {
                _newRecordBadge.SetActive(true);
            }
        }

        private void HandleCoinsChanged(int totalCoins)
        {
            if (_gameOverTotalCoinsText != null) _gameOverTotalCoinsText.text = $"Coins: {totalCoins}";
        }

        private void HandleSessionCoinsChanged(int sessionCoins)
        {
            if (_hudCoinsText != null) _hudCoinsText.text = $"+{sessionCoins}";
        }

        private void HandleGameStarted()
        {
            _newBestSeenInThisRun = false;
            if (_newRecordBadge != null) _newRecordBadge.SetActive(false);
            ShowHud();
        }

        private void HandleGameOver()
        {
            ShowGameOver();
            // If we got here AFTER HandleBestScoreChanged (the other valid ordering),
            // the flag is already true; light the badge now that the panel is up.
            if (_newBestSeenInThisRun && _newRecordBadge != null)
            {
                _newRecordBadge.SetActive(true);
            }
        }

        private void HandleGameReset()
        {
            _newBestSeenInThisRun = false;
            if (_newRecordBadge != null) _newRecordBadge.SetActive(false);
            ShowMenu();
        }

        private void ShowMenu() => SetPanels(menu: true, hud: false, gameOver: false);

        private void ShowHud() => SetPanels(menu: false, hud: true, gameOver: false);

        private void ShowGameOver() => SetPanels(menu: false, hud: false, gameOver: true);

        private void SetPanels(bool menu, bool hud, bool gameOver)
        {
            if (_menuPanel != null) _menuPanel.SetActive(menu);
            if (_hudPanel != null) _hudPanel.SetActive(hud);
            if (_gameOverPanel != null) _gameOverPanel.SetActive(gameOver);
        }
    }
}
