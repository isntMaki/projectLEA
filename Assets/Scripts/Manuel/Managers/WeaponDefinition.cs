using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// What kind of weapon this is - how it is *delivered*, which is what decides which
    /// attributes it needs. This drives both the Inspector fields and the runtime logic,
    /// so a sword is never asked for a magazine and a gun is never asked for a swing arc.
    ///
    /// Deliberately NOT in here: magic, shields, traps, turrets and the like. Those are
    /// class abilities, not weapons, and they live on the class's ability prefab - see
    /// <see cref="AbilityDefinition"/> and <see cref="ClassDefinition.abilityPrefab"/>.
    /// </summary>
    public enum WeaponCategory
    {
        /// <summary>Swung. Arc hit detection, no ammo. Swords, hammers, axes.</summary>
        Melee,

        /// <summary>Firearm. Magazine, reserve and reload.</summary>
        Gun,

        /// <summary>Drawn. Charge time and arrows.</summary>
        Bow,

        /// <summary>Thrown. Limited count, and either a fuse or an impact trigger.</summary>
        Thrown,

        /// <summary>Explosive. Magazine plus a blast radius.</summary>
        Launcher
    }

    /// <summary>
    /// How a gun responds to the trigger. This is the difference between a pistol and a
    /// rifle: a Semi weapon fires once per click and cannot be fired any faster by holding,
    /// an Auto weapon keeps firing while the button is down, and a Burst weapon spends
    /// several rounds per click on a faster internal clock.
    /// </summary>
    public enum FireMode
    {
        Semi,
        Auto,
        Burst
    }

    /// <summary>
    /// What the right mouse button does. Most guns have no alt fire at all. The three that
    /// do exist in the arsenal are the defining quirk of their weapon, so they are modelled
    /// here rather than special-cased in the firing code.
    /// </summary>
    public enum AltFire
    {
        /// <summary>Right click does nothing.</summary>
        None,

        /// <summary>A quick burst of rounds. The Classic's three-shot burst.</summary>
        Burst,

        /// <summary>Same pellets in a much tighter cone. The Bucky's canister shot.</summary>
        TightSpread,

        /// <summary>A slower, heavier swing. The knife's backstab.</summary>
        HeavySwing
    }

    /// <summary>
    /// One weapon, as a standalone asset.
    ///
    /// This is a ScriptableObject so the workflow is drag and drop: right-click in the
    /// Project window and pick Create > ProjectLEA > Weapon, tune it in its own Inspector,
    /// then drag the file into the WeaponManager's catalogue and into any class's allowed
    /// weapons list. One asset, referenced from as many places as you like.
    ///
    /// The Inspector only shows the fields that apply to the chosen category - see
    /// WeaponDefinitionEditor. The core block applies to everything.
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeapon", menuName = "ProjectLEA/Weapon", order = 0)]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Weapon";

        public WeaponCategory category = WeaponCategory.Melee;

        [TextArea(2, 4)]
        [Tooltip("Shown in the shop.")]
        public string description = "";

        [Header("Economy")]
        [Min(0)]
        [Tooltip("Price in match credits.")]
        public int cost = 100;

        [Tooltip("Owned from the start, without paying.")]
        public bool ownedByDefault;

        [Header("Damage and timing")]
        [Min(0f)] public float damage = 10f;

        [Min(0f)]
        [Tooltip("Seconds between the end of one attack and the start of the next. For a gun " +
                 "this is the reciprocal of its fire rate: 0.1 delay = 10 rounds per second.")]
        public float delay = 0.5f;

        [Min(0f)]
        [Tooltip("Telegraph before the hit lands. This is the opponent's window to react. " +
                 "Guns fire on the click, so theirs is 0.")]
        public float windup = 0.15f;

        [Min(0f)]
        [Tooltip("How far the attack reaches, in metres.")]
        public float range = 2.2f;

        // --- Melee ---------------------------------------------------
        [Header("Melee")]
        [Range(1f, 360f)]
        [Tooltip("Width of the swing, in degrees. 360 hits all around.")]
        public float swingArc = 70f;

        // --- Everything except melee --------------------------------
        [Header("Projectile (Gun / Bow / Thrown / Launcher)")]
        [Min(0f)]
        [Tooltip("How fast the projectile travels. 0 means instant hit.")]
        public float projectileSpeed = 0f;

        // --- Gun, Bow, Thrown and Launcher all consume ammo ----------
        [Header("Ammo (Gun / Bow / Thrown / Launcher)")]
        [Min(0)]
        [Tooltip("Rounds before a reload is needed. 0 means the weapon is single-use.")]
        public int magazineSize = 12;

        [Min(0)]
        [Tooltip("Spare rounds carried.")]
        public int reserveAmmo = 60;

        [Min(0f)] public float reloadTime = 1.6f;

        // --- Gun only ------------------------------------------------
        [Header("Gun")]
        [Tooltip("Semi = one shot per click. Auto = hold to keep firing. Burst = one click " +
                 "fires several rounds back to back on a faster clock.")]
        public FireMode fireMode = FireMode.Semi;

        [Min(2)]
        [Tooltip("Rounds fired per click when the fire mode is Burst.")]
        public int burstCount = 3;

        [Min(0.01f)]
        [Tooltip("Seconds between the rounds of a burst, which is faster than the normal delay.")]
        public float burstInterval = 0.2f;

        [Min(1)]
        [Tooltip("Projectiles fired by one trigger pull. Shotguns fire a spread of pellets; " +
                 "everything else fires one.")]
        public int pelletsPerShot = 1;

        [Min(0f)]
        [Tooltip("Half-angle of the cone those projectiles spread across, in degrees. " +
                 "0 means perfectly accurate.")]
        public float pelletSpread = 0f;

        [Header("Gun - hit zones")]
        [Min(0.1f)]
        [Tooltip("Damage multiplier when the shot lands above roughly three quarters of the " +
                 "target's body height. Valorant's headshot multiplier, per weapon.")]
        public float headshotMultiplier = 1f;

        [Min(0.1f)]
        [Tooltip("Damage multiplier when the shot lands below roughly half the target's body " +
                 "height. Most guns use 0.85.")]
        public float legshotMultiplier = 1f;

        [Header("Gun - damage falloff")]
        [Min(0f)]
        [Tooltip("Full damage up to this distance, in metres. 0 means the weapon never loses " +
                 "damage with distance - the Vandal, Guardian, Marshal, Outlaw, Operator and Ares.")]
        public float falloffStart = 0f;

        [Min(0f)]
        [Tooltip("End of the mid damage band, in metres. Beyond this the weapon does its " +
                 "minimum damage.")]
        public float falloffMid = 30f;

        [Min(0f)]
        [Tooltip("Distance at which the damage stops dropping. Anything past the mid band " +
                 "uses the minimum ratio.")]
        public float falloffEnd = 50f;

        [Range(0.05f, 1f)]
        [Tooltip("Damage dealt between the start and the mid distance, as a fraction of full.")]
        public float falloffMidRatio = 0.9f;

        [Range(0.05f, 1f)]
        [Tooltip("Damage dealt past the mid distance, as a fraction of full.")]
        public float falloffMinRatio = 0.8f;

        [Header("Gun - alternate fire (right mouse button)")]
        [Tooltip("What the right mouse button does. Most guns have no alt fire.")]
        public AltFire altFire = AltFire.None;

        [Min(2)]
        [Tooltip("Rounds in an alt-fire burst (Burst only).")]
        public int altBurstCount = 3;

        [Min(0.01f)]
        [Tooltip("Seconds between the rounds of an alt-fire burst.")]
        public float altBurstInterval = 0.2f;

        [Min(0f)]
        [Tooltip("Damage of the alt-fire swing (HeavySwing only). 0 means use the weapon's " +
                 "normal damage.")]
        public float altDamage = 0f;

        [Min(0f)]
        [Tooltip("Windup of the alt-fire swing (HeavySwing only), in seconds.")]
        public float altWindup = 0.3f;

        [Range(0.01f, 1f)]
        [Tooltip("Pellet-spread multiplier for a tight alt-fire cone (TightSpread only).")]
        public float altSpreadScale = 0.4f;

        [Header("Viewmodel")]
        [Tooltip("First-person model shown in the player's hands. Baked from an imported GLB " +
                 "by Tools > Manuel > Bake Weapon Models; leave empty for an invisible weapon.")]
        public GameObject viewmodelPrefab;

        [Tooltip("Extra local offset from the camera's viewmodel socket. Zero is usually right - " +
                 "the hand position is already baked into the prefab.")]
        public Vector3 viewmodelOffset = Vector3.zero;

        // --- Bow only -----------------------------------------------
        [Header("Bow")]
        [Min(0f)]
        [Tooltip("How long the shot must be held before it is at full power.")]
        public float drawTime = 0.8f;

        // --- Thrown only --------------------------------------------
        [Header("Thrown")]
        [Min(0f)]
        [Tooltip("Seconds before it detonates. 0 means it triggers on impact.")]
        public float fuse = 2f;

        // --- Launcher only ------------------------------------------
        [Header("Launcher")]
        [Min(0f)]
        [Tooltip("Blast radius in metres.")]
        public float splashRadius = 3.5f;

        /// <summary>True for everything except melee. Drives projectile handling.</summary>
        public bool IsRanged => category != WeaponCategory.Melee;

        /// <summary>
        /// True for categories that consume ammunition. Derived from the category, so a
        /// sword can never be asked to reload no matter what the ammo fields say.
        /// </summary>
        public bool UsesAmmo => IsRanged;

        /// <summary>True when the weapon fires and forgets, rather than being carried.</summary>
        public bool IsExpendable => category == WeaponCategory.Thrown;

        /// <summary>True when the weapon deals area damage on impact.</summary>
        public bool IsExplosive => category == WeaponCategory.Launcher;

        /// <summary>True when the weapon keeps firing while the trigger is held.</summary>
        public bool IsAutomatic => category == WeaponCategory.Gun && fireMode == FireMode.Auto;

        /// <summary>True when one trigger pull sends more than one projectile.</summary>
        public bool IsShotgun => category == WeaponCategory.Gun && pelletsPerShot > 1;

        /// <summary>True when right click does something.</summary>
        public bool HasAltFire => category == WeaponCategory.Gun && altFire != AltFire.None;

        /// <summary>Damage of the alt swing, falling back to the normal damage.</summary>
        public float AltSwingDamage => altDamage > 0f ? altDamage : damage;

        /// <summary>
        /// Damage as a fraction of full, for the distance a shot travelled. Valorant drops
        /// damage in flat bands rather than smoothly, so this steps down at two ranges.
        /// </summary>
        public float DamageRatioAtDistance(float distance)
        {
            if (falloffStart <= 0f) return 1f;
            if (distance <= falloffStart) return 1f;
            if (distance <= falloffMid) return falloffMidRatio;
            return falloffMinRatio;
        }

        /// <summary>One-line summary for the shop card, built only from applicable fields.</summary>
        public string Summary()
        {
            if (!IsRanged)
                return $"{category}  -  {damage:0.#} dmg  -  {delay:0.##}s  -  {swingArc:0} deg arc";

            string ammo = magazineSize > 0 ? $"  -  {magazineSize}/{reserveAmmo}" : "  -  single use";
            string mode = category == WeaponCategory.Gun ? $"  -  {fireMode}" : "";
            string pellets = IsShotgun ? $"  -  {pelletsPerShot} pellets" : "";
            string extra = IsExplosive ? $"  -  {splashRadius:0.#}m blast" : "";
            return $"{category}{mode}  -  {damage:0.#} dmg  -  {1f / Mathf.Max(0.001f, delay):0.#} rps{ammo}{pellets}{extra}";
        }
    }
}
