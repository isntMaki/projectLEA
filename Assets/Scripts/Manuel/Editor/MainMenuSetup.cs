using System.Collections.Generic;
using ProjectLEA.Manuel.Managers;
using ProjectLEA.Manuel.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectLEA.Manuel.EditorTools
{
    /// <summary>
    /// Creates the MainMenu scene and registers the scene order.
    ///
    /// The scene lives in the Manuel folder, so everything this project ships for the Manuel
    /// chapter - scenes, scripts, presets - is in one place.
    ///
    /// Menu-driven only - nothing runs on its own, because creating scenes unasked would be
    /// a rude thing to do to someone's project.
    ///
    /// Menu: Tools > Manuel > Create Main Menu
    /// </summary>
    public static class MainMenuSetup
    {
        private const string SceneFolder = "Assets/Scenes/Manuel";
        private const string MainMenuScenePath = "Assets/Scenes/Manuel/MainMenu.unity";
        private const string MatchScenePath = "Assets/Scenes/Manuel.unity";

        [MenuItem("Tools/Manuel/Create Main Menu")]
        public static void CreateMainMenu()
        {
            EnsureFolder(SceneFolder);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) != null)
            {
                Debug.Log($"[MainMenuSetup] {MainMenuScenePath} already exists. Registering the scene order.");
                RegisterScenes();
                EnsureStandardPreset();
                return;
            }

            // Put the user's scene back the way we found it afterwards.
            string previousScene = SceneManager.GetActiveScene().path;
            EditorSceneManager.SaveOpenScenes();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // One object is all the scene needs - the menu builds its own canvas, camera,
            // settings panel and lobby at runtime.
            var root = new GameObject("MainMenu");
            root.AddComponent<MainMenuUI>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MainMenuSetup] Created {MainMenuScenePath} with a MainMenuUI on a single object.");

            RegisterScenes();
            EnsureStandardPreset();

            if (!string.IsNullOrEmpty(previousScene) && previousScene != MainMenuScenePath)
            {
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            }

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// Creates the Standard preset the Create screen offers by default, if it is missing.
        /// Without one the lobby can still host - it falls back to the managers' own numbers -
        /// but the player would be choosing between game modes that do not exist.
        /// </summary>
        [MenuItem("Tools/Manuel/Create Standard Preset")]
        public static void EnsureStandardPreset()
        {
            const string presetPath = "Assets/Resources/Manuel/Presets/Standard.asset";

            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Manuel");
            EnsureFolder("Assets/Resources/Manuel/Presets");

            if (AssetDatabase.LoadAssetAtPath<MatchPreset>(presetPath) != null) return;

            var preset = ScriptableObject.CreateInstance<MatchPreset>();
            preset.name = "Standard";
            AssetDatabase.CreateAsset(preset, presetPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MainMenuSetup] Created the Standard preset at {presetPath}.");
        }

        [MenuItem("Tools/Manuel/Register Scenes In Build Settings")]
        public static void RegisterScenes()
        {
            var wanted = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(MatchScenePath, true)
            };

            // Keep anything else that was already registered, so this does not quietly
            // unregister scenes the user added themselves.
            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path == MainMenuScenePath || existing.path == MatchScenePath) continue;
                wanted.Add(existing);
            }

            EditorBuildSettings.scenes = wanted.ToArray();

            PinPlayModeScene();

            Debug.Log("[MainMenuSetup] Build Settings scene order: MainMenu first, then Manuel. " +
                      "Play also starts at MainMenu regardless of which scene is open.");
        }

        /// <summary>
        /// Pins the editor's Play button to the main menu.
        ///
        /// Unity runs the scene that is open in the editor when you press Play, not scene 0.
        /// That is fine for testing the match, but it is what made the main menu look like it
        /// had been replaced: the setup tools leave Manuel.unity open, so Play dropped straight
        /// into the arena. Pinning the start scene means Play always lands on the menu while the
        /// open scene is still there to work on.
        /// </summary>
        [MenuItem("Tools/Manuel/Pin Play Start Scene")]
        public static void PinPlayModeScene()
        {
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath);
            if (menu == null) return;

            if (EditorSceneManager.playModeStartScene == menu) return;

            EditorSceneManager.playModeStartScene = menu;
            Debug.Log($"[MainMenuSetup] Play mode start scene pinned to {MainMenuScenePath}.");
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
    }

    /// <summary>
    /// Pins the Play start scene after every domain reload. Editor preferences normally survive a
    /// reload, but the pin is cheap and idempotent, and running it on delayCall means a fresh
    /// checkout or a cleared preference still lands Play on the menu - the menu is the front door,
    /// and Play should always walk through it.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayStartScenePin
    {
        static PlayStartScenePin()
        {
            EditorApplication.delayCall += MainMenuSetup.PinPlayModeScene;
        }
    }
}
