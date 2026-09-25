using UnityEngine;

namespace ProjectLEA.Manuel.Net
{
    /// <summary>
    /// Put this on the REMOTE player's body. It receives transforms from the other machine
    /// (sent by their <see cref="NetPlayer"/>) and moves this body to match.
    ///
    /// This side never simulates the opponent. It only paints the position the opponent's
    /// machine is authoritative for, which is what keeps a body from being dragged back to a
    /// spawn point by the local round manager or shoved by a local CharacterController.
    ///
    /// Also marks the body so weapons know to relay hits: damage against this body belongs
    /// on the other machine, where that player's real Health lives.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetProxyPlayer : MonoBehaviour
    {
        [Tooltip("How quickly the proxy catches up to a received position. Higher = snappier, " +
                 "lower = smoother at the cost of visible lag.")]
        [SerializeField] private float catchUpSpeed = 18f;

        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private bool _hasTarget;

        private void Awake()
        {
            _targetPosition = transform.position;
            _targetRotation = transform.rotation;
        }

        /// <summary>Called by the network layer when a transform arrives.</summary>
        public void ApplySnapshot(Vector3 position, Quaternion rotation)
        {
            _targetPosition = position;
            _targetRotation = rotation;
            _hasTarget = true;
        }

        private void Update()
        {
            if (!_hasTarget) return;

            // Exponential catch-up: converges correctly at any frame rate, unlike a raw Lerp
            // with a fixed timestep.
            //
            // UNSCALED time, same reason as the NetMatchSync phase heartbeat: the class select
            // screen and the shop both push MatchUiPause and hold Time.timeScale at zero for as
            // long as they are up. A scaled lerp factor would collapse to zero and the remote
            // body would visibly freeze in place for the whole duration of those screens.
            float t = 1f - Mathf.Exp(-catchUpSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, _targetPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, t);
        }
    }
}
