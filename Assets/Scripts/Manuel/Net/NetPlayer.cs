using UnityEngine;

namespace ProjectLEA.Manuel.Net
{
    /// <summary>
    /// Put this on the LOCAL player. It samples the player's own transform at a fixed rate
    /// and ships it to the other machine, which drives the matching proxy body through
    /// <see cref="NetProxyPlayer"/>.
    ///
    /// Sampling rather than sending every frame keeps the two machines from saturating each
    /// other's receive queues - 20 Hz is enough for melee-range combat to read correctly.
    ///
    /// Uses unscaled time on purpose: the shop pauses the clock, but the opponent still
    /// needs to see where you are standing while you are browsing.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetPlayer : MonoBehaviour
    {
        [Tooltip("Seconds between transform sends. 0.05 = 20 updates per second.")]
        [SerializeField] private float sendInterval = 0.05f;

        private float _nextSendAt;

        private void Update()
        {
            if (!LobbyNetwork.IsActive) return;
            if (Time.unscaledTime < _nextSendAt) return;

            _nextSendAt = Time.unscaledTime + sendInterval;

            var net = LobbyNetwork.Instance;
            net?.SendTransform(transform.position, transform.rotation);
        }
    }
}
