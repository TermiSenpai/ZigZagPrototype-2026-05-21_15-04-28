using UnityEngine;
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

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Fires when the run starts; switches Menu → HUD.")]
        private GameEventSO _onGameStarted;

        [SerializeField, Tooltip("Fires on death; switches HUD → GameOver.")]
        private GameEventSO _onGameOver;

        [SerializeField, Tooltip("Fires on retry; switches GameOver → HUD.")]
        private GameEventSO _onGameReset;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised when the Retry button is clicked. The state machine listens.")]
        private GameEventSO _onRetryRequested;

        private void Awake()
        {
            Debug.Assert(_menuPanel != null, $"{nameof(UIController)} requires a Menu panel.", this);
            Debug.Assert(_hudPanel != null, $"{nameof(UIController)} requires a HUD panel.", this);
            Debug.Assert(_gameOverPanel != null, $"{nameof(UIController)} requires a GameOver panel.", this);
            Debug.Assert(_onGameStarted != null, $"{nameof(UIController)} requires {nameof(_onGameStarted)}.", this);
            Debug.Assert(_onGameOver != null, $"{nameof(UIController)} requires {nameof(_onGameOver)}.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(UIController)} requires {nameof(_onGameReset)}.", this);
            Debug.Assert(_onRetryRequested != null, $"{nameof(UIController)} requires {nameof(_onRetryRequested)}.", this);
        }

        private void OnEnable()
        {
            if (_onGameStarted != null) _onGameStarted.Register(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Register(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            if (_onGameStarted != null) _onGameStarted.Unregister(HandleGameStarted);
            if (_onGameOver != null) _onGameOver.Unregister(HandleGameOver);
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
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

        private void HandleGameStarted() => ShowHud();

        private void HandleGameOver() => ShowGameOver();

        private void HandleGameReset() => ShowMenu();

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
