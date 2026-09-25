using System;
using System.Collections.Generic;
using ProjectLEA.Manuel.Abilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// One ability belonging to a class: the data the cooldown system needs, and the numbers the
    /// archetype library tunes itself with.
    ///
    /// Set <see cref="archetype"/> and the ClassAbilityHost installs the matching script on the
    /// player when the class is picked; the ability's <see cref="power"/>, <see cref="duration"/>,
    /// <see cref="range"/> and <see cref="radius"/> are what makes two classes that share an
    /// archetype feel different.
    ///
    /// Stays inline on the class rather than being its own asset, because an ability only
    /// ever belongs to one class and its numbers are tuning, not content.
    /// </summary>
    [Serializable]
    public class AbilityDefinition
    {
        [Header("Identity")]
        [Tooltip("Must match the Ability Name field on the script that implements this.")]
        public string displayName = "New Ability";

        [TextArea(2, 4)]
        [Tooltip("Shown in the shop.")]
        public string description = "";

        [Header("Timing and cost")]
        [Min(0f)] public float cooldown = 8f;

        [Min(0f)]
        [Tooltip("How long the effect lasts. 0 means instant.")]
        public float duration = 0f;

        [Min(0f)]
        [Tooltip("Generic strength value - damage, distance, duration. Read by the ability script.")]
        public float power = 10f;

        [Tooltip("True for the class's X-slot ultimate: longer cooldown, bigger effect.")]
        public bool isUltimate = false;

        [Header("Behaviour")]
        [Tooltip("Which reusable archetype this ability runs. The ClassAbilityHost adds the " +
                 "matching script to the player when the class is picked, so a class needs no " +
                 "prefab of its own - unless you set one below, which overrides this.")]
        public AbilityArchetype archetype = AbilityArchetype.None;

        [Tooltip("Only used by the Zone archetype, to pick which kind of zone is deployed.")]
        public ZoneKind zoneKind = ZoneKind.None;

        [Min(0f)]
        [Tooltip("Meters. Max placement range for zones, mines and decoys, or distance for a " +
                 "dash, or max range for a teleport.")]
        public float range = 20f;

        [Min(0f)]
        [Tooltip("Meters. Radius of a zone, or the splash radius of a projectile or mine.")]
        public float radius = 4f;

        [Header("Input")]
        [Tooltip("Key that fires this ability in play.")]
        public Key activationKey = Key.Q;

        /// <summary>True when this ability has no archetype and is display-only.</summary>
        public bool IsPassive => archetype == AbilityArchetype.None;
    }

    /// <summary>
    /// The four Valorant roles. Every class belongs to exactly one, and the class select
    /// screen groups the roster by it.
    /// </summary>
    public enum ClassRole
    {
        Duelist,
        Initiator,
        Controller,
        Sentinel
    }

    /// <summary>
    /// One playable class, as a standalone asset.
    ///
    /// Drag and drop, same as weapons: right-click in the Project window and pick
    /// Create > ProjectLEA > Class, tune it in its own Inspector, then drag it into the
    /// ClassManager's roster.
    ///
    /// Every class carries a fixed passive AND a fixed negative trait - that pairing is the
    /// point, so picking a class is a real trade-off rather than a straight upgrade.
    ///
    /// The class's abilities come from a shared archetype library: set the archetype on each
    /// ability and the ClassAbilityHost installs the matching script. A bespoke prefab is
    /// still supported via <see cref="abilityPrefab"/> for anything the library cannot express.
    /// </summary>
    [CreateAssetMenu(fileName = "NewClass", menuName = "ProjectLEA/Class", order = 1)]
    public class ClassDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Class";

        [Tooltip("The role the class plays. Used to group the select screen and the roster.")]
        public ClassRole role = ClassRole.Duelist;

        [TextArea(2, 4)]
        [Tooltip("Shown in the shop and on the class select screen.")]
        public string description = "";

        [Header("Traits - every class needs both")]
        [Tooltip("The fixed upside of picking this class.")]
        public string passiveTrait = "None";

        [Tooltip("The fixed downside. This is what keeps the class from being a straight upgrade.")]
        public string negativeTrait = "None";

        [Header("Loadout")]
        [Tooltip("Weapon assets this class may buy. Drag them in from the Project window. " +
                 "Leave empty to allow everything.")]
        public List<WeaponDefinition> allowedWeapons = new List<WeaponDefinition>();

        [Header("Abilities")]
        [Tooltip("Data for the abilities, used for display, keys and cooldowns.")]
        public List<AbilityDefinition> abilities = new List<AbilityDefinition>();

        [Tooltip("Prefab holding the scripts that implement this class's abilities. " +
                 "Its components should implement IClassAbility.")]
        public GameObject abilityPrefab;

        [Header("Stats")]
        [Min(1f)] public float maxHealth = 100f;

        [Min(0f)]
        [Tooltip("Multiplier on the player's walk speed. 1 = unchanged.")]
        public float moveSpeedMultiplier = 1f;

        [Min(0f)]
        [Tooltip("Starting stamina. Only used once the stamina system exists.")]
        public float maxStamina = 100f;

        /// <summary>True when the class places no restriction on which weapons can be bought.</summary>
        public bool AllowsAnyWeapon => allowedWeapons == null || allowedWeapons.Count == 0;

        /// <summary>Whether this class may buy the given weapon asset.</summary>
        public bool AllowsWeapon(WeaponDefinition weapon)
        {
            if (AllowsAnyWeapon) return true;
            if (weapon == null) return false;

            return allowedWeapons.Contains(weapon);
        }
    }
}
