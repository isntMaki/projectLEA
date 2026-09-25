using ProjectLEA.Manuel.Abilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectLEA.Manuel.EditorTools
{
    /// <summary>
    /// Scene geometry helpers for testing.
    ///
    /// Kept separate from ManagersSetup so it is obvious these are development aids rather
    /// than part of the game. All menu-driven and opt-in: nothing here runs on its own,
    /// because adding objects to the scene unasked is exactly what got in the way before.
    ///
    /// Menu: Tools > Manuel > Create Test Ground
    ///       Tools > Manuel > Create Sample Ability Prefab
    /// </summary>
    public static class SceneTools
    {
        private const string ScenePath = "Assets/Scenes/Manuel.unity";
        private const string GroundMaterialPath = "Assets/Materials/Manuel/M_TestGround.mat";
        private const string AbilityPrefabPath = "Assets/Prefabs/Manuel/SampleDashAbility.prefab";

        private const float GroundSize = 60f;
        private const float SpawnRadius = 12f;

        [MenuItem("Tools/Manuel/Create Test Ground")]
        public static void CreateTestGround()
        {
            if (!EnsureManuelSceneOpen(out var scene)) return;

            // The floor. Top surface sits exactly at y = 0 so the player's spawn height and
            // the training target's placement both stay correct.
            var existing = GameObject.Find("TestGround");
            if (existing != null)
            {
                Debug.Log("[SceneTools] A TestGround is already in the scene.");
                Selection.activeGameObject = existing;
            }
            else
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "TestGround";
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(GroundSize, 1f, GroundSize);

                var renderer = ground.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    var material = LoadOrCreateGroundMaterial();
                    if (material != null) renderer.sharedMaterial = material;
                }

                Debug.Log($"[SceneTools] Created a {GroundSize}x{GroundSize}m test ground.");
                Selection.activeGameObject = ground;
            }

            CreateSpawnPoint("SpawnPoint_A", new Vector3(-SpawnRadius, 0f, 0f));
            CreateSpawnPoint("SpawnPoint_B", new Vector3(SpawnRadius, 0f, 0f));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log("[SceneTools] Test ground ready. SpawnManager will find SpawnPoint_A and " +
                      "SpawnPoint_B, so respawns now go to opposite sides.");
        }

        [MenuItem("Tools/Manuel/Create Sample Ability Prefab")]
        public static void CreateSampleAbilityPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(AbilityPrefabPath);
            if (existing != null)
            {
                Debug.Log($"[SceneTools] {AbilityPrefabPath} already exists.");
                Selection.activeObject = existing;
                return;
            }

            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/Manuel");

            // Build it in the scene, save it as a prefab, then remove the scene copy.
            var temp = new GameObject("SampleDashAbility");
            temp.AddComponent<SampleDashAbility>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, AbilityPrefabPath);
            UnityEngine.Object.DestroyImmediate(temp);

            AssetDatabase.SaveAssets();

            Selection.activeObject = prefab;
            Debug.Log($"[SceneTools] Created {AbilityPrefabPath}. Assign it to a class's " +
                      "'Ability Prefab' slot, and make sure the class has an ability whose " +
                      "display name matches the script's Ability Name field.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private static void CreateSpawnPoint(string name, Vector3 position)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
            {
                Debug.Log($"[SceneTools] '{name}' already exists; leaving it alone.");
                return;
            }

            var spawn = new GameObject(name);
            spawn.transform.position = position;

            Debug.Log($"[SceneTools] Created '{name}' at {position}.");
        }

        private static Material LoadOrCreateGroundMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogWarning("[SceneTools] No usable shader found for the ground material.");
                return null;
            }

            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Materials/Manuel");

            var material = new Material(shader) { color = new Color(0.28f, 0.30f, 0.34f) };
            AssetDatabase.CreateAsset(material, GroundMaterialPath);

            Debug.Log($"[SceneTools] Created {GroundMaterialPath}.");
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int lastSlash = path.LastIndexOf('/');
            if (lastSlash <= 0) return;

            string parent = path.Substring(0, lastSlash);
            string leaf = path.Substring(lastSlash + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static bool EnsureManuelSceneOpen(out Scene scene)
        {
            scene = SceneManager.GetActiveScene();
            if (scene.path == ScenePath) return true;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return true;
        }
    }
}
