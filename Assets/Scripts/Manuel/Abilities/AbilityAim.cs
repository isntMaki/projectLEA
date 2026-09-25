using ProjectLEA.Manuel.Managers;
using ProjectLEA.Manuel.Net;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// Shared helpers for ability scripts: where the player is aiming, and how to deal damage
    /// that works both offline and across the network.
    ///
    /// AIM: every targeted archetype places its effect at the point the crosshair is on, up to
    /// the ability's range. A grounded version is offered too, because a smoke or a mine wants
    /// to land on the floor rather than on the wall the crosshair happens to be touching.
    ///
    /// DAMAGE: an ability that deals damage follows the same rule a gunshot does - the machine
    /// that pulled the trigger computes the final amount, and if the body it hit is the other
    /// player's proxy the amount is relayed instead of applied, because the proxy's Health is
    /// a stand-in. <see cref="ApplyHit"/> is that rule in one place, so weapons and abilities
    /// cannot drift apart.
    /// </summary>
    public static class AbilityAim
    {
        /// <summary>Solid geometry only, so ability markers never clip through windows.</summary>
        private const int AimMask = ~0;

        /// <summary>The camera the local player is looking through.</summary>
        public static Transform CameraTransform =>
            Camera.main != null ? Camera.main.transform : null;

        /// <summary>
        /// The point the crosshair is on, or a point range meters ahead if it hits nothing.
        /// </summary>
        public static bool AimedPoint(out Vector3 point, float range = 30f)
        {
            var origin = CameraTransform;
            if (origin == null)
            {
                point = Vector3.zero;
                return false;
            }

            var ray = new Ray(origin.position, origin.forward);

            if (Physics.SphereCast(ray, 0.05f, out var hit, Mathf.Max(0.1f, range),
                                   AimMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }

            point = ray.GetPoint(Mathf.Max(0.1f, range));
            return false;
        }

        /// <summary>
        /// A point on the ground under the crosshair, clamped to range. Used by zones, mines and
        /// decoys, which all want to sit on the floor: a pure surface raycast would put a smoke
        /// halfway up the wall the player is looking at.
        /// </summary>
        public static bool GroundedPoint(out Vector3 point, float range = 30f)
        {
            var origin = CameraTransform;
            if (origin == null)
            {
                point = Vector3.zero;
                return false;
            }

            var ray = new Ray(origin.position, origin.forward);
            float distance = Mathf.Max(0.1f, range);

            // First whatever the crosshair is on...
            if (Physics.SphereCast(ray, 0.05f, out var surface, distance,
                                   AimMask, QueryTriggerInteraction.Ignore))
            {
                // ...then straight down from there to the floor, if that surface was not already it.
                if (surface.normal.y > 0.5f)
                {
                    point = surface.point;
                    return true;
                }

                var down = new Ray(surface.point + Vector3.up * 0.5f, Vector3.down);
                if (Physics.Raycast(down, out var ground, 10f, AimMask, QueryTriggerInteraction.Ignore))
                {
                    point = ground.point;
                    return true;
                }

                point = surface.point;
                return true;
            }

            // Looking at the sky: drop the point to the ground below the aim line's end.
            var skyPoint = ray.GetPoint(distance);
            var downFromSky = new Ray(skyPoint + Vector3.up * 0.5f, Vector3.down);
            if (Physics.Raycast(downFromSky, out var skyGround, 20f, AimMask, QueryTriggerInteraction.Ignore))
            {
                point = skyGround.point;
                return true;
            }

            point = skyPoint;
            return false;
        }

        /// <summary>The direction the crosshair is pointing.</summary>
        public static Vector3 AimedDirection()
        {
            var origin = CameraTransform;
            return origin != null ? origin.forward : Vector3.forward;
        }

        // ------------------------------------------------------------------
        // Damage
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies a hit, relaying it over the network when the body it landed on belongs to the
        /// other player. Returns true when the hit was relayed and must NOT be applied locally.
        /// </summary>
        public static bool ApplyHit(Health health, DamageInfo info)
        {
            if (health == null) return false;

            if (RelayIfProxy(health, info)) return true;

            health.ApplyDamage(info);
            return false;
        }

        /// <summary>
        /// An explosion at a point: linear falloff from full damage at the centre to a quarter
        /// at the edge, with the caster immune to their own blast. Relays any hit on the other
        /// player's proxy, exactly like a direct hit does.
        /// </summary>
        public static void ExplodeAt(Vector3 centre, float radius, float damage, GameObject caster,
                                     bool harmCaster = false)
        {
            radius = Mathf.Max(0.1f, radius);

            var colliders = Physics.OverlapSphere(centre, radius, AimMask, QueryTriggerInteraction.Ignore);
            foreach (var collider in colliders)
            {
                if (collider == null) continue;
                if (caster != null && IsPartOf(collider.transform, caster.transform)) continue;

                var health = collider.GetComponentInParent<Health>();
                if (health == null) continue;

                float distance = Vector3.Distance(centre, health.transform.position);
                float falloff = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(distance / radius));

                var info = DamageInfo.Explosion(damage * falloff, centre, caster, null, radius);
                ApplyHit(health, info);
            }
        }

        /// <summary>True when the body this Health belongs to is the other player's proxy.</summary>
        public static bool RelayIfProxy(Health health, DamageInfo info)
        {
            if (!LobbyNetwork.IsActive) return false;
            if (health == null) return false;
            if (health.GetComponentInParent<NetProxyPlayer>() == null) return false;

            LobbyNetwork.Instance.SendDamage(info.amount, info.point);
            return true;
        }

        private static bool IsPartOf(Transform candidate, Transform root)
        {
            if (candidate == root) return true;
            return candidate != null && root != null && candidate.IsChildOf(root);
        }
    }
}
