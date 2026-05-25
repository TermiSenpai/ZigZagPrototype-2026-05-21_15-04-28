using System;
using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.Player
{
    /// <summary>
    /// Drives the ball along the path: constant-speed forward motion on one of two
    /// world-axis directions in the XZ plane, instant direction flip on demand,
    /// downward raycast ground probe, and a self-simulated fall when there is no
    /// ground left.
    /// </summary>
    /// <remarks>
    /// ADR-001 in <c>zigzag_architecture.md</c> mandates a kinematic ball: no
    /// Rigidbody, no PhysX integration. Movement is applied directly to
    /// <see cref="Transform.position"/>; the fall is a hand-rolled downward velocity.
    ///
    /// Direction model matches the original Ketchapp ZigZag: the path is laid out
    /// along world X and Z axes (cubes form 90° turns in world space), and the ball
    /// alternates between pure -X and pure +Z motion. Combined with the -45° Y
    /// camera rotation, this is what produces the iconic "diagonal zigzag" visual
    /// on screen even though world-space motion is axis-aligned.
    ///
    /// Lifecycle control is external: the ball does not listen to input. The
    /// <see cref="GameStateMachine"/> calls <see cref="StartMoving"/>, <see cref="StopMoving"/>,
    /// <see cref="ResetTo"/> and <see cref="FlipDirection"/> based on game state, which
    /// keeps a single source of truth for "may I move now?" and removes the risk
    /// of the same tap both starting the run and flipping the direction.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BallController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Tunable values for speed, acceleration, fall and ground check.")]
        private GameConfigSO _config;

        [Header("Event Channels (Outbound)")]
        [SerializeField, Tooltip("Optional SO channel raised whenever the ball flips axis. Presentation systems (audio, VFX) listen to it without depending on this MonoBehaviour.")]
        private GameEventSO _onDirectionChangedChannel;

        [Header("Visual Rolling")]
        [SerializeField, Tooltip("Visual radius used to convert linear speed into angular speed (no-slip rolling). Match the rendered sphere's radius — default 0.5 fits a Unity sphere primitive at scale 1.")]
        private float _ballRadius = 0.5f;

        public event Action<Vector3> OnDirectionChanged;
        public event Action OnFell;

        public Vector3 CurrentDirection { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool IsMoving { get; private set; }
        public bool IsGrounded { get; private set; }

        private static readonly Vector3 AlongNegativeX = new Vector3(-1f, 0f, 0f);
        private static readonly Vector3 AlongPositiveZ = new Vector3(0f, 0f, 1f);

        private bool _isOnXAxis;
        private bool _hasFallen;
        private Vector3 _rollAxis;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(BallController)} requires a {nameof(GameConfigSO)} reference.", this);

            CurrentDirection = AlongNegativeX;
            _isOnXAxis = true;
            _rollAxis = Vector3.Cross(Vector3.up, CurrentDirection);
            CurrentSpeed = _config != null ? _config.InitialSpeed : 0f;
            IsGrounded = true;
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

            // No-slip rolling visual: ω = v/r (rad/s) around the axis perpendicular
            // to motion in the ground plane. Applied last so it picks up the same
            // CurrentSpeed used for translation this frame.
            float rollDegrees = CurrentSpeed * deltaTime * Mathf.Rad2Deg / _ballRadius;
            transform.Rotate(_rollAxis, rollDegrees, Space.World);
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
            transform.rotation = Quaternion.identity;
            CurrentDirection = AlongNegativeX;
            CurrentSpeed = _config != null ? _config.InitialSpeed : 0f;
            IsMoving = false;
            IsGrounded = true;
            _isOnXAxis = true;
            _hasFallen = false;
            _rollAxis = Vector3.Cross(Vector3.up, CurrentDirection);
        }

        /// <summary>
        /// Swaps the ball's axis. No-op when not moving or while falling — both
        /// guards match the original game's behavior, where input is locked once
        /// the ball has left the path.
        /// </summary>
        public void FlipDirection()
        {
            if (!IsMoving || !IsGrounded) return;

            _isOnXAxis = !_isOnXAxis;
            CurrentDirection = _isOnXAxis ? AlongNegativeX : AlongPositiveZ;
            _rollAxis = Vector3.Cross(Vector3.up, CurrentDirection);
            OnDirectionChanged?.Invoke(CurrentDirection);
            if (_onDirectionChangedChannel != null) _onDirectionChangedChannel.Raise();
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
        private void OnValidate()
        {
            if (_ballRadius < 0.01f) _ballRadius = 0.01f;
        }

        private void OnDrawGizmosSelected()
        {
            if (_config == null) return;
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, transform.position + Vector3.down * _config.GroundCheckDistance);
        }
#endif
    }
}
