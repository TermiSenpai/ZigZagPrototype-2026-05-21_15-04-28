using UnityEngine;

namespace ZigZag.Runtime.Gameplay.Player
{
    /// <summary>
    /// Provisional bootstrapper for iteration 1: starts the ball on <see cref="Start"/>
    /// and logs direction changes / fall events. Exists only so the movement script
    /// can be playtested before the real <c>GameStateMachine</c> is implemented.
    /// </summary>
    // TODO: delete this component once GameStateMachine drives StartMoving on enter-Playing (iteration 2).
    [DisallowMultipleComponent]
    public sealed class BallAutoStarter : MonoBehaviour
    {
        [SerializeField, Tooltip("Ball controller to drive. Required.")]
        private BallController _ball;

        [SerializeField, Tooltip("If true, prints OnDirectionChanged and OnFell to the console.")]
        private bool _verbose = true;

        private void Awake()
        {
            Debug.Assert(_ball != null, $"{nameof(BallAutoStarter)} requires a {nameof(BallController)} reference.", this);
        }

        private void OnEnable()
        {
            if (_ball == null) return;
            _ball.OnDirectionChanged += HandleDirectionChanged;
            _ball.OnFell += HandleFell;
        }

        private void OnDisable()
        {
            if (_ball == null) return;
            _ball.OnDirectionChanged -= HandleDirectionChanged;
            _ball.OnFell -= HandleFell;
        }

        private void Start()
        {
            if (_ball == null) return;
            _ball.StartMoving();
        }

        private void HandleDirectionChanged(Vector3 newDirection)
        {
            if (_verbose) Debug.Log($"[BallAutoStarter] Direction changed → {newDirection}", this);
        }

        private void HandleFell()
        {
            if (_verbose) Debug.Log("[BallAutoStarter] Ball fell.", this);
        }
    }
}
