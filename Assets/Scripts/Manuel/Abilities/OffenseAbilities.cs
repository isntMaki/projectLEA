using ProjectLEA.Manuel.Managers;
using ProjectLEA.Manuel.Net;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// Caster-side archetypes. These exist only on the machine that cast them, and damage the
    /// opponent through the same relay a gunshot uses - the caster computes the final amount and
    /// the victim's machine applies it. The victim therefore sees no projectile and no mine yet;
    /// that is placeholder behaviour until the models and effects arrive, not a design decision.
    /// </summary>
    namespace OffenseAbilities
    {
        /// <summary>
        /// A proximity explosive placed at the aimed point. It arms after a short delay, then
        /// detonates the moment a body enters its radius, dealing splash damage with falloff.
        /// </summary>
        public class MineAbility : AbilityBase
        {
            [Tooltip("Seconds after placement before the mine is armed. Stops it catching the caster mid-cast.")]
            [SerializeField] private float armDelay = 0.4f;

            [Tooltip("Seconds before an untriggered mine gives up on its own.")]
            [SerializeField] private float lifetime = 20f;

            [Tooltip("Fraction of the ability's power used as the blast damage.")]
            [SerializeField] private float damageShare = 1f;

            [Tooltip("Tint of the marker, so a mine reads as a mine.")]
            [SerializeField] private Color tint = new Color(0.8f, 0.3f, 0.25f, 0.8f);

            private GameObject _mine;

            public override void OnUnequipped(GameObject player) => Remove();

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                if (!AbilityAim.GroundedPoint(out var point, Range(ability))) return;

                Remove();

                _mine = AbilityVisuals.SpawnSphereMarker(point, 0.35f, tint);

                // The marker's own collider was removed by the factory, so this trigger is what
                // actually detects a body walking into it.
                var trigger = _mine.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = Mathf.Max(0.5f, Radius(ability));

                var state = _mine.AddComponent<MineTrigger>();
                state.Setup(player, armDelay, Mathf.Max(0.5f, lifetime), Power(ability) * damageShare,
                            Radius(ability));
                state.OnDetonate += HandleDetonation;

                LogActivation($"Mine at {point}, blast {Radius(ability):0.#}m");
            }

            private void HandleDetonation(MineTrigger trigger)
            {
                if (trigger == null) return;

                Vector3 point = trigger.transform.position;
                float damage = trigger.Damage;
                float radius = trigger.Radius;

                AbilityAim.ExplodeAt(point, radius, damage, trigger.Caster, harmCaster: false);

                // A bright flash marker where the blast was, so the caster sees the hit.
                var blast = AbilityVisuals.SpawnSphereMarker(point, radius,
                                                             new Color(1f, 0.75f, 0.3f, 0.5f));
                Object.Destroy(blast, 0.6f);

                Remove();
            }

            private void OnDisable() => Remove();

            private void Remove()
            {
                if (_mine == null) return;

                var state = _mine.GetComponent<MineTrigger>();
                if (state != null) state.OnDetonate -= HandleDetonation;

                Object.Destroy(_mine);
                _mine = null;
            }

            /// <summary>Detached from the ability so it survives after the ability is dropped.</summary>
            private class MineTrigger : MonoBehaviour
            {
                public GameObject Caster { get; private set; }
                public float Damage { get; private set; }
                public float Radius { get; private set; }

                public event System.Action<MineTrigger> OnDetonate;

                private float _armedAt;
                private float _expiresAt;

                public void Setup(GameObject caster, float armDelay, float lifetime,
                                  float damage, float radius)
                {
                    Caster = caster;
                    Damage = damage;
                    Radius = radius;
                    _armedAt = Time.time + armDelay;
                    _expiresAt = Time.time + lifetime;
                }

                private void Update()
                {
                    if (Time.time >= _expiresAt) Destroy(gameObject);
                }

                private void OnTriggerStay(Collider other)
                {
                    if (!enabled) return;
                    if (Time.time < _armedAt) return;
                    if (other == null) return;

                    // The caster cannot trip their own mine, and neither can the other player's
                    // proxy - that body is not this machine's to blow up.
                    if (Caster != null && other.transform.IsChildOf(Caster.transform)) return;
                    if (other.GetComponentInParent<NetProxyPlayer>() != null) return;
                    if (other.GetComponentInParent<Health>() == null) return;

                    enabled = false;
                    OnDetonate?.Invoke(this);

                    Destroy(gameObject);
                }
            }
        }

        /// <summary>
        /// An explosive round along the aim ray. It detonates on the first thing it hits and
        /// deals splash damage around the impact, with falloff from the centre.
        /// </summary>
        public class ProjectileAbility : AbilityBase
        {
            [Tooltip("Fraction of the ability's power used as the blast damage.")]
            [SerializeField] private float damageShare = 1f;

            [Tooltip("How fast the round travels, in m/s. Zero is hitscan.")]
            [SerializeField] private float speed = 0f;

            [Tooltip("Trail tint.")]
            [SerializeField] private Color tint = new Color(1f, 0.7f, 0.3f, 0.8f);

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                float range = Range(ability);
                float radius = Mathf.Max(0.5f, Radius(ability));
                float damage = Power(ability) * damageShare;

                var origin = AbilityAim.CameraTransform;
                if (origin == null) return;

                Vector3 start = origin.position;
                Vector3 forward = origin.forward;

                if (speed <= 0f)
                {
                    FireHitscan(start, forward, range, radius, damage, player);
                    return;
                }

                FireTravelling(start, forward, range, radius, damage, player, speed);
            }

            /// <summary>A fast round resolves in the same frame it was fired.</summary>
            private void FireHitscan(Vector3 start, Vector3 forward, float range, float radius,
                                     float damage, GameObject caster)
            {
                if (Physics.SphereCast(start, 0.1f, forward, out var hit, range,
                                       ~0, QueryTriggerInteraction.Ignore))
                {
                    Detonate(hit.point, radius, damage, caster);
                    return;
                }

                Detonate(start + forward * range, radius, damage, caster);
            }

            /// <summary>A slow round is a moving marker that detonates on contact.</summary>
            private void FireTravelling(Vector3 start, Vector3 forward, float range, float radius,
                                        float damage, GameObject caster, float speed)
            {
                var body = AbilityVisuals.SpawnSphereMarker(start, 0.25f, tint);
                var mover = body.AddComponent<TravellingRound>();
                mover.Setup(start, forward, range, radius, damage, caster, speed);
                mover.OnDetonate += (point, r, d, c) => Detonate(point, r, d, c);
            }

            private void Detonate(Vector3 point, float radius, float damage, GameObject caster)
            {
                AbilityAim.ExplodeAt(point, radius, damage, caster, harmCaster: false);

                var blast = AbilityVisuals.SpawnSphereMarker(point, radius,
                                                             new Color(1f, 0.75f, 0.3f, 0.5f));
                Object.Destroy(blast, 0.6f);

                LogActivation($"Detonation at {point}, {damage:0.#} damage over {radius:0.#}m");
            }

            /// <summary>Carries a round to its destination so a launcher's shot is actually visible.</summary>
            private class TravellingRound : MonoBehaviour
            {
                public event System.Action<Vector3, float, float, GameObject> OnDetonate;

                private Vector3 _start;
                private Vector3 _direction;
                private float _remaining;
                private float _radius;
                private float _damage;
                private GameObject _caster;
                private float _speed;

                public void Setup(Vector3 start, Vector3 direction, float range, float radius,
                                  float damage, GameObject caster, float speed)
                {
                    _start = start;
                    _direction = direction.normalized;
                    _remaining = range;
                    _radius = radius;
                    _damage = damage;
                    _caster = caster;
                    _speed = Mathf.Max(1f, speed);
                }

                private void Update()
                {
                    float step = _speed * Time.deltaTime;
                    if (step >= _remaining)
                    {
                        OnDetonate?.Invoke(_start + _direction * _remaining, _radius, _damage, _caster);
                        Destroy(gameObject);
                        return;
                    }

                    transform.position += _direction * step;
                    _remaining -= step;

                    if (Physics.SphereCast(transform.position, 0.2f, _direction, out _, step,
                                           ~0, QueryTriggerInteraction.Ignore))
                    {
                        OnDetonate?.Invoke(transform.position, _radius, _damage, _caster);
                        Destroy(gameObject);
                    }
                }
            }
        }

        /// <summary>
        /// Marks the opponent through walls for the duration, so the caster knows where they
        /// are. Purely a local visual on the caster's machine - it reads the proxy's position,
        /// which the network already supplies, and tints the body. It changes nothing the other
        /// machine is authoritative for, so nothing has to be sent.
        /// </summary>
        public class RevealAbility : AbilityBase
        {
            [Tooltip("Tint applied to a revealed body.")]
            [SerializeField] private Color tint = new Color(1f, 0.85f, 0.2f, 1f);

            [Tooltip("Seconds the mark lasts when the class data gives no duration.")]
            [SerializeField] private float defaultDuration = 3f;

            [Tooltip("Radius around the aimed point in which a body is marked. 0 = anywhere.")]
            [SerializeField] private float searchRadius = 0f;

            private readonly System.Collections.Generic.List<MarkedBody> _marked =
                new System.Collections.Generic.List<MarkedBody>();

            /// <summary>A renderer and the material it had before the mark, so the mark comes off cleanly.</summary>
            private struct MarkedBody
            {
                public Renderer Renderer;
                public Material Original;
            }

            public override void OnUnequipped(GameObject player) => Unmark();

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                Unmark();

                float duration = Duration(ability) > 0f ? Duration(ability) : defaultDuration;

                foreach (var body in FindEnemyBodies())
                {
                    if (searchRadius > 0f)
                    {
                        float distance = Vector3.Distance(transform.position, body.position);
                        if (distance > searchRadius) continue;
                    }

                    var renderer = body.GetComponentInChildren<Renderer>();
                    if (renderer == null) continue;

                    _marked.Add(new MarkedBody
                    {
                        Renderer = renderer,
                        Original = renderer.sharedMaterial
                    });

                    AbilityVisuals.ApplyTo(renderer, tint);
                }

                CancelInvoke(nameof(Unmark));
                Invoke(nameof(Unmark), Mathf.Max(0.5f, duration));

                LogActivation($"Reveal, {_marked.Count} body/bodies marked for {duration:0.#}s");
            }

            private void OnDisable() => Unmark();

            /// <summary>The other player's body on this machine, plus any offline target.</summary>
            private System.Collections.Generic.List<Transform> FindEnemyBodies()
            {
                var bodies = new System.Collections.Generic.List<Transform>();

                if (RoundManager.Exists)
                {
                    var remote = RoundManager.Instance.GetPlayer(PlayerSlot.Two);
                    if (remote != null) bodies.Add(remote);
                }

                // Offline: the training dummy and anything else hittable that is not the caster.
                var healths = Object.FindObjectsByType<Health>(FindObjectsSortMode.None);
                foreach (var health in healths)
                {
                    if (health == null) continue;
                    if (health.IsPlayer && health.Slot == PlayerSlot.One) continue;
                    if (!bodies.Contains(health.transform)) bodies.Add(health.transform);
                }

                return bodies;
            }

            /// <summary>Hands each renderer its own material back.</summary>
            private void Unmark()
            {
                foreach (var marked in _marked)
                {
                    if (marked.Renderer == null) continue;
                    marked.Renderer.sharedMaterial = marked.Original;
                }

                _marked.Clear();
            }
        }
    }
}
