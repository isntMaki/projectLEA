using ProjectLEA.Manuel.Managers;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// Shared boilerplate for every ability archetype: the player it belongs to, the components
    /// an archetype commonly needs, and sane fallbacks for the tuning values so a script never
    /// crashes when the class data is missing.
    ///
    /// An archetype is instantiated by <see cref="ClassAbilityHost"/> when the class is picked,
    /// one component per ability, and its <see cref="AbilityName"/> is set to the ability's
    /// display name so the host's key binding finds it.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class AbilityBase : MonoBehaviour, IClassAbility
    {
        [Header("Identity")]
        [Tooltip("Set by the ClassAbilityHost to the ability's display name. Do not edit.")]
        [SerializeField] private string abilityName = "";

        /// <summary>Tuning defaults, used when the class asset supplies no value of its own.</summary>
        [Header("Tuning fallbacks")]
        [SerializeField] private float defaultPower = 1f;
        [SerializeField] private float defaultDuration = 1f;
        [SerializeField] private float defaultRange = 20f;
        [SerializeField] private float defaultRadius = 4f;

        /// <summary>The player this ability is installed on.</summary>
        protected GameObject Player { get; private set; }

        protected PlayerController Controller { get; private set; }
        protected Health Health { get; private set; }
        protected StatusHost Status { get; private set; }
        protected WeaponUser Weapons { get; private set; }

        public string AbilityName => abilityName;

        /// <summary>Called by the host so this component answers to the right key.</summary>
        public void SetAbilityName(string name) => abilityName = name ?? "";

        public virtual void OnEquipped(GameObject player)
        {
            Player = player;

            if (player != null)
            {
                Controller = player.GetComponent<PlayerController>();
                Health = player.GetComponent<Health>();
                Status = player.GetComponent<StatusHost>();
                Weapons = player.GetComponent<WeaponUser>();
            }
        }

        public virtual void OnUnequipped(GameObject player)
        {
            // Override to cancel anything still running, so dropping a class mid-effect cannot
            // leave the player locked in a dash or glowing with a buff.
        }

        public abstract void OnActivated(GameObject player, AbilityDefinition ability);

        // ------------------------------------------------------------------
        // Tuning, with fallbacks
        // ------------------------------------------------------------------

        protected float Power(AbilityDefinition ability) =>
            ability != null && ability.power > 0f ? ability.power : defaultPower;

        protected float Duration(AbilityDefinition ability) =>
            ability != null && ability.duration > 0f ? ability.duration : defaultDuration;

        protected float Range(AbilityDefinition ability) =>
            ability != null && ability.range > 0f ? ability.range : defaultRange;

        protected float Radius(AbilityDefinition ability) =>
            ability != null && ability.radius > 0f ? ability.radius : defaultRadius;

        protected ZoneKind KindOf(AbilityDefinition ability) =>
            ability != null ? ability.zoneKind : ZoneKind.None;

        /// <summary>Logs the activation, so a key that does nothing is distinguishable in the
        /// Console from a key that was never pressed.</summary>
        protected void LogActivation(string detail)
        {
            Debug.Log($"[{GetType().Name}] '{abilityName}' activated. {detail}");
        }
    }
}
