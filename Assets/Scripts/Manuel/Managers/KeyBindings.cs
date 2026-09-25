using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Persisted key bindings: an action name to the key the player wants for it.
    ///
    /// Everything the player can press in the match reads through here rather than through a
    /// hardcoded <see cref="Key"/>, so the settings menu can rebind it and every consumer sees
    /// the change on the next frame without anything having to be told about it.
    ///
    /// Bindings are stored in PlayerPrefs as JSON. That is deliberately not a ScriptableObject:
    /// a rebinding is the player's preference, not project data, and it has to survive a build
    /// and be writable from the standalone player.
    ///
    /// Ability keys are a special case. They belong to a class, not to the player - a dash class
    /// casts on Q, a trap class on E - so they are stored under the class's own name and fall
    /// back to the class asset when the player has not overridden them.
    /// </summary>
    public static class KeyBindings
    {
        private const string PlayerPrefsKey = "ProjectLEA.KeyBindings";

        /// <summary>
        /// The keyboard actions the settings menu offers. Mouse buttons are not in here: fire
        /// and aim stay on the mouse, and the settings key stays on Escape so the rebind screen
        /// always has a way out.
        /// </summary>
        public static readonly string[] Actions =
        {
            "Move Forward",
            "Move Back",
            "Move Left",
            "Move Right",
            "Jump",
            "Crouch",
            "Reload",
            "Shop",
            "Scoreboard"
        };

        /// <summary>The default key for each action, in the same order as <see cref="Actions"/>.</summary>
        private static readonly Key[] Defaults =
        {
            Key.W,
            Key.S,
            Key.A,
            Key.D,
            Key.Space,
            Key.LeftCtrl,
            Key.R,
            Key.B,
            Key.Tab
        };

        [Serializable]
        private struct Entry
        {
            public string action;
            public int key; // UnityEngine.InputSystem.Key as an int
        }

        [Serializable]
        private class SaveData
        {
            public Entry[] entries;
        }

        private static readonly Dictionary<string, Key> _bindings = new Dictionary<string, Key>();
        private static bool _loaded;

        /// <summary>
        /// Raised after a binding changes. Consumers that cache a key should re-read on this
        /// rather than polling, though most simply read <see cref="Get"/> when they need it.
        /// </summary>
        public static event Action OnBindingsChanged;

        static KeyBindings()
        {
            Load();
        }

        /// <summary>The key bound to the action, or the fallback when the player never set one.</summary>
        public static Key Get(string action, Key fallback)
        {
            Load();

            if (!_bindings.TryGetValue(action, out Key key)) return fallback;
            return key == Key.None ? fallback : key;
        }

        /// <summary>The default key an action ships with, so the menu can show it.</summary>
        public static Key GetDefault(string action)
        {
            int index = Array.IndexOf(Actions, action);
            return index >= 0 && index < Defaults.Length ? Defaults[index] : Key.None;
        }

        /// <summary>
        /// The key an ability fires on. A class's own key is the default; a player override
        /// wins when there is one, so rebinding "dash" does not require editing 29 class assets.
        /// </summary>
        public static Key GetAbilityKey(ClassDefinition definition, Key fallback)
        {
            if (definition == null) return fallback;
            return Get(AbilityAction(definition), fallback);
        }

        /// <summary>The stored action name for a class's ability key.</summary>
        public static string AbilityAction(ClassDefinition definition)
        {
            return definition != null ? $"{definition.displayName} ability" : string.Empty;
        }

        /// <summary>Records a binding and saves it immediately.</summary>
        public static void Set(string action, Key key)
        {
            Load();

            bool changed = !_bindings.TryGetValue(action, out Key existing) || existing != key;

            _bindings[action] = key;

            if (changed)
            {
                Save();
                OnBindingsChanged?.Invoke();
            }
        }

        /// <summary>Every binding the player changed, shown as "Action: Key" lines.</summary>
        public static IEnumerable<string> DescribeOverrides()
        {
            Load();

            foreach (var pair in _bindings)
            {
                if (pair.Value == Key.None) continue;
                yield return $"{pair.Key}: {pair.Value}";
            }
        }

        /// <summary>Clears every override and saves, so all actions fall back to their defaults.</summary>
        public static void Reset()
        {
            Load();

            if (_bindings.Count == 0) return;

            _bindings.Clear();
            Save();
            OnBindingsChanged?.Invoke();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
                if (string.IsNullOrEmpty(json)) return;

                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null || data.entries == null) return;

                foreach (var entry in data.entries)
                {
                    if (string.IsNullOrEmpty(entry.action)) continue;

                    var key = (Key)entry.key;
                    if (key == Key.None) continue;

                    _bindings[entry.action] = key;
                }
            }
            catch (Exception e)
            {
                // A corrupt or hand-edited preference must not take the controls with it.
                Debug.LogWarning($"[KeyBindings] Could not read the saved bindings ({e.Message}); " +
                                 "using the defaults.");
                _bindings.Clear();
            }
        }

        private static void Save()
        {
            var entries = new List<Entry>(_bindings.Count);

            foreach (var pair in _bindings)
            {
                if (pair.Value == Key.None) continue;
                entries.Add(new Entry { action = pair.Key, key = (int)pair.Value });
            }

            var data = new SaveData { entries = entries.ToArray() };

            try
            {
                PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(data));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KeyBindings] Could not save the bindings ({e.Message}).");
            }
        }
    }
}
