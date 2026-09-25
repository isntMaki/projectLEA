using System.IO;
using ProjectLEA.Manuel.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectLEA.Manuel.EditorTools
{
    /// <summary>
    /// Places the manager stack in the Manuel scene so its data is serialized and editable.
    ///
    /// This has to be a scene edit rather than runtime creation: a component added with
    /// AddComponent gets default field values, so a runtime-created WeaponManager would have
    /// an empty weapon list no matter what you typed into the Inspector. Authoring them into
    /// the scene is what makes "add a weapon in the Inspector" actually work.
    ///
    /// Menu: Tools > Manuel > Create Managers
    ///
    /// Safe to run again - it only adds components that are missing, so existing data and
    /// anything you have filled in is left alone.
    /// </summary>
    public static class ManagersSetup
    {
        private const string ScenePath = "Assets/Scenes/Manuel.unity";
        private const string MarkerPath = "ProjectSettings/ManagersSetup.done";
        private const string RootName = "[Managers]";

        [MenuItem("Tools/Manuel/Create Managers")]
        public static void RunMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            Apply(scene);
        }

        /// <summary>Prompt-free variant used by the automatic run.</summary>
        public static void RunAuto()
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Apply(scene);
        }

        private static void Apply(Scene scene)
        {
            var root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Debug.Log($"[ManagersSetup] Created '{RootName}'.");
            }

            // Add order is Awake order, so the catalogues come first.
            AddIfMissing<EconomyManager>(root);
            AddIfMissing<WeaponManager>(root);
            AddIfMissing<ClassManager>(root);
            AddIfMissing<ScoreManager>(root);
            AddIfMissing<SpawnManager>(root);
            AddIfMissing<RoundManager>(root);
            AddIfMissing<GameManager>(root);
            AddIfMissing<LoadoutUI>(root);
            AddIfMissing<MatchHud>(root);
            AddIfMissing<SettingsMenu>(root);

            ConfigurePlayer();
            CreatePlayerTwo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ManagersSetup] '{RootName}' is in the scene. Select it and fill in the " +
                      "weapon catalogue and class roster in the Inspector.");

            File.WriteAllText(MarkerPath,
                "Managers created " + System.DateTime.Now.ToString("u") + System.Environment.NewLine);
        }

        private static void AddIfMissing<T>(GameObject root) where T : Component
        {
            if (root.GetComponent<T>() != null) return;

            root.AddComponent<T>();
            Debug.Log($"[ManagersSetup] Added {typeof(T).Name}.");
        }

        private static void ConfigurePlayer()
        {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogWarning("[ManagersSetup] No object named 'Player' - the player was left " +
                                 "without a health bar or the ability to attack.");
                return;
            }

            // Everything the player needs to fight. Each is only added when missing, so
            // re-running this never duplicates or overwrites tuned values.
            AddIfMissing<ClassAbilityHost>(player);
            AddIfMissing<WeaponUser>(player);
            AddIfMissing<WeaponViewModel>(player);
            AddIfMissing<Health>(player);

            // Mark the player's health as a player's, so reaching zero awards a point.
            var health = player.GetComponent<Health>();
            if (health == null) return;

            var serialized = new SerializedObject(health);

            var isPlayerProp = serialized.FindProperty("isPlayer");
            if (isPlayerProp != null) isPlayerProp.boolValue = true;

            var slotProp = serialized.FindProperty("slot");
            if (slotProp != null) slotProp.enumValueIndex = (int)PlayerSlot.One;

            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// Puts the stand-in second player in the scene. It is a damageable body registered
        /// as player two, so the round loop has an opponent to score kills against.
        /// </summary>
        private static void CreatePlayerTwo()
        {
            var existing = GameObject.Find("PlayerTwo");
            if (existing != null) return;

            var playerTwo = new GameObject("PlayerTwo");
            playerTwo.transform.position = new Vector3(12f, 1f, 0f);

            var health = playerTwo.AddComponent<Health>();
            var serialized = new SerializedObject(health);

            var isPlayerProp = serialized.FindProperty("isPlayer");
            if (isPlayerProp != null) isPlayerProp.boolValue = true;

            var slotProp = serialized.FindProperty("slot");
            if (slotProp != null) slotProp.enumValueIndex = (int)PlayerSlot.Two;

            serialized.ApplyModifiedProperties();

            playerTwo.AddComponent<PlayerTwoDummy>();

            Debug.Log("[ManagersSetup] Created 'PlayerTwo' at (12,1,0) as the stand-in opponent.");
        }

        /// <summary>
        /// Drops a training target into the scene so weapons have something to hit.
        /// Separate from the main setup because it is a testing aid, not part of the game.
        /// </summary>
        [MenuItem("Tools/Manuel/Create Training Target")]
        public static void CreateTrainingTarget()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var existing = GameObject.Find("TrainingTarget");
            if (existing != null)
            {
                Debug.Log("[ManagersSetup] A TrainingTarget is already in the scene.");
                Selection.activeGameObject = existing;
                return;
            }

            var target = new GameObject("TrainingTarget");
            target.transform.position = new Vector3(0f, 1f, 6f);

            var health = target.AddComponent<Health>();
            var serialized = new SerializedObject(health);
            var isPlayerProp = serialized.FindProperty("isPlayer");
            if (isPlayerProp != null) isPlayerProp.boolValue = false;
            serialized.ApplyModifiedProperties();

            target.AddComponent<TrainingTarget>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Selection.activeGameObject = target;
            Debug.Log("[ManagersSetup] Created a TrainingTarget at (0,1,6). Press T in play to clear its readout.");
        }
    }

    /// <summary>Runs the setup once, automatically, the first time this file is compiled.</summary>
    [InitializeOnLoad]
    public static class ManagersSetupAuto
    {
        private static bool _busy;

        static ManagersSetupAuto()
        {
            if (File.Exists(MarkerPath)) return;
            EditorApplication.update += Tick;
        }

        private const string MarkerPath = "ProjectSettings/ManagersSetup.done";

        private static void Tick()
        {
            if (_busy) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EditorApplication.update -= Tick;
            _busy = true;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    ManagersSetup.RunAuto();
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[ManagersSetup] Automatic setup failed. Use Tools > Manuel > " +
                                   "Create Managers instead. " + e);
                }
            };
        }
    }
}
