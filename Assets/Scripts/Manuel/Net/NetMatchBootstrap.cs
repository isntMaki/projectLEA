using ProjectLEA.Manuel.Managers;
using ProjectLEA.Manuel.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectLEA.Manuel.Bootstrap
{
    /// <summary>
    /// Adds the in-match network layer to a match scene at runtime.
    ///
    /// The match scene is authored as a single-player scene - it has no NetMatchSync on it,
    /// and it should not need one, because a scene you open directly for offline testing must
    /// still work. So instead of baking the component into the scene, this bootstrap watches
    /// for a match scene coming up and installs the layer only when a network session is
    /// running.
    ///
    /// It deliberately does nothing when there is no session: a plain F5 into the match scene
    /// gives you the offline prototype, exactly as before.
    ///
    /// TIMING GOTCHA: <c>RuntimeInitializeOnLoadMethod</c> runs exactly ONCE, at startup, and
    /// <c>AfterSceneLoad</c> means "after the FIRST scene" - which is the main menu. A match
    /// loaded later with <c>SceneManager.LoadScene</c> never triggers it, so installing from
    /// that callback alone silently never happens. The startup hook therefore subscribes to
    /// <see cref="SceneManager.sceneLoaded"/>, which does fire for every load after the first.
    /// </summary>
    public static class NetMatchBootstrap
    {
        /// <summary>Scenes that get the network layer. Others - the menu, the lobby - do not.</summary>
        private static readonly string[] MatchScenes = { "Manuel" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void WatchSceneLoads()
        {
            // Subscribing once, early, so every SceneManager.LoadScene after this is checked.
            // The handler is idempotent - a scene already carrying a NetMatchSync is skipped -
            // so it is safe for it to fire for the menu as well.
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>Catches the case where the game is launched straight into a match scene.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallOnFirstScene()
        {
            Install(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Install(scene);
        }

        private static void Install(Scene scene)
        {
            if (!LobbyNetwork.IsActive)
            {
                // Offline. Nothing to wire, and adding a NetMatchSync now would spend the
                // match looking for a host that does not exist.
                return;
            }

            if (System.Array.IndexOf(MatchScenes, scene.name) < 0) return;

            // The managers root is where the rest of the match logic lives, so the network
            // layer goes there too rather than on its own stray object.
            var root = GameObject.Find(ManagersBootstrap.ManagerObjectName);
            if (root == null)
            {
                Debug.LogWarning("[NetMatchBootstrap] No [Managers] object in the scene; " +
                                 "the network layer has nowhere to sit.");
                return;
            }

            if (root.GetComponent<NetMatchSync>() != null) return;

            root.AddComponent<NetMatchSync>();
            Debug.Log($"[NetMatchBootstrap] Installed NetMatchSync for a networked match in '{scene.name}'.");
        }
    }
}
