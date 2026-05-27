using UnityEngine;
using ZigZag.Runtime.Data;
using ZigZag.Runtime.Events;

namespace ZigZag.Runtime.Gameplay.CameraSystem
{
    /// <summary>
    /// Smoothly follows a target transform by advancing the camera only along the
    /// global forward axis <c>(-1, 0, 1)/√2</c>. Lateral target motion is discarded
    /// by design — the ball visibly serpentines across the screen instead of being
    /// kept dead-center, reproducing the original Ketchapp ZigZag behavior.
    /// </summary>
    /// <remarks>
    /// The camera origin (its world XZ at init) and the target origin (the target's
    /// world position at init) are captured once at <see cref="Start"/> or whenever
    /// <see cref="SetTarget"/> is called. Each <see cref="LateUpdate"/> the desired
    /// position is recomputed from those origins via <see cref="CameraFollowMath"/>
    /// and reached with <see cref="Vector3.SmoothDamp(Vector3,Vector3,ref Vector3,float)"/>.
    /// The Y plane is locked to the camera's initial Y so the camera never chases
    /// the ball downward when it falls off the path (ADR-007 + ADR-014).
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of the SmoothDamp approach time.")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Transform the camera follows. Only its motion along the global forward axis (-1,0,1)/√2 moves the camera.")]
        private Transform _target;

        [Header("Event Channels (Inbound)")]
        [SerializeField, Tooltip("Listened-to: raised on retry. The camera snaps back to its captured origin so a long run does not slingshot the view back over many world units.")]
        private GameEventSO _onGameReset;

        private Vector3 _cameraOrigin;
        private Vector3 _targetOrigin;
        private float _lockedY;
        private Vector3 _smoothVelocity;
        private bool _originsCaptured;

        public Transform Target => _target;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(CameraFollow)} requires a {nameof(GameConfigSO)} reference.", this);
        }

        private void Start()
        {
            CaptureOrigins();
        }

        private void OnEnable()
        {
            if (_onGameReset != null) _onGameReset.Register(HandleGameReset);
        }

        private void OnDisable()
        {
            if (_onGameReset != null) _onGameReset.Unregister(HandleGameReset);
        }

        // Snaps the camera back to its captured origin so a long run does
        // not slingshot the view back over many world units when the player
        // retries. The Y plane is already locked, so only X/Z need a reset.
        private void HandleGameReset()
        {
            if (!_originsCaptured) return;
            transform.position = new Vector3(_cameraOrigin.x, _lockedY, _cameraOrigin.z);
            _smoothVelocity = Vector3.zero;
        }

        private void LateUpdate()
        {
            if (_target == null || _config == null || !_originsCaptured) return;

            Vector3 desired = CameraFollowMath.ComputeDesiredPosition(
                _cameraOrigin,
                _targetOrigin,
                _target.position,
                GameConfigSO.GlobalForward,
                _lockedY);

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref _smoothVelocity,
                _config.CameraFollowSmoothTime);
        }

        /// <summary>
        /// Reassigns the follow target and recaptures the camera/target origins
        /// and locked Y from the current world state. Intended for runtime wiring
        /// from a bootstrapper.
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            _smoothVelocity = Vector3.zero;
            CaptureOrigins();
        }

        private void CaptureOrigins()
        {
            _cameraOrigin = transform.position;
            _lockedY = _cameraOrigin.y;

            if (_target == null)
            {
                _originsCaptured = false;
                return;
            }

            _targetOrigin = _target.position;
            _originsCaptured = true;
        }
    }
}
