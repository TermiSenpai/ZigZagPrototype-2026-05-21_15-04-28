using UnityEngine;
using ZigZag.Runtime.Data;

namespace ZigZag.Runtime.Gameplay.CameraSystem
{
    /// <summary>
    /// Smoothly follows a target transform on the XZ plane while keeping the camera's
    /// initial world Y locked. The locked Y prevents the camera from chasing the ball
    /// downward when it falls off the path (ADR-007).
    /// </summary>
    /// <remarks>
    /// The horizontal offset between camera and target is captured once at <see cref="Start"/>
    /// (or whenever <see cref="SetTarget"/> is called) so the designer fully controls the
    /// framing by placing the camera in the scene.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField, Tooltip("Source of the SmoothDamp approach time.")]
        private GameConfigSO _config;

        [SerializeField, Tooltip("Transform the camera follows on X and Z. Y is ignored.")]
        private Transform _target;

        private Vector3 _horizontalOffset;
        private float _lockedY;
        private Vector3 _smoothVelocity;
        private bool _offsetCaptured;

        public Transform Target => _target;

        private void Awake()
        {
            Debug.Assert(_config != null, $"{nameof(CameraFollow)} requires a {nameof(GameConfigSO)} reference.", this);
        }

        private void Start()
        {
            CaptureOffset();
        }

        private void LateUpdate()
        {
            if (_target == null || _config == null || !_offsetCaptured) return;

            Vector3 targetPosition = _target.position;
            Vector3 desired = new Vector3(
                targetPosition.x + _horizontalOffset.x,
                _lockedY,
                targetPosition.z + _horizontalOffset.z);

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref _smoothVelocity,
                _config.CameraFollowSmoothTime);
        }

        /// <summary>
        /// Reassigns the follow target and recaptures the horizontal offset and locked
        /// Y from the current camera and target positions. Intended for runtime wiring
        /// from a bootstrapper.
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            _smoothVelocity = Vector3.zero;
            CaptureOffset();
        }

        private void CaptureOffset()
        {
            Vector3 cameraPosition = transform.position;
            _lockedY = cameraPosition.y;

            if (_target == null)
            {
                _offsetCaptured = false;
                return;
            }

            Vector3 targetPosition = _target.position;
            _horizontalOffset = new Vector3(
                cameraPosition.x - targetPosition.x,
                0f,
                cameraPosition.z - targetPosition.z);
            _offsetCaptured = true;
        }
    }
}
