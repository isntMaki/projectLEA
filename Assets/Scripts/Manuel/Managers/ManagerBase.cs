using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Base class for the game's managers.
    ///
    /// A manager owns one system's state and lifecycle and is reachable from anywhere via
    /// <c>Instance</c>. Instances are deliberately scene-scoped rather than persistent: a
    /// 1v1 match lives in a single scene, so carrying managers across a load would leak
    /// state from one match into the next.
    ///
    /// Network note: in a real PvP build the match-owning managers (Game, Round, Score,
    /// Economy) must eventually be server-authoritative. They are written so that exactly
    /// one authority mutates state and everything else reacts through events - that is the
    /// shape that survives the move to a server without a rewrite.
    /// </summary>
    public abstract class ManagerBase<T> : MonoBehaviour where T : ManagerBase<T>
    {
        private static T _instance;

        /// <summary>The live instance, or null if no manager of this type exists.</summary>
        public static T Instance => _instance;

        /// <summary>True when an instance exists. Safe to check without creating one.</summary>
        public static bool Exists => _instance != null;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Destroy the component rather than the GameObject: several managers are
                // expected to share one object.
                Debug.LogWarning($"[{typeof(T).Name}] Duplicate instance on '{name}' - removing the extra.");
                Destroy(this);
                return;
            }

            _instance = (T)this;
            OnManagerAwake();
        }

        protected virtual void OnDestroy()
        {
            // Only the real instance clears the slot; a rejected duplicate must not.
            if (_instance == this) _instance = null;
        }

        /// <summary>Override this instead of Awake.</summary>
        protected virtual void OnManagerAwake() { }
    }
}
