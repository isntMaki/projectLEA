using ProjectLEA.Manuel.Managers;
using UnityEditor;
using UnityEngine;

namespace ProjectLEA.Manuel.EditorTools
{
    /// <summary>
    /// Inspector for a weapon asset, showing only the fields that apply to its chosen
    /// category. Pick "Melee" and you get a swing arc; pick "Gun" and you get a magazine,
    /// reserve and reload. A sword can never be asked for ammunition, which is the point.
    ///
    /// If anything goes wrong this falls back to Unity's default rendering rather than
    /// leaving the Inspector broken.
    /// </summary>
    [CustomEditor(typeof(WeaponDefinition))]
    public class WeaponDefinitionEditor : Editor
    {
        /// <summary>Fields every category has.</summary>
        private static readonly string[] CoreFields =
        {
            "displayName",
            "category",
            "description",
            "cost",
            "ownedByDefault",
            "damage",
            "delay",
            "windup",
            "range"
        };

        /// <summary>Viewmodel fields, shown for every category (melee has a model too).</summary>
        private static readonly string[] ViewmodelFields = { "viewmodelPrefab", "viewmodelOffset" };

        /// <summary>The fields to show for a given category, in order.</summary>
        private static string[] FieldsFor(WeaponCategory category)
        {
            if (category == WeaponCategory.Melee)
            {
                return new[] { "swingArc", "altFire", "altDamage", "altWindup" };
            }

            // Everything else is ranged and consumes ammo.
            switch (category)
            {
                case WeaponCategory.Bow:
                    return new[] { "projectileSpeed", "magazineSize", "reserveAmmo", "reloadTime", "drawTime" };
                case WeaponCategory.Thrown:
                    return new[] { "projectileSpeed", "magazineSize", "reserveAmmo", "reloadTime", "fuse" };
                case WeaponCategory.Launcher:
                    return new[] { "projectileSpeed", "magazineSize", "reserveAmmo", "reloadTime", "splashRadius" };
                default:
                    return new[]
                    {
                        "fireMode", "burstCount", "burstInterval",
                        "pelletsPerShot", "pelletSpread",
                        "headshotMultiplier", "legshotMultiplier",
                        "falloffStart", "falloffMid", "falloffEnd", "falloffMidRatio", "falloffMinRatio",
                        "altFire", "altBurstCount", "altBurstInterval", "altDamage", "altWindup", "altSpreadScale"
                    };
            }
        }

        public override void OnInspectorGUI()
        {
            try
            {
                serializedObject.Update();

                DrawFields(CoreFields);

                var category = (WeaponCategory)serializedObject.FindProperty("category").enumValueIndex;

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(HeaderFor(category), EditorStyles.boldLabel);
                DrawFields(FieldsFor(category));

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Viewmodel", EditorStyles.boldLabel);
                DrawFields(ViewmodelFields);

                serializedObject.ApplyModifiedProperties();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WeaponDefinitionEditor] Falling back to default rendering: " + e.Message);
                DrawDefaultInspector();
            }

            // A read-only reminder of how the category affects runtime behaviour, so the
            // derived rules are visible without reading the code.
            EditorGUILayout.Space(6f);
            var weapon = (WeaponDefinition)target;
            EditorGUILayout.HelpBox(
                $"Ranged: {weapon.IsRanged}    Uses ammo: {weapon.UsesAmmo}    " +
                $"Explosive: {weapon.IsExplosive}\nShop line: {weapon.Summary()}",
                MessageType.None);
        }

        private void DrawFields(string[] names)
        {
            foreach (var name in names)
            {
                var property = serializedObject.FindProperty(name);
                if (property != null) EditorGUILayout.PropertyField(property, true);
            }
        }

        private static string HeaderFor(WeaponCategory category)
        {
            switch (category)
            {
                case WeaponCategory.Melee: return "Melee only";
                case WeaponCategory.Bow: return "Bow only";
                case WeaponCategory.Thrown: return "Thrown only";
                case WeaponCategory.Launcher: return "Launcher only";
                default: return "Gun only";
            }
        }
    }
}
