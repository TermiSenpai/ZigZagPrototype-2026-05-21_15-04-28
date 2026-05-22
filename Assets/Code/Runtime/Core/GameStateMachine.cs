using UnityEngine;
using ZigZag.Runtime.Events;
using ZigZag.Runtime.Gameplay.Player;
using ZigZag.Runtime.Input;

namespace ZigZag.Runtime.Core
{
    /// <summary>
    /// Owns the Menu → Playing → GameOver → Playing lifecycle. Routes the single
    /// tap action based on the current state, drives the ball directly, and
    /// raises ScriptableObject channels for other systems (UI, audio, scoring)
    /// to react to lifecycle transitions.
    /// </summary>
    /// <remarks>
    /// ADR-010 in zigzag_architecture.md rejects both static GameEvents and
    /// singleton MonoBehaviours: this component lives in the scene, its lifetime
    /// matches the scene, and it holds no global state.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameStateMachine : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Captures the single tap action used to start the game and flip direction.")]
        private InputHandler _inputHandler;

        [SerializeField, Tooltip("The ball driven by this state machine.")]
        private BallController _ball;

        [SerializeField, Tooltip("Position the ball is teleported to on boot and on retry.")]
        private Transform _ballSpawnPoint;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Raised when a run starts (Menu → Playing or Retry → Playing).")]
        private GameEventSO _onGameStarted;

        [SerializeField, Tooltip("Raised when the ball falls and the run ends (Playing → GameOver).")]
        private GameEventSO _onGameOver;

        [SerializeField, Tooltip("Raised when the player retries (GameOver → Playing). UI listens to hide the GameOver panel.")]
        private GameEventSO _onGameReset;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to channel; UI raises it when the player clicks Retry.")]
        private GameEventSO _onRetryRequested;

        public GameState CurrentState { get; private set; } = GameState.Menu;

        private void Awake()
        {
            Debug.Assert(_inputHandler != null, $"{nameof(GameStateMachine)} requires an {nameof(InputHandler)} reference.", this);
            Debug.Assert(_ball != null, $"{nameof(GameStateMachine)} requires a {nameof(BallController)} reference.", this);
            Debug.Assert(_ballSpawnPoint != null, $"{nameof(GameStateMachine)} requires a spawn point Transform.", this);
            Debug.Assert(_onGameStarted != null, $"{nameof(GameStateMachine)} requires an {nameof(_onGameStarted)} channel.", this);
            Debug.Assert(_onGameOver != null, $"{nameof(GameStateMachine)} requires an {nameof(_onGameOver)} channel.", this);
            Debug.Assert(_onGameReset != null, $"{nameof(GameStateMachine)} requires an {nameof(_onGameReset)} channel.", this);
            Debug.Assert(_onRetryRequested != null, $"{nameof(GameStateMachine)} requires an {nameof(_onRetryRequested)} channel.", this);
        }

        private void OnEnable()
        {
            if (_inputHandler != null) _inputHandler.OnTapped += HandleTap;
            if (_ball != null) _ball.OnFell += HandleBallFell;
            if (_onRetryRequested != null) _onRetryRequested.Register(HandleRetryRequested);
        }

        private void OnDisable()
        {
            if (_inputHandler != null) _inputHandler.OnTapped -= HandleTap;
            if (_ball != null) _ball.OnFell -= HandleBallFell;
            if (_onRetryRequested != null) _onRetryRequested.Unregister(HandleRetryRequested);
        }

        private void Start()
        {
            // Boot in Menu: park the ball at spawn so it is visible but idle.
            if (_ball != null && _ballSpawnPoint != null)
            {
                _ball.ResetTo(_ballSpawnPoint.position);
            }
        }

        private void HandleTap()
        {
            switch (CurrentState)
            {
                case GameState.Menu:
                    StartGame();
                    break;
                case GameState.Playing:
                    if (_ball != null) _ball.FlipDirection();
                    break;
                case GameState.GameOver:
                    // Tap is intentionally ignored; the Retry button is the only input.
                    break;
            }
        }

        private void HandleBallFell()
        {
            if (CurrentState != GameState.Playing) return;
            EndGame();
        }

        private void HandleRetryRequested()
        {
            if (CurrentState != GameState.GameOver) return;
            CurrentState = GameState.Menu;
            ResetForRetry();
        }

        private void StartGame()
        {
            CurrentState = GameState.Playing;
            if (_ball != null) _ball.StartMoving();
            _onGameStarted.Raise();
        }

        private void EndGame()
        {
            CurrentState = GameState.GameOver;
            _onGameOver.Raise();
        }

        private void ResetForRetry()
        {
            if (_ball != null && _ballSpawnPoint != null)
            {
                _ball.ResetTo(_ballSpawnPoint.position);
            }
            _onGameReset.Raise();
        }
    }
}
