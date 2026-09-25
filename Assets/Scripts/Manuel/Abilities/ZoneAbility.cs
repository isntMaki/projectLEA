using ProjectLEA.Manuel.Managers;
using ProjectLEA.Manuel.Net;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// Deploys a <see cref="ZoneKind"/> at the aimed point: a smoke, a wall, a damage field, a
    /// slow, a pull, a suppression field or a flash.
    ///
    /// THE MIRROR: the zone is spawned locally AND announced to the other machine, which spawns
    /// its own copy. Both copies render, so both players see the smoke and the wall. Neither copy
    /// ever touches the other player's proxy - <see cref="AbilityZone"/> affects only the body
    /// its own machine owns - so the damage lands exactly once, and each machine applies the
    /// slow, the flash and the pull to the only body it is authoritative for.
    ///
    /// In an offline match there is no proxy and nothing to mirror, so the zone simply affects
    /// whatever local bodies are inside it.
    /// </summary>
    public class ZoneAbility : AbilityBase
    {
        [Tooltip("Seconds between the zone's damage and slow ticks.")]
        [SerializeField] private float tickInterval = 0.5f;

        [Tooltip("Barrier size for a wall: x width, y height, z thickness.")]
        [SerializeField] private Vector3 wallSize = new Vector3(6f, 3f, 0.5f);

        [Tooltip("Fraction of the ability's damage per tick, so a class tunes total damage not per-tick damage.")]
        [Range(0.05f, 1f)] [SerializeField] private float damagePerTickShare = 1f;

        [Tooltip("Multiplier on the ability's power when it is used as the slow strength.")]
        [SerializeField] private float slowScale = 1f;

        [Tooltip("Multiplier on the ability's power when it is used as the pull speed.")]
        [SerializeField] private float pullScale = 1f;

        public override void OnActivated(GameObject player, AbilityDefinition ability)
        {
            ZoneKind kind = KindOf(ability);
            if (kind == ZoneKind.None)
            {
                Debug.LogWarning($"[ZoneAbility] '{AbilityName}' has no zone kind set, so it does nothing. " +
                                 "Set one on the class asset.");
                return;
            }

            if (!AbilityAim.GroundedPoint(out var point, Range(ability))) return;

            float duration = Duration(ability);
            float radius = Radius(ability);
            float tickDamage = Power(ability) * damagePerTickShare;
            float slow = Mathf.Clamp01(Power(ability) * slowScale);
            float pull = Power(ability) * pullScale;

            SpawnZone(point, kind, player, radius, duration, tickDamage, slow, pull, wallSize);

            // Tell the other machine, so it sees the smoke and its own body suffers the effect.
            // Offline this is a no-op and the local zone is the only one, which is correct.
            if (LobbyNetwork.IsActive)
                LobbyNetwork.Instance.SendZone((int)kind, point, radius, duration, tickDamage, slow, pull);

            LogActivation($"{kind} at {point}, radius {radius:0.#}");
        }

        /// <summary>Creates the local copy of a zone. Also the entry point the network layer uses.</summary>
        public static AbilityZone SpawnZone(Vector3 point, ZoneKind kind, GameObject caster,
                                            float radius, float duration, float tickDamage,
                                            float slow, float pull, Vector3 barrierSize)
        {
            var host = new GameObject($"Zone_{kind}");
            host.transform.position = point;

            var zone = host.AddComponent<AbilityZone>();
            zone.Initialise(kind, caster, radius, duration, tickDamage, slow, pull, barrierSize);

            return zone;
        }
    }
}
