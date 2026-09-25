using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// One hit, described. Everything that deals damage builds one of these and hands it to
    /// <see cref="Health.ApplyDamage"/>, so there is a single place where damage enters an
    /// entity and a single shape for listeners to consume.
    ///
    /// A struct rather than a class: hits are created constantly and never outlive the call.
    /// </summary>
    public struct DamageInfo
    {
        /// <summary>Raw damage before any mitigation.</summary>
        public float amount;

        /// <summary>Where the hit landed, in world space.</summary>
        public Vector3 point;

        /// <summary>Direction the hit was travelling, normalised.</summary>
        public Vector3 direction;

        /// <summary>Whoever caused it. Null for world damage such as falling.</summary>
        public GameObject source;

        /// <summary>The weapon responsible, when there was one.</summary>
        public WeaponDefinition weapon;

        /// <summary>True when this is area damage rather than a direct hit.</summary>
        public bool isExplosive;

        /// <summary>Blast radius in metres. Only meaningful when <see cref="isExplosive"/>.</summary>
        public float splashRadius;

        /// <summary>Convenience constructor for a plain direct hit.</summary>
        public static DamageInfo Direct(float amount, Vector3 point, Vector3 direction, GameObject source, WeaponDefinition weapon)
        {
            return new DamageInfo
            {
                amount = amount,
                point = point,
                direction = direction,
                source = source,
                weapon = weapon,
                isExplosive = false,
                splashRadius = 0f
            };
        }

        /// <summary>Convenience constructor for a blast.</summary>
        public static DamageInfo Explosion(float amount, Vector3 point, GameObject source, WeaponDefinition weapon, float radius)
        {
            return new DamageInfo
            {
                amount = amount,
                point = point,
                direction = Vector3.up,
                source = source,
                weapon = weapon,
                isExplosive = true,
                splashRadius = radius
            };
        }
    }
}
