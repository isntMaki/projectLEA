using System.Collections.Generic;
using ProjectLEA.Manuel.Managers;
using ProjectLEA.Manuel.Net;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// One deployed ability zone on the ground: a smoke, a wall, a damage field, a slow, a pull,
    /// a suppression field or a flash. Created by <see cref="ZoneAbility"/> when a player casts,
    /// and created again on the other machine by <see cref="NetMatchSync"/> hearing about it.
    ///
    /// WHOSE BODY IT TOUCHES - the rule that keeps a mirrored zone from double-applying:
    /// a zone never touches the other player's proxy. It affects the local player, and anything
    /// else that is not a proxy (a training dummy, the spike site). In a networked match that
    /// means each machine's copy of the zone only ever damages the body that machine owns, so
    /// the damage lands exactly once and both machines agree about who was inside.
    ///
    /// The visual is a primitive scaled to the radius - placeholder geometry until the real
    /// models and effects arrive, same as everything else in this pass.
    /// </summary>
    [DisallowMultipleComponent]
    public class AbilityZone : MonoBehaviour
    {
        [Header("Identity")]
        public ZoneKind kind = ZoneKind.None;

        [Tooltip("Who cast it, so the zone can be cleaned up with them and can spare them.")]
        public GameObject caster;

        [Header("Shape")]
        public float radius = 4f;

        [Tooltip("Wall only: the barrier's size (x width, y height, z thickness).")]
        public Vector3 wallSize = new Vector3(6f, 3f, 0.5f);

        [Header("Timing")]
        public float duration = 6f;

        [Tooltip("Seconds between damage and slow ticks.")]
        public float tickInterval = 0.5f;

        [Header("Effect")]
        [Tooltip("Damage dealt per tick to a body inside. Only used by Damage.")]
        public float damagePerTick = 6f;

        [Tooltip("Speed multiplier while inside. Only used by Slow.")]
        public float slowFactor = 0.5f;

        [Tooltip("Meters per second of pull towards the centre. Only used by Pull.")]
        public float pullSpeed = 7f;

        [Tooltip("True when the zone may damage the player who cast it.")]
        public bool harmsCaster = false;

        [Header("Looks")]
        public Color smokeColor = new Color(0.55f, 0.6f, 0.68f, 0.75f);
        public Color damageColor = new Color(0.85f, 0.25f, 0.2f, 0.35f);
        public Color healColor = new Color(0.25f, 0.8f, 0.5f, 0.3f);
        public Color slowColor = new Color(0.3f, 0.55f, 0.95f, 0.3f);
        public Color pullColor = new Color(0.65f, 0.3f, 0.9f, 0.3f);
        public Color suppressColor = new Color(0.85f, 0.55f, 0.15f, 0.3f);
        public Color flashColor = new Color(1f, 0.95f, 0.7f, 0.4f);
        public Color wallColor = new Color(0.45f, 0.5f, 0.58f, 0.9f);

        private float _endsAt;
        private float _nextTick;
        private GameObject _visual;
        private bool _flashed;

        /// <summary>The bodies this zone may affect this frame. Never includes a network proxy.</summary>
        private readonly List<Health> _hits = new List<Health>();

        /// <summary>Configures everything and spawns the placeholder visual.</summary>
        public void Initialise(ZoneKind zoneKind, GameObject owner, float zoneRadius, float zoneDuration,
                               float tickDamage, float slow, float pull, Vector3 barrierSize)
        {
            kind = zoneKind;
            caster = owner;
            radius = Mathf.Max(0.2f, zoneRadius);
            duration = Mathf.Max(0.1f, zoneDuration);
            damagePerTick = Mathf.Max(0f, tickDamage);
            slowFactor = Mathf.Clamp01(slow);
            pullSpeed = Mathf.Max(0f, pull);
            wallSize = barrierSize;

            _endsAt = Time.time + duration;
            _nextTick = Time.time + tickInterval;

            SpawnVisual();
        }

        private void SpawnVisual()
        {
            switch (kind)
            {
                case ZoneKind.Wall:
                    _visual = AbilityVisuals.SpawnBoxMarker(transform.position, wallSize, wallColor);
                    _visual.transform.SetParent(transform, true);
                    _visual.transform.localPosition = Vector3.zero;
                    break;

                default:
                    _visual = AbilityVisuals.SpawnSphereMarker(transform.position, radius, ColorFor(kind));
                    _visual.transform.SetParent(transform, true);
                    _visual.transform.localPosition = Vector3.zero;
                    break;
            }
        }

        private Color ColorFor(ZoneKind zoneKind)
        {
            switch (zoneKind)
            {
                case ZoneKind.Smoke: return smokeColor;
                case ZoneKind.Damage: return damagePerTick < 0f ? healColor : damageColor;
                case ZoneKind.Slow: return slowColor;
                case ZoneKind.Pull: return pullColor;
                case ZoneKind.Suppress: return suppressColor;
                case ZoneKind.Flash: return flashColor;
                default: return smokeColor;
            }
        }

        private void Update()
        {
            if (Time.time >= _endsAt)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.time < _nextTick) return;
            _nextTick = Time.time + tickInterval;

            Tick();
        }

        /// <summary>One application of whatever the zone does, to every body it is allowed to touch.</summary>
        private void Tick()
        {
            if (kind == ZoneKind.None || kind == ZoneKind.Smoke || kind == ZoneKind.Wall) return;

            _hits.Clear();
            var colliders = Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Ignore);

            foreach (var collider in colliders)
            {
                if (collider == null) continue;

                // The other player's body on this machine is a proxy whose real state lives
                // elsewhere. This machine is not authoritative for it, so it is skipped here -
                // the other machine's copy of this same zone is what applies the effect.
                if (collider.GetComponentInParent<NetProxyPlayer>() != null) continue;

                var health = collider.GetComponentInParent<Health>();
                if (health == null || _hits.Contains(health)) continue;

                if (!harmsCaster && caster != null &&
                    IsPartOf(collider.transform, caster.transform)) continue;

                _hits.Add(health);
            }

            foreach (var health in _hits) ApplyTo(health);
        }

        private void ApplyTo(Health health)
        {
            var status = health.GetComponent<StatusHost>();

            switch (kind)
            {
                case ZoneKind.Damage:
                    if (damagePerTick > 0f)
                        health.ApplyDamage(DamageInfo.Direct(damagePerTick, health.transform.position,
                                                             Vector3.up, caster, null));
                    break;

                case ZoneKind.Slow:
                    // Refreshed every tick, so leaving the zone is what ends the slow.
                    status?.ApplySlow(slowFactor, tickInterval * 1.5f);
                    break;

                case ZoneKind.Pull:
                    status?.ApplyPull(transform.position, pullSpeed * tickInterval);
                    break;

                case ZoneKind.Suppress:
                    status?.ApplySuppress(tickInterval * 1.5f);
                    break;

                case ZoneKind.Flash:
                    // A flash blinds once, on arrival - re-blinding every tick would make the
                    // zone an infinite stun for anyone standing in it.
                    if (_flashed) break;
                    _flashed = true;
                    if (status != null && !status.IsFlashing)
                        status.ApplyFlash(Mathf.Max(0.5f, duration * 0.5f));
                    break;
            }
        }

        private static bool IsPartOf(Transform candidate, Transform root)
        {
            if (candidate == root) return true;
            return candidate != null && root != null && candidate.IsChildOf(root);
        }

        private void OnDestroy()
        {
            if (_visual != null) Destroy(_visual);
        }
    }
}
