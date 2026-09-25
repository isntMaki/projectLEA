using System;
using ProjectLEA.Manuel.Net;
using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Anything that can be damaged. Put this on the player, on a training target, on
    /// anything you want to be hittable.
    ///
    /// This is the single place damage enters an entity, so block, dodge, armour and any
    /// future mitigation all belong here rather than scattered across the weapons. Right
    /// now there is no mitigation at all - the raw amount goes straight through.
    ///
    /// When a player's health reaches zero the kill is reported to the ScoreManager, which
    /// is what awards the point. That is the only link between dying and scoring.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [Header("Health")]
        [Min(1f)]
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Refill to full on Awake.")]
        [SerializeField] private bool startFull = true;

        [Header("Identity")]
        [Tooltip("Tick this when this object is one of the two players. Death then awards a point.")]
        [SerializeField] private bool isPlayer;

        [Tooltip("Which player this is. Only used when 'Is Player' is ticked.")]
        [SerializeField] private PlayerSlot slot = PlayerSlot.One;

        [Header("Rules")]
        [Tooltip("Ignore all incoming damage. Useful while testing.")]
        [SerializeField] private bool invulnerable;

        [Tooltip("Log every hit to the Console.")]
        [SerializeField] private bool logDamage;

        public float Current { get; private set; }
        public float Max => maxHealth;

        /// <summary>
        /// Temporary damage absorption layered over health. An ability shield or bought armour
        /// writes this; incoming damage drains it before it touches health.
        /// </summary>
        public float Shield { get; private set; }

        /// <summary>True while a shield is active and has not yet expired.</summary>
        public bool HasShield => Shield > 0f && Time.time < _shieldEndsAt;

        /// <summary>Raised with the remaining shield whenever it changes.</summary>
        public event Action<float> OnShieldChanged;

        /// <summary>0 to 1. This is what the hidden health readout will be fed from.</summary>
        public float Normalised => maxHealth > 0f ? Mathf.Clamp01(Current / maxHealth) : 0f;

        public bool IsDead => Current <= 0f;
        public bool IsPlayer => isPlayer;
        public PlayerSlot Slot => slot;
        public bool Invulnerable => invulnerable || MatchCheats.IsGod(slot);

        /// <summary>Raised with the hit that landed and the damage actually applied.</summary>
        public event Action<DamageInfo, float> OnDamaged;

        /// <summary>Raised once, the first time health reaches zero.</summary>
        public event Action<Health> OnDied;

        /// <summary>Raised with the new value whenever it changes.</summary>
        public event Action<float> OnHealthChanged;

        private bool _deathReported;

        private float _shieldEndsAt = -1f;

        /// <summary>
        /// Grants a shield that absorbs damage for a duration. A stronger refresh replaces a
        /// weaker one; a weaker refresh never lowers a shield already in place.
        /// </summary>
        public void SetShield(float amount, float duration)
        {
            amount = Mathf.Max(0f, amount);
            if (amount < Shield && HasShield) return;

            Shield = amount;
            _shieldEndsAt = Time.time + Mathf.Max(0.1f, duration);
            OnShieldChanged?.Invoke(Shield);
        }

        private void Update()
        {
            // A shield that ran out must read as gone, otherwise an expired shield would still
            // report itself to the HUD.
            if (Shield > 0f && Time.time >= _shieldEndsAt)
            {
                Shield = 0f;
                OnShieldChanged?.Invoke(Shield);
            }
        }

        private void Awake()
        {
            Current = startFull ? maxHealth : 0f;
        }

        private void Start()
        {
            OnHealthChanged?.Invoke(Current);
        }

        /// <summary>
        /// Changes the maximum and optionally refills. Used when a class is picked, since
        /// max health is a class stat.
        /// </summary>
        public void SetMaxHealth(float value, bool refill = true)
        {
            maxHealth = Mathf.Max(1f, value);
            if (refill) ResetToFull();
            else Current = Mathf.Min(Current, maxHealth);

            OnHealthChanged?.Invoke(Current);
        }

        /// <summary>Engineers call this; a preset's god mode is checked per hit instead.</summary>
        public void SetInvulnerable(bool value) => invulnerable = value;

        /// <summary>True while this entity takes no damage at all, from any source.</summary>
        public bool IsCurrentlyInvulnerable => invulnerable || MatchCheats.IsGod(slot);

        /// <summary>
        /// Applies a hit. Returns how much damage actually landed, which is 0 when
        /// invulnerable or already dead.
        /// </summary>
        public float ApplyDamage(DamageInfo info)
        {
            if (invulnerable) return 0f;
            if (IsDead) return 0f;
            if (info.amount <= 0f) return 0f;

            // A cheat makes the hit as large as it needs to be to kill, rather than a fixed
            // huge number that would overflow the death reporting down the line.
            float amount = MatchCheats.oneHitKills ? MatchCheats.OneHitKillDamage : info.amount;

            // A shield eats what it can before health pays. Deliberately before the clamp below,
            // so an oversized hit still burns the whole shield down.
            if (Shield > 0f && Time.time < _shieldEndsAt)
            {
                float absorbed = Mathf.Min(amount, Shield);
                Shield -= absorbed;
                amount -= absorbed;
                OnShieldChanged?.Invoke(Shield);
            }

            float applied = Mathf.Min(amount, Current);
            Current -= applied;

            if (logDamage)
                Debug.Log($"[Health] {name} took {applied:0.#} from " +
                          $"{(info.weapon != null ? info.weapon.displayName : "unknown")} " +
                          $"({Current:0.#}/{maxHealth:0.#}).");

            OnDamaged?.Invoke(info, applied);
            OnHealthChanged?.Invoke(Current);

            if (IsDead) Die();
            return applied;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f) return;

            Current = Mathf.Min(maxHealth, Current + amount);
            OnHealthChanged?.Invoke(Current);
        }

        public void ResetToFull()
        {
            Current = maxHealth;
            Shield = 0f;
            _shieldEndsAt = -1f;
            _deathReported = false;
            OnHealthChanged?.Invoke(Current);
            OnShieldChanged?.Invoke(Shield);
        }

        private void Die()
        {
            if (_deathReported) return;
            _deathReported = true;

            Debug.Log($"[Health] {name} died.");

            // Deliberately does NOT award anything itself. GameManager listens for this and
            // runs the kill through one path - otherwise a relayed hit and a local one would
            // each score, and a kill would count twice.
            OnDied?.Invoke(this);
        }
    }
}
