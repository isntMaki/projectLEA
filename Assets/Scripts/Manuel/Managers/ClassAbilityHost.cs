using System.Collections.Generic;
using ProjectLEA.Manuel.Abilities;
using ProjectLEA.Manuel.Abilities.BodyAbilities;
using ProjectLEA.Manuel.Abilities.MovementAbilities;
using ProjectLEA.Manuel.Abilities.OffenseAbilities;
using ProjectLEA.Manuel.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Implemented by any script that provides a class ability.
    ///
    /// This is the hook that makes "add a class, then attach a script for its abilities"
    /// work: put the script on a prefab, drop the prefab into the class's
    /// <c>abilityPrefab</c> slot, and the host calls these methods.
    ///
    /// <see cref="AbilityName"/> is how the host knows which keypress belongs to which
    /// script - it is matched against <see cref="AbilityDefinition.displayName"/> on the
    /// class. A component whose name matches nothing is still equipped, but never fired:
    /// that is how a passive ability is written.
    /// </summary>
    public interface IClassAbility
    {
        /// <summary>
        /// Must match the <see cref="AbilityDefinition.displayName"/> of the ability this
        /// script implements. Leave it empty for a passive that is never activated by a key.
        /// </summary>
        string AbilityName { get; }

        /// <summary>Called when the class is picked and the ability becomes active.</summary>
        void OnEquipped(GameObject player);

        /// <summary>Called when the class is dropped. Undo anything OnEquipped set up.</summary>
        void OnUnequipped(GameObject player);

        /// <summary>
        /// Called when the player presses this ability's key and it is off cooldown.
        /// The definition carries the tuning values from the class, so the script does not
        /// have to duplicate them.
        /// </summary>
        void OnActivated(GameObject player, AbilityDefinition ability);
    }

    /// <summary>
    /// Lives on the player and runs the picked class's abilities.
    ///
    /// A class's abilities come from the shared archetype library: each <see cref="AbilityDefinition"/>
    /// names an <see cref="AbilityArchetype"/>, and on a class pick this component adds the
    /// matching script to the player - one child object per ability - and matches it to the
    /// definition by name, which is how the keypress finds it. Cooldowns are tracked here, so
    /// an ability script only has to implement what the ability *does*.
    ///
    /// A class can still supply a bespoke <see cref="ClassDefinition.abilityPrefab"/> for
    /// anything the library cannot express; its components are installed alongside the
    /// archetypes and bind the same way.
    /// </summary>
    public class ClassAbilityHost : MonoBehaviour
    {
        /// <summary>The host on the local player, or null if there is not one.</summary>
        public static ClassAbilityHost Instance { get; private set; }

        public static bool Exists => Instance != null;

        /// <summary>The class currently installed, or null.</summary>
        public ClassDefinition CurrentClass { get; private set; }

        /// <summary>
        /// The abilities of the installed class, in the order their keys are read. Index matches
        /// <see cref="GetCooldownRemaining"/>, which is what the HUD's ability bar needs to show
        /// what is castable right now.
        /// </summary>
        public IReadOnlyList<AbilityDefinition> Definitions => _definitions;

        private GameObject _instance;
        private readonly List<IClassAbility> _abilities = new List<IClassAbility>();
        private readonly List<AbilityDefinition> _definitions = new List<AbilityDefinition>();
        private readonly List<float> _readyAt = new List<float>();

        /// <summary>Which ability components each definition fires. Empty means it is passive.</summary>
        private readonly List<List<IClassAbility>> _bindings = new List<List<IClassAbility>>();

        /// <summary>The status effect runtime on this player, lazily installed.</summary>
        private StatusHost _status;

        /// <summary>The child objects the archetype library created, so a re-pick can take them down.</summary>
        private readonly List<GameObject> _archetypeObjects = new List<GameObject>();

        /// <summary>
        /// Every archetype the library implements, keyed by the enum a class asset picks.
        /// Built once, the first time any class is equipped.
        /// </summary>
        private static readonly Dictionary<AbilityArchetype, System.Type> _archetypeTypes =
            new Dictionary<AbilityArchetype, System.Type>
            {
                // `using ProjectLEA.Manuel.Abilities.MovementAbilities;` and friends import the
                // TYPES in those nested namespaces, not their names as prefixes - so the entries
                // are the bare type names. `Namespace.Type` is still a CS0246 even with the using.
                { AbilityArchetype.Dash,       typeof(DashAbility) },
                { AbilityArchetype.Updraft,    typeof(UpdraftAbility) },
                { AbilityArchetype.Teleport,   typeof(TeleportAbility) },
                { AbilityArchetype.HealSelf,   typeof(HealSelfAbility) },
                { AbilityArchetype.ShieldSelf, typeof(ShieldSelfAbility) },
                { AbilityArchetype.BuffSelf,   typeof(BuffSelfAbility) },
                { AbilityArchetype.Decoy,      typeof(DecoyAbility) },
                { AbilityArchetype.Mine,       typeof(MineAbility) },
                { AbilityArchetype.Projectile, typeof(ProjectileAbility) },
                { AbilityArchetype.Reveal,     typeof(RevealAbility) },
                { AbilityArchetype.Zone,       typeof(ZoneAbility) }
            };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ClassAbilityHost] More than one host in the scene; removing the extra.");
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Installs a class, replacing whatever was there before.</summary>
        public void ApplyClass(ClassDefinition definition)
        {
            Clear();
            CurrentClass = definition;

            if (definition == null) return;

            EnsureStatusHost();
            BuildCooldowns(definition);

            int installed = BuildArchetypes(definition);

            // A bespoke prefab is optional, and sits on top of the archetypes. Its components
            // bind by name the same way, so a class can mix library abilities with custom ones.
            if (definition.abilityPrefab != null)
            {
                _instance = Instantiate(definition.abilityPrefab, transform);
                _instance.name = definition.abilityPrefab.name;
                _instance.transform.localPosition = Vector3.zero;
                _instance.transform.localRotation = Quaternion.identity;

                _instance.GetComponentsInChildren(true, _abilities);

                foreach (var ability in _abilities) ability.OnEquipped(gameObject);
            }

            BindAbilitiesToDefinitions();

            if (installed > 0)
                Debug.Log($"[ClassAbilityHost] '{definition.displayName}' installed {installed} " +
                          $"archetype script(s) and {_abilities.Count - installed} prefab script(s).");
            else if (_abilities.Count == 0)
                Debug.Log($"[ClassAbilityHost] '{definition.displayName}' has no ability scripts - " +
                          "set an archetype on each of its abilities, or give it a prefab.");
        }

        /// <summary>
        /// Adds one archetype component per non-passive ability. Each gets its own child object
        /// because the archetypes are single-component, and each is told the ability's display
        /// name so the key binding can find it.
        /// </summary>
        /// <returns>How many archetype scripts were installed.</returns>
        private int BuildArchetypes(ClassDefinition definition)
        {
            if (definition.abilities == null) return 0;

            int installed = 0;

            for (int i = 0; i < definition.abilities.Count; i++)
            {
                var ability = definition.abilities[i];
                if (ability == null) continue;
                if (ability.archetype == AbilityArchetype.None) continue;

                if (!_archetypeTypes.TryGetValue(ability.archetype, out var type) || type == null)
                {
                    Debug.LogWarning($"[ClassAbilityHost] '{definition.displayName}' ability " +
                                     $"'{ability.displayName}' uses archetype '{ability.archetype}', " +
                                     "which the library does not implement. It will never fire.");
                    continue;
                }

                var child = new GameObject($"Ability_{ability.archetype}_{i}");
                child.transform.SetParent(transform, false);
                child.transform.localPosition = Vector3.zero;
                _archetypeObjects.Add(child);

                var component = child.AddComponent(type) as IClassAbility;
                if (component == null) continue;

                if (component is AbilityBase archetype) archetype.SetAbilityName(ability.displayName);

                _abilities.Add(component);
                component.OnEquipped(gameObject);

                installed++;
            }

            return installed;
        }

        /// <summary>
        /// The status runtime must exist for slow, flash, suppress and pull to do anything, and
        /// it belongs on the player's own body. Installed rather than authored in the scene, so
        /// the ability system cannot silently end up without it.
        /// </summary>
        private void EnsureStatusHost()
        {
            if (_status != null) return;

            _status = GetComponent<StatusHost>();
            if (_status == null) _status = gameObject.AddComponent<StatusHost>();
        }

        /// <summary>Removes the current class's abilities.</summary>
        public void Clear()
        {
            foreach (var ability in _abilities) ability.OnUnequipped(gameObject);

            _abilities.Clear();
            _definitions.Clear();
            _readyAt.Clear();
            _bindings.Clear();

            // The archetype library owns its own child objects, which a re-pick would otherwise
            // leave in the scene with orphaned scripts still running their Update.
            foreach (var child in _archetypeObjects)
            {
                if (child != null) Destroy(child);
            }
            _archetypeObjects.Clear();

            if (_instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }

            CurrentClass = null;
        }

        private void BuildCooldowns(ClassDefinition definition)
        {
            _definitions.Clear();
            _readyAt.Clear();
            _bindings.Clear();

            if (definition.abilities == null) return;

            foreach (var ability in definition.abilities)
            {
                _definitions.Add(ability);
                _readyAt.Add(0f);   // ready immediately on pick
                _bindings.Add(new List<IClassAbility>());
            }
        }

        /// <summary>
        /// Matches each ability script to the definition it names. Unmatched scripts stay
        /// equipped but never fire, which is how passives work.
        /// </summary>
        private void BindAbilitiesToDefinitions()
        {
            for (int i = 0; i < _definitions.Count; i++)
            {
                var definition = _definitions[i];
                if (definition == null) continue;

                foreach (var ability in _abilities)
                {
                    if (ability == null) continue;
                    if (string.IsNullOrEmpty(ability.AbilityName)) continue;

                    if (string.Equals(ability.AbilityName, definition.displayName,
                                      System.StringComparison.OrdinalIgnoreCase))
                    {
                        _bindings[i].Add(ability);
                    }
                }
            }

            // Say so when a script names an ability the class does not have, because the
            // symptom otherwise is simply "the key does nothing".
            foreach (var ability in _abilities)
            {
                if (ability == null || string.IsNullOrEmpty(ability.AbilityName)) continue;

                bool matched = false;
                foreach (var definition in _definitions)
                {
                    if (definition == null) continue;
                    if (string.Equals(ability.AbilityName, definition.displayName,
                                      System.StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    Debug.LogWarning($"[ClassAbilityHost] '{ability.GetType().Name}' declares ability " +
                                     $"'{ability.AbilityName}', but '{CurrentClass?.displayName}' has no " +
                                     "ability with that name - it will never fire.");
                }
            }
        }

        private void Update()
        {
            if (_definitions.Count == 0) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < _definitions.Count; i++)
            {
                var definition = _definitions[i];
                if (definition == null) continue;

                // The cheat ignores the timer rather than zeroing it, so turning it off
                // mid-match leaves whatever cooldown was actually running intact.
                if (!MatchCheats.infiniteAbilities && Time.time < _readyAt[i]) continue;
                if (!keyboard[KeyBindings.GetAbilityKey(CurrentClass, definition.activationKey)].wasPressedThisFrame) continue;

                // A suppressed player cannot cast. Read every press rather than caching a flag,
                // because a suppress that ends this frame should let the very next key through.
                if (_status != null && _status.Suppressed)
                {
                    Debug.Log($"[ClassAbilityHost] '{definition.displayName}' blocked - the player is suppressed.");
                    continue;
                }

                // Clicks while a full-screen UI is up are menu clicks. Those screens freeze
                // match time but Update still runs, so without this every ability key would
                // also fire while the player is shopping or picking a class.
                if (MatchUiPause.IsPaused) return;

                _readyAt[i] = Time.time + definition.cooldown;

                // Only the scripts that claim this ability fire.
                foreach (var ability in _bindings[i]) ability.OnActivated(gameObject, definition);

                Debug.Log($"[ClassAbilityHost] Activated '{definition.displayName}'.");
            }
        }

        /// <summary>Seconds until the ability at this index is usable again. 0 when ready.</summary>
        public float GetCooldownRemaining(int index)
        {
            if (index < 0 || index >= _readyAt.Count) return 0f;
            return Mathf.Max(0f, _readyAt[index] - Time.time);
        }

        /// <summary>0 when ready, 1 immediately after use. Handy for a HUD.</summary>
        public float GetCooldownNormalised(int index)
        {
            if (index < 0 || index >= _readyAt.Count) return 0f;

            var definition = _definitions[index];
            if (definition == null || definition.cooldown <= 0f) return 0f;

            return Mathf.Clamp01(GetCooldownRemaining(index) / definition.cooldown);
        }
    }
}
