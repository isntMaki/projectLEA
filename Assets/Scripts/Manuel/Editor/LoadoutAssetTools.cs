using System.Collections.Generic;
using ProjectLEA.Manuel.Managers;
using UnityEditor;
using UnityEngine;

namespace ProjectLEA.Manuel.EditorTools
{
    /// <summary>
    /// Tools for the drag-and-drop loadout workflow.
    ///
    /// Weapons and classes are ScriptableObject assets, so the normal way to work is:
    /// right-click in the Project window > Create > ProjectLEA > Weapon (or Class), tune it,
    /// then drag it into the manager's list. These menu items just take the busywork out of
    /// making the first batch, and the buttons on the managers sweep the whole project for
    /// assets you have already made.
    ///
    /// Menu: Tools > Manuel > Loadout > ...
    /// </summary>
    public static class LoadoutAssetTools
    {
        private const string WeaponFolder = "Assets/Data/Manuel/Weapons";
        private const string ClassFolder = "Assets/Data/Manuel/Classes";

        [MenuItem("Tools/Manuel/Loadout/Create Test Weapons (5 assets)")]
        public static void CreateTestWeapons()
        {
            EnsureFolder(WeaponFolder);

            var made = new List<WeaponDefinition>
            {
                CreateWeapon("Test1_Blade", w =>
                {
                    w.displayName = "Test1 Blade";
                    w.category = WeaponCategory.Melee;
                    w.description = "Throwaway melee entry - delete the asset once real weapons exist.";
                    w.cost = 0;
                    w.ownedByDefault = true;
                    w.damage = 24f;
                    w.delay = 0.55f;
                    w.windup = 0.14f;
                    w.range = 2.4f;
                    w.swingArc = 80f;
                }),

                CreateWeapon("Test2_Pistol", w =>
                {
                    w.displayName = "Test2 Pistol";
                    w.category = WeaponCategory.Gun;
                    w.description = "Throwaway gun entry - delete the asset once real weapons exist.";
                    w.cost = 150;
                    w.damage = 12f;
                    w.delay = 0.28f;
                    w.windup = 0.06f;
                    w.range = 40f;
                    w.projectileSpeed = 60f;
                    w.magazineSize = 12;
                    w.reserveAmmo = 48;
                    w.reloadTime = 1.5f;
                }),

                CreateWeapon("Test3_Bow", w =>
                {
                    w.displayName = "Test3 Bow";
                    w.category = WeaponCategory.Bow;
                    w.description = "Throwaway bow entry - delete the asset once real weapons exist.";
                    w.cost = 200;
                    w.damage = 35f;
                    w.delay = 0.9f;
                    w.windup = 0.35f;
                    w.range = 55f;
                    w.projectileSpeed = 45f;
                    w.magazineSize = 1;
                    w.reserveAmmo = 20;
                    w.reloadTime = 0.7f;
                    w.drawTime = 0.8f;
                }),

                CreateWeapon("Test4_Grenade", w =>
                {
                    w.displayName = "Test4 Grenade";
                    w.category = WeaponCategory.Thrown;
                    w.description = "Throwaway thrown entry - delete the asset once real weapons exist.";
                    w.cost = 120;
                    w.damage = 45f;
                    w.delay = 1.1f;
                    w.windup = 0.3f;
                    w.range = 25f;
                    w.projectileSpeed = 18f;
                    w.magazineSize = 1;
                    w.reserveAmmo = 3;
                    w.reloadTime = 0.6f;
                    w.fuse = 2f;
                }),

                CreateWeapon("Test5_Rocket", w =>
                {
                    w.displayName = "Test5 Rocket";
                    w.category = WeaponCategory.Launcher;
                    w.description = "Throwaway launcher entry - delete the asset once real weapons exist.";
                    w.cost = 400;
                    w.damage = 70f;
                    w.delay = 1.6f;
                    w.windup = 0.4f;
                    w.range = 60f;
                    w.projectileSpeed = 30f;
                    w.magazineSize = 1;
                    w.reserveAmmo = 4;
                    w.reloadTime = 2.2f;
                    w.splashRadius = 3.5f;
                })
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int added = AddToWeaponCatalog(made);

            Debug.Log($"[LoadoutAssetTools] Created {made.Count} weapon asset(s) in {WeaponFolder} " +
                      $"and added {added} to the WeaponManager catalogue. Select the [Managers] " +
                      "object to see them.");
        }

        [MenuItem("Tools/Manuel/Loadout/Create Test Classes (2 assets)")]
        public static void CreateTestClasses()
        {
            EnsureFolder(ClassFolder);

            var made = new List<ClassDefinition>
            {
                CreateClass("TestClass_Alpha", c =>
                {
                    c.displayName = "Test Class Alpha";
                    c.description = "Throwaway class - delete the asset once real classes exist.";
                    c.passiveTrait = "25% faster movement";
                    c.negativeTrait = "25% less max health";
                    c.maxHealth = 75f;
                    c.moveSpeedMultiplier = 1.25f;
                    c.abilities = new List<AbilityDefinition>
                    {
                        new AbilityDefinition { displayName = "Test Dash", cooldown = 6f, power = 8f },
                        new AbilityDefinition { displayName = "Test Focus", cooldown = 12f, duration = 4f, power = 20f }
                    };
                }),

                CreateClass("TestClass_Beta", c =>
                {
                    c.displayName = "Test Class Beta";
                    c.description = "Throwaway class - delete the asset once real classes exist.";
                    c.passiveTrait = "40% more max health";
                    c.negativeTrait = "15% slower movement";
                    c.maxHealth = 140f;
                    c.moveSpeedMultiplier = 0.85f;
                    c.abilities = new List<AbilityDefinition>
                    {
                        new AbilityDefinition { displayName = "Test Brace", cooldown = 10f, duration = 3f, power = 30f }
                    };
                })
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int added = AddToClassRoster(made);

            Debug.Log($"[LoadoutAssetTools] Created {made.Count} class asset(s) in {ClassFolder} " +
                      $"and added {added} to the ClassManager roster.");
        }

        // ------------------------------------------------------------------
        // Creation helpers
        // ------------------------------------------------------------------
        private static WeaponDefinition CreateWeapon(string fileName, System.Action<WeaponDefinition> configure)
        {
            string path = $"{WeaponFolder}/{fileName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (existing != null) return existing;

            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            configure(weapon);
            weapon.name = weapon.displayName;

            AssetDatabase.CreateAsset(weapon, path);
            return weapon;
        }

        private static ClassDefinition CreateClass(string fileName, System.Action<ClassDefinition> configure)
        {
            string path = $"{ClassFolder}/{fileName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<ClassDefinition>(path);
            if (existing != null) return existing;

            var definition = ScriptableObject.CreateInstance<ClassDefinition>();
            configure(definition);
            definition.name = definition.displayName;

            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        // ------------------------------------------------------------------
        // Manager list helpers - used by the menu items and the buttons below
        // ------------------------------------------------------------------
        internal static int AddToWeaponCatalog(List<WeaponDefinition> weapons)
        {
            var manager = Object.FindFirstObjectByType<WeaponManager>();
            if (manager == null)
            {
                Debug.Log("[LoadoutAssetTools] No WeaponManager in the open scene; " +
                          "drag the assets into its catalogue by hand.");
                return 0;
            }

            var serialized = new SerializedObject(manager);
            var list = serialized.FindProperty("catalog");
            if (list == null) return 0;

            int added = 0;
            foreach (var weapon in weapons)
            {
                if (weapon == null || ListContains(list, weapon)) continue;

                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = weapon;
                added++;
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            return added;
        }

        internal static int AddToClassRoster(List<ClassDefinition> definitions)
        {
            var manager = Object.FindFirstObjectByType<ClassManager>();
            if (manager == null)
            {
                Debug.Log("[LoadoutAssetTools] No ClassManager in the open scene; " +
                          "drag the assets into its roster by hand.");
                return 0;
            }

            var serialized = new SerializedObject(manager);
            var list = serialized.FindProperty("classes");
            if (list == null) return 0;

            int added = 0;
            foreach (var definition in definitions)
            {
                if (definition == null || ListContains(list, definition)) continue;

                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = definition;
                added++;
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            return added;
        }

        private static bool ListContains(SerializedProperty list, Object item)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == item) return true;
            }
            return false;
        }

        /// <summary>Every asset of a type in the project, sorted by name.</summary>
        internal static List<T> FindAllAssets<T>() where T : Object
        {
            var found = new List<T>();

            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) found.Add(asset);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int lastSlash = path.LastIndexOf('/');
            if (lastSlash <= 0) return;

            string parent = path.Substring(0, lastSlash);
            string leaf = path.Substring(lastSlash + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    /// <summary>Adds a button that sweeps the project for weapon assets already made.</summary>
    [CustomEditor(typeof(WeaponManager))]
    public class WeaponManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Drag and drop", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag weapon assets from the Project window into the catalogue above. " +
                "Right-click in the Project window > Create > ProjectLEA > Weapon to make one.",
                MessageType.None);

            if (GUILayout.Button("Add every weapon asset found in the project"))
            {
                var all = LoadoutAssetTools.FindAllAssets<WeaponDefinition>();
                int added = LoadoutAssetTools.AddToWeaponCatalog(all);
                Debug.Log($"[WeaponManagerEditor] Found {all.Count} weapon asset(s), added {added}.");
            }

            if (GUILayout.Button("Create the 5 test weapon assets"))
            {
                LoadoutAssetTools.CreateTestWeapons();
            }
        }
    }

    /// <summary>Adds a button that sweeps the project for class assets already made.</summary>
    [CustomEditor(typeof(ClassManager))]
    public class ClassManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Drag and drop", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag class assets from the Project window into the roster above. " +
                "Right-click in the Project window > Create > ProjectLEA > Class to make one.",
                MessageType.None);

            if (GUILayout.Button("Add every class asset found in the project"))
            {
                var all = LoadoutAssetTools.FindAllAssets<ClassDefinition>();
                int added = LoadoutAssetTools.AddToClassRoster(all);
                Debug.Log($"[ClassManagerEditor] Found {all.Count} class asset(s), added {added}.");
            }

            if (GUILayout.Button("Create the 2 test class assets"))
            {
                LoadoutAssetTools.CreateTestClasses();
            }
        }
    }
}
