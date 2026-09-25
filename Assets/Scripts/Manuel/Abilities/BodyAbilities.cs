using ProjectLEA.Manuel.Managers;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// Self-affecting archetypes: things a class does to its own player. None of these reach
    /// the network, because the player's own machine is the only authority on its own body.
    ///
    /// Buffs are deliberately kept on separate multiplier fields from the class's own stats and
    /// from a debuff: <see cref="PlayerController.SpeedMultiplier"/> is the class, <see cref="PlayerController.ExternalSpeedMultiplier"/>
    /// is a debuff written by <see cref="StatusHost"/>, and <see cref="PlayerController.BuffSpeedMultiplier"/>
    /// is a buff written here. Each system owns one field, so an Empress and a Slow Zone can
    /// both be active without either clobbering the other.
    /// </summary>
    namespace BodyAbilities
    {
        /// <summary>
        /// Restores health over time. <c>power</c> is the total amount, spread across
        /// <c>duration</c> in even ticks.
        /// </summary>
        public class HealSelfAbility : AbilityBase
        {
            [Tooltip("Seconds between healing ticks.")]
            [SerializeField] private float tickInterval = 0.25f;

            private float _perTick;
            private float _nextTick;
            private float _endsAt;

            public override void OnUnequipped(GameObject player) => Cancel();

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                if (Health == null) return;

                float total = Power(ability);
                float duration = Mathf.Max(tickInterval, Duration(ability));

                _perTick = total * tickInterval / duration;
                _endsAt = Time.time + duration;
                _nextTick = Time.time;

                LogActivation($"Heal, {total:0.#} over {duration:0.#}s");
            }

            private void Update()
            {
                if (_endsAt <= 0f) return;
                if (Time.time >= _endsAt) { Cancel(); return; }
                if (Time.time < _nextTick) return;

                _nextTick = Time.time + tickInterval;
                Health?.Heal(_perTick);
            }

            private void OnDisable() => Cancel();

            private void Cancel()
            {
                _endsAt = 0f;
                _perTick = 0f;
            }
        }

        /// <summary>
        /// Grants a temporary shield that absorbs damage before health does.
        /// <c>power</c> is the shield amount, <c>duration</c> how long it lasts.
        /// </summary>
        public class ShieldSelfAbility : AbilityBase
        {
            [Tooltip("Multiplier on the ability's power, so a class can tune shield strength separately.")]
            [SerializeField] private float shieldScale = 1f;

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                if (Health == null) return;

                float amount = Power(ability) * shieldScale;
                Health.SetShield(amount, Duration(ability));

                LogActivation($"Shield, {amount:0.#} for {Duration(ability):0.#}s");
            }
        }

        /// <summary>
        /// A movement and handling buff. <c>power</c> is the bonus move speed as a fraction
        /// (0.25 = 25% faster), <c>duration</c> how long it lasts.
        /// </summary>
        public class BuffSelfAbility : AbilityBase
        {
            [Tooltip("How much of the speed bonus also applies to fire rate. 0 = speed only.")]
            [Range(0f, 1f)] [SerializeField] private float fireRateShare = 1f;

            [Tooltip("Seconds the buff lasts when the class data gives no duration.")]
            [SerializeField] private float defaultBuffDuration = 6f;

            private float _endsAt;

            public override void OnUnequipped(GameObject player) => Expire();

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                // Refreshing an active buff should extend it, not stack it.
                float duration = ability != null && ability.duration > 0f
                    ? ability.duration
                    : defaultBuffDuration;

                _endsAt = Time.time + duration;

                if (Controller != null)
                    Controller.BuffSpeedMultiplier = 1f + Power(ability);

                if (Weapons != null && fireRateShare > 0f)
                    Weapons.FireRateMultiplier = 1f + Power(ability) * fireRateShare;

                LogActivation($"Buff, +{Power(ability) * 100f:0.#}% speed for {duration:0.#}s");
            }

            private void Update()
            {
                if (_endsAt <= 0f) return;
                if (Time.time < _endsAt) return;

                Expire();
            }

            private void OnDisable() => Expire();

            /// <summary>Everything granted must be taken back, or the buff becomes permanent.</summary>
            private void Expire()
            {
                _endsAt = 0f;

                if (Controller != null) Controller.BuffSpeedMultiplier = 1f;
                if (Weapons != null) Weapons.FireRateMultiplier = 1f;
            }
        }

        /// <summary>
        /// Spawns a standing decoy at the aimed point that draws fire: it is a body-shaped
        /// placeholder that is destroyed the moment it is hit. In a networked match it exists
        /// only on the machine that cast it, which is fine for something whose only job is to
        /// be shot at.
        /// </summary>
        public class DecoyAbility : AbilityBase
        {
            [Tooltip("The decoy's body, scaled to roughly match a player.")]
            [SerializeField] private float decoyHeight = 1.8f;

            [Tooltip("How long an unshot decoy stands before it disappears on its own.")]
            [SerializeField] private float lifetime = 10f;

            [Tooltip("Tint, so a decoy reads as a decoy rather than as another player.")]
            [SerializeField] private Color tint = new Color(0.7f, 0.7f, 0.75f, 0.9f);

            public override void OnActivated(GameObject player, AbilityDefinition ability)
            {
                if (!AbilityAim.GroundedPoint(out var point, Range(ability))) return;

                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "AbilityDecoy";
                body.transform.position = point + Vector3.up * (decoyHeight * 0.5f);
                body.transform.localScale = new Vector3(0.5f, decoyHeight * 0.5f, 0.5f);

                // A capsule taller than it is wide is stable, unlike the disc-shaped cylinder
                // trap, so the default collider is kept - the decoy needs one to be hittable.
                var renderer = body.GetComponent<Renderer>();
                if (renderer != null) AbilityVisuals.ApplyTo(renderer, tint);

                // A decoy reports who shot it, which is the information it exists to gather.
                var health = body.AddComponent<Health>();
                health.SetMaxHealth(1f, refill: true);
                health.SetInvulnerable(false);
                health.OnDamaged += (info, applied) =>
                {
                    Debug.Log($"[DecoyAbility] Decoy destroyed by {(info.source != null ? info.source.name : "unknown")}.");
                    Destroy(body);
                };

                Destroy(body, Mathf.Max(1f, lifetime));

                LogActivation($"Decoy at {point}");
            }
        }
    }
}
