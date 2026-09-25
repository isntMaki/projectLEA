using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Owns the class roster and which class is currently picked.
    ///
    /// The design calls for a mandatory re-pick after every point, so this listens for a
    /// point being scored and clears the selection. That is the whole reason a class is a
    /// per-round choice rather than a permanent one.
    ///
    /// Picking a class applies its stats to the player and hands its ability prefab to
    /// <see cref="ClassAbilityHost"/>, which is where the class's actual ability scripts run.
    /// </summary>
    public class ClassManager : ManagerBase<ClassManager>
    {
        [Tooltip("Every playable class. Add entries here.")]
        [SerializeField] private List<ClassDefinition> classes = new List<ClassDefinition>();

        [Tooltip("Fill the roster with throwaway sample classes when it is empty, purely so the " +
                 "loadout screen has something to show. Stops the moment you add a real entry.")]
        [SerializeField] private bool seedTestRosterWhenEmpty = true;

        [Tooltip("Clear the pick whenever a point is scored, forcing a fresh choice each round.")]
        [SerializeField] private bool forceRepickEachRound = true;

        /// <summary>Index into <see cref="Classes"/>, or -1 when nothing is picked.</summary>
        public int SelectedIndex { get; private set; } = -1;

        public IReadOnlyList<ClassDefinition> Classes => classes;

        public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < classes.Count;

        public ClassDefinition Selected => HasSelection ? classes[SelectedIndex] : null;

        /// <summary>Raised with the newly picked class.</summary>
        public event Action<ClassDefinition> OnClassSelected;

        /// <summary>Raised when the pick is dropped, so the UI knows to demand a new one.</summary>
        public event Action OnSelectionCleared;

        private bool _subscribed;

        protected override void OnManagerAwake()
        {
            if (classes.Count == 0 && seedTestRosterWhenEmpty)
            {
                SeedTestRoster();
                Debug.LogWarning("[ClassManager] The roster was empty, so it was filled with " +
                                 "throwaway test classes just so the loadout screen has something to " +
                                 "show. They vanish as soon as you add a real entry in the Inspector.");
            }

            if (classes.Count == 0)
            {
                Debug.LogWarning("[ClassManager] The class roster is empty. " +
                                 "Add classes in the Inspector to populate the loadout screen.");
            }
        }

        /// <summary>
        /// Two throwaway classes, deliberately mirroring each other so the passive/negative
        /// trade-off is visible at a glance. Not serialized - these only exist at runtime.
        /// </summary>
        private void SeedTestRoster()
        {
            // ScriptableObject.CreateInstance, not new: these are assets in spirit, just not
            // saved to disk. Real ones are made with Tools > Manuel > Loadout.
            classes.Add(TestClass(c =>
            {
                c.displayName = "Test Class Alpha";
                c.description = "Throwaway class - delete once real classes exist.";
                c.passiveTrait = "25% faster movement";
                c.negativeTrait = "25% less max health";
                c.maxHealth = 75f;
                c.moveSpeedMultiplier = 1.25f;
                c.abilities = new List<AbilityDefinition>
                {
                    new AbilityDefinition { displayName = "Test Dash", cooldown = 6f, power = 8f },
                    new AbilityDefinition { displayName = "Test Focus", cooldown = 12f, duration = 4f, power = 20f }
                };
            }));

            classes.Add(TestClass(c =>
            {
                c.displayName = "Test Class Beta";
                c.description = "Throwaway class - delete once real classes exist.";
                c.passiveTrait = "40% more max health";
                c.negativeTrait = "15% slower movement";
                c.maxHealth = 140f;
                c.moveSpeedMultiplier = 0.85f;
                c.abilities = new List<AbilityDefinition>
                {
                    new AbilityDefinition { displayName = "Test Brace", cooldown = 10f, duration = 3f, power = 30f }
                };
            }));
        }

        /// <summary>Builds an unsaved class instance for the throwaway test roster.</summary>
        private static ClassDefinition TestClass(System.Action<ClassDefinition> configure)
        {
            var definition = ScriptableObject.CreateInstance<ClassDefinition>();
            configure(definition);
            definition.name = definition.displayName;
            return definition;
        }

        private void Start()
        {
            Subscribe();
        }

        // Must override, not shadow: a new private OnDestroy would hide the base and the
        // singleton slot would never be cleared.
        protected override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (ScoreManager.Exists) ScoreManager.Instance.OnRoundScored += HandleRoundScored;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (ScoreManager.Exists) ScoreManager.Instance.OnRoundScored -= HandleRoundScored;
            _subscribed = false;
        }

        private void HandleRoundScored(PlayerSlot slot, int total)
        {
            if (!forceRepickEachRound) return;

            Debug.Log("[ClassManager] Round scored - clearing the class pick, a new one is required.");
            ClearSelection();
        }

        public bool IsValidIndex(int index) => index >= 0 && index < classes.Count;

        public ClassDefinition Get(int index) => IsValidIndex(index) ? classes[index] : null;

        /// <summary>Picks a class, applies its stats and installs its abilities.</summary>
        public bool SelectClass(int index)
        {
            if (!IsValidIndex(index)) return false;

            SelectedIndex = index;
            var definition = classes[index];

            // A roster slot that resolves to nothing is a broken or missing ScriptableObject
            // reference in the scene. Picking it anyway null-refs further down the line and
            // takes the whole match-start with it, so it is reported and refused instead.
            if (definition == null)
            {
                SelectedIndex = -1;
                Debug.LogError($"[ClassManager] Roster slot {index} is empty - the class asset " +
                               "behind it is missing or its reference is broken. Pick refused.");
                return false;
            }

            ApplyToPlayer(definition);

            Debug.Log($"[ClassManager] Picked '{definition.displayName}' " +
                      $"(+{definition.passiveTrait} / -{definition.negativeTrait}).");

            OnClassSelected?.Invoke(definition);
            return true;
        }

        /// <summary>Drops the current pick and removes the class's abilities.</summary>
        public void ClearSelection()
        {
            SelectedIndex = -1;

            if (ClassAbilityHost.Exists) ClassAbilityHost.Instance.Clear();

            if (WeaponManager.Exists) WeaponManager.Instance.ClearEquipped();

            OnSelectionCleared?.Invoke();
        }

        private void ApplyToPlayer(ClassDefinition definition)
        {
            if (definition == null) return;

            var player = GameObject.Find("Player");
            if (player == null) return;

            if (ClassAbilityHost.Exists) ClassAbilityHost.Instance.ApplyClass(definition);

            // Max health is a class stat, so picking a class refills it. This is what makes
            // the class's maxHealth actually mean something.
            var health = player.GetComponent<Health>();
            if (health != null) health.SetMaxHealth(definition.maxHealth);

            var controller = player.GetComponent<ProjectLEA.Manuel.PlayerController>();
            if (controller != null)
            {
                controller.SpeedMultiplier = Mathf.Max(0f, definition.moveSpeedMultiplier);
            }
        }
    }
}
