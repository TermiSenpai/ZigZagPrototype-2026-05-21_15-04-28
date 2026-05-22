using System;
using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Input;

namespace ZigZag.Runtime.Gameplay.Player
{
    /// <summary>
    /// Drives the ball along the path: constant-speed forward motion on one of two
    /// 45° diagonals in the XZ plane, instant direction flip on tap, downward
    /// raycast ground probe, and a self-simulated fall when there is no ground left.
    /// </summary>
    /// <remarks>
    /// ADR-001 in <c>zigzag_architecture.md</c> mandates a kinematic ball: no
    /// Rigidbody, no PhysX integration. Movement is applied directly to
    /// <see cref="Transform.position"/>; the fall is a hand-rolled downward velocity.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BallController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Tunable values for speed, acceleration, fall and ground check.")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Source of the single tap action that flips direction.")]
        private InputHandler _inputHandler;

        public event Action<Vector3> OnDirectionChanged;
        public event Action OnFell;

        public Vector3 CurrentDirection { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool IsMoving { get; private set; }
        public bool IsGrounded { get; private set; }

        private static readonly Vector3 RightDiagonal = new Vector3(1f, 0f, 1f).normalized;
        private static readonly Vector3 LeftDiagonal = new Vector3(-1f, 0f, 1f).normalized;

        private bool _isOnLeftDiagonal;
        private bool _hasFallen;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(BallController)} requires a {nameof(GameConfigSO)} reference.", this);
            Debug.Assert(_inputHandler != null, $"{nameof(BallController)} requires an {nameof(InputHandler)} reference.", this);

            CurrentDirection = RightDiagonal;
            CurrentSpeed = _config != null ? _config.InitialSpeed : 0f;
            IsGrounded = true;
        }

        private void OnEnable()
        {
            if (_inputHandler != null)
            {
                _inputHandler.OnTapped += HandleTapped;
            }
        }

        private void OnDisable()
        {
            if (_inputHandler != null)
            {
                _inputHandler.OnTapped -= HandleTapped;
            }
        }

        private void Update()
        {
            if (!IsMoving || _config == null) return;

            float deltaTime = Time.deltaTime;
            UpdateGrounded();

            if (IsGrounded)
            {
                CurrentSpeed = Mathf.Min(CurrentSpeed + _config.Acceleration * deltaTime, _config.MaxSpeed);
                transform.position += CurrentDirection * (CurrentSpeed * deltaTime);
            }
            else
            {
                Vector3 horizontal = CurrentDirection * (CurrentSpeed * deltaTime);
                Vector3 vertical = Vector3.down * (_config.FallSpeed * deltaTime);
                transform.position += horizontal + vertical;

                if (!_hasFallen && transform.position.y < _config.FallThreshold)
                {
                    _hasFallen = true;
                    IsMoving = false;
                    OnFell?.Invoke();
                }
            }
        }

        /// <summary>Begins forward motion. Safe to call multiple times.</summary>
        public void StartMoving()
        {
            IsMoving = true;
        }

        /// <summary>Halts motion without resetting position, speed or direction.</summary>
        public void StopMoving()
        {
            IsMoving = false;
        }

        /// <summary>
        /// Teleports the ball to <paramref name="position"/> and rewinds movement state
        /// to initial values. Caller is responsible for re-issuing <see cref="StartMoving"/>.
        /// </summary>
        public void ResetTo(Vector3 position)
        {
            transform.position = position;
            CurrentDirection = RightDiagonal;
            CurrentSpeed = _config != null ? _config.InitialSpeed : 0f;
            IsMoving = false;
            IsGrounded = true;
            _isOnLeftDiagonal = false;
            _hasFallen = false;
        }

        private void HandleTapped()
        {
            if (!IsMoving || !IsGrounded) return;

            _isOnLeftDiagonal = !_isOnLeftDiagonal;
            CurrentDirection = _isOnLeftDiagonal ? LeftDiagonal : RightDiagonal;
            OnDirectionChanged?.Invoke(CurrentDirection);
        }

        private void UpdateGrounded()
        {
            IsGrounded = Physics.Raycast(
                transform.position,
                Vector3.down,
                _config.GroundCheckDistance,
                _config.GroundLayerMask,
                QueryTriggerInteraction.Ignore);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_config == null) return;
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, transform.position + Vector3.down * _config.GroundCheckDistance);
        }
#endif
    }
}
