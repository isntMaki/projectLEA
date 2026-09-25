using ProjectLEA.Manuel.Managers;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// Movement archetypes. All three are self-only, so nothing here touches the network: the
    /// player's own machine is authoritative for its own body, and the transform sync carries
    /// the result to the other machine.
    ///
    /// Dash and Teleport both borrow the dodge's motion channel, <see cref="PlayerController.ExternalMotionLock"/>
    /// plus <see cref="PlayerController.ExternalVelocity"/> - gravity, camera feel and footsteps
    /// all keep working through it. Updraft instead writes the vertical velocity directly,
    /// because a launch should leave the player free to steer on the way up.
    /// </summary>
    namespace MovementAbilities
    {
        /// <summary>
        /// A burst of speed along the look direction. The class's <c>power</c> is the distance
        /// covered; <c>range</c> is unused.
        /// </summary>
        public class DashAbility : AbilityBase
        {
            [Tooltip("How long the burst lasts. Shorter is snappier.")]
            [SerializeField] private float dashDuration = 0.22f;

            [Tooltip("Distance covered at the default power of 1.")]
            [SerializeField] private float referenceDistance = 7f;

            private float _endsAt;
            private bool _dashing;

            public override void OnUnequipped(GameObject player) => Stop();

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                if (Controller == null || _dashing) return;

                float distance = referenceDistance * Power(ability);

                Vector3 direction = Controller.transform.forward;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f) return;
                direction.Normalize();

                _dashing = true;
                _endsAt = Time.time + dashDuration;

                Controller.ExternalMotionLock = true;
                Controller.ExternalVelocity = direction * (distance / dashDuration);

                LogActivation($"Dash, {distance:0.#}m over {dashDuration:0.##}s");
            }

            private void Update()
            {
                if (_dashing && Time.time >= _endsAt) Stop();
            }

            private void OnDisable() => Stop();

            /// <summary>Always hand control back, or the player is left unable to move.</summary>
            private void Stop()
            {
                if (!_dashing) return;
                _dashing = false;

                if (Controller == null) return;

                Controller.ExternalMotionLock = false;
                Controller.ExternalVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Launches the player straight up. <c>power</c> scales the height.
        /// </summary>
        public class UpdraftAbility : AbilityBase
        {
            [Tooltip("Upward speed reached at the default power of 1, in m/s.")]
            [SerializeField] private float referenceLaunch = 7f;

            [Tooltip("Minimum upward speed, so a zeroed power value still does something.")]
            [SerializeField] private float minimumLaunch = 2f;

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                if (Controller == null) return;

                float launch = Mathf.Max(minimumLaunch, referenceLaunch * Power(ability));

                // Writing the vertical velocity directly means the jump arc and air control keep
                // working, rather than the motion lock a dash uses.
                Controller.ApplyExternalVertical(launch);

                LogActivation($"Updraft, {launch:0.#} m/s");
            }
        }

        /// <summary>
        /// Instantly moves the player to the point they are aiming at, up to <c>range</c> away.
        /// A blocked destination falls back to the nearest open point along the ray.
        /// </summary>
        public class TeleportAbility : AbilityBase
        {
            [Tooltip("How far along the aim ray the destination can be.")]
            [SerializeField] private float maxRange = 20f;

            [Tooltip("Radius of the capsule swept along the ray, so the destination is actually standable.")]
            [SerializeField] private float checkRadius = 0.45f;

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                if (Controller == null) return;

                float range = Mathf.Max(1f, Range(ability));
                if (maxRange > 0f) range = Mathf.Min(range, maxRange);

                var origin = AbilityAim.CameraTransform;
                if (origin == null) return;

                Vector3 from = origin.position;
                Vector3 forward = origin.forward;
                Vector3 destination = from + forward * range;

                // Stop at the first thing the player would not fit behind.
                if (Physics.SphereCast(from, checkRadius, forward, out var hit, range,
                                       ~0, QueryTriggerInteraction.Ignore))
                {
                    destination = from + forward * Mathf.Max(0f, hit.distance - checkRadius);
                }

                // Keep the player's feet on the ground they were standing on.
                destination.y = Controller.transform.position.y;

                float moved = Vector3.Distance(Controller.transform.position, destination);
                Controller.transform.position = destination;

                LogActivation($"Teleport, {moved:0.#}m");
            }
        }
    }
}
