using UnityEngine;

namespace ZigZag.Runtime.Gameplay.World
{
    /// <summary>
    /// Visual "collapse" animation for a platform cube. Once <see cref="Begin"/> is
    /// called, the cube accelerates downward each frame until either the pool
    /// recycles it (which fires <see cref="OnDisable"/> and resets state) or the
    /// fall distance reaches <see cref="MaxFallDistance"/>.
    /// </summary>
    /// <remarks>
    /// The fall is hand-rolled (not a Rigidbody) to keep the cube kinematic and
    /// to avoid PhysX side effects on the ball's downward ground raycast. The
    /// triggering window is wide enough (controlled by
    /// <c>GameConfigSO.PlatformFallStartBehind</c>) that the ball has already
    /// left the cube before the fall starts, so the cube dropping out from under
    /// the ball is not a concern.
    ///
    /// Lifecycle contract with <see cref="PlatformPool"/>:
    /// <list type="bullet">
    /// <item>Pool releases cube → SetActive(false) → <see cref="OnDisable"/> resets state.</item>
    /// <item>Pool gets cube → SetActive(true) → faller idle; <see cref="PathGenerator"/> positions the cube.</item>
    /// <item><see cref="PathGenerator"/> calls <see cref="Begin"/> when the ball has moved past it.</item>
    /// </list>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlatformFaller : MonoBehaviour
    {
        // Hard cap so cubes that began falling but never get recycled (e.g. while
        // the game-over screen is up) eventually stop integrating instead of
        // dropping forever.
        private const float MaxFallDistance = 60f;

        [SerializeField, Tooltip("Downward acceleration (units/s^2) applied while falling. Higher = snappier collapse.")]
        private float _gravity = 18f;

        private float _velocity;
        private float _fallDistance;
        private bool _isFalling;

        public bool IsFalling => _isFalling;

        /// <summary>
        /// Starts the fall animation. Idempotent: extra calls after the first do nothing.
        /// </summary>
        public void Begin()
        {
            if (_isFalling) return;
            _isFalling = true;
            _velocity = 0f;
            _fallDistance = 0f;
        }

        private void Update()
        {
            if (!_isFalling) return;

            float deltaTime = Time.deltaTime;
            _velocity += _gravity * deltaTime;
            float step = _velocity * deltaTime;

            Vector3 position = transform.position;
            position.y -= step;
            transform.position = position;

            _fallDistance += step;
            if (_fallDistance >= MaxFallDistance) _isFalling = false;
        }

        private void OnDisable()
        {
            // Pool deactivates the cube before reusing it; the next Get() will reposition
            // it via PathGenerator, so we just clear the falling state here.
            _isFalling = false;
            _velocity = 0f;
            _fallDistance = 0f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_gravity < 0f) _gravity = 0f;
        }
#endif
    }
}
