using ProjectLEA.Manuel.Managers;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// A worked example of a class ability, and the template to copy for real ones.
    ///
    /// HOW TO WIRE IT UP
    ///  1. Make an empty GameObject, add this component to it.
    ///  2. Set "Ability Name" to match the display name of an ability on your class
    ///     (the seeded test class has one called "Test Dash").
    ///  3. Drag the GameObject into the Project window to make it a prefab.
    ///  4. Assign that prefab to the class's "Ability Prefab" slot in the ClassManager.
    ///
    /// The name is how the host knows which keypress belongs to which script. If it does not
    /// match, this never fires and the host logs a warning saying so - which is the difference
    /// between "the key does nothing" and a diagnosable problem.
    ///
    /// The dash is driven through PlayerController.ExternalMotionLock / ExternalVelocity, the
    /// same channel the dodge used, so gravity, camera feel and footsteps all keep working.
    /// </summary>
    [DisallowMultipleComponent]
    public class SampleDashAbility : MonoBehaviour, IClassAbility
    {
        [Header("Identity")]
        [Tooltip("Must match the ability's display name on the class, or this never fires.")]
        [SerializeField] private string abilityName = "Test Dash";

        [Header("Dash")]
        [Tooltip("Distance covered at the ability's default power.")]
        [SerializeField] private float dashDistance = 7f;

        [Tooltip("How long the burst lasts. Shorter is snappier.")]
        [SerializeField] private float dashDuration = 0.22f;

        [Tooltip("Reference power. The ability's own power scales the distance against this.")]
        [SerializeField] private float referencePower = 8f;

        private ProjectLEA.Manuel.PlayerController _controller;
        private float _endsAt;
        private bool _dashing;

        public string AbilityName => abilityName;

        public void OnEquipped(GameObject player)
        {
            _controller = player != null ? player.GetComponent<ProjectLEA.Manuel.PlayerController>() : null;

            if (_controller == null)
            {
                Debug.LogWarning("[SampleDashAbility] The player has no PlayerController, " +
                                 "so the dash will not move anything.");
            }
        }

        public void OnUnequipped(GameObject player)
        {
            Stop();
        }

        public void OnActivated(GameObject player, AbilityDefinition ability)
        {
            if (_controller == null || _dashing) return;

            // The class's own data drives the distance, so tuning happens in one place.
            float power = ability != null ? ability.power : referencePower;
            float distance = dashDistance * Mathf.Max(0.1f, power / Mathf.Max(0.01f, referencePower));

            Vector3 direction = _controller.transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            _dashing = true;
            _endsAt = Time.time + dashDuration;

            _controller.ExternalMotionLock = true;
            _controller.ExternalVelocity = direction * (distance / dashDuration);

            Debug.Log($"[SampleDashAbility] Dashing {distance:0.#}m over {dashDuration:0.##}s.");
        }

        private void Update()
        {
            if (_dashing && Time.time >= _endsAt) Stop();
        }

        private void OnDisable()
        {
            Stop();
        }

        /// <summary>
        /// Always give the controller back, or the player is left permanently unable to move.
        /// </summary>
        private void Stop()
        {
            if (!_dashing) return;
            _dashing = false;

            if (_controller == null) return;

            _controller.ExternalMotionLock = false;
            _controller.ExternalVelocity = Vector3.zero;
        }
    }
}
