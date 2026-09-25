using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Fallback that creates the manager stack at runtime if the scene does not already
    /// have one.
    ///
    /// IMPORTANT: managers created this way have **no authored data** - a component added
    /// with AddComponent gets default field values, so the weapon and class lists come up
    /// empty. That is why the real setup is done once by
    /// Tools > Manuel > Create Managers, which places a serialized [Managers] object in the
    /// scene that you can then fill in in the Inspector.
    ///
    /// This exists only so a scene without the managers still runs and tells you why it is
    /// empty, instead of silently doing nothing.
    /// </summary>
    public static class ManagersBootstrap
    {
        public const string ManagerObjectName = "[Managers]";

        /// <summary>
        /// Only the match scene needs a manager stack. The main menu has no player and no
        /// arena, and installing one there put "P1 0 rounds", a "WAITING / Press P to start"
        /// readout and a Tab scoreboard behind the PLAY button - the match HUD has no business
        /// in the menu at all, so this bails unless a player body is actually present.
        /// </summary>
        private static bool IsMatchScene => GameObject.Find("Player") != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureManagers()
        {
            if (!IsMatchScene) return;

            if (GameManager.Exists)
            {
                // Placed in the scene, so it carries authored data. Nothing to do.
                EnsureAbilityHost();
                return;
            }

            var root = new GameObject(ManagerObjectName);

            // Add order is Awake order. Economy and the two catalogues come first so the
            // others can reach them from their own Start().
            root.AddComponent<EconomyManager>();
            root.AddComponent<WeaponManager>();
            root.AddComponent<ClassManager>();
            root.AddComponent<ScoreManager>();
            root.AddComponent<SpawnManager>();
            root.AddComponent<RoundManager>();
            root.AddComponent<GameManager>();
            root.AddComponent<MatchHud>();
            root.AddComponent<ClassSelectUI>();
            root.AddComponent<LoadoutUI>();

            EnsureAbilityHost();

            Debug.LogWarning($"[ManagersBootstrap] No managers were in the scene, so '{ManagerObjectName}' " +
                             "was created at runtime. It has NO authored data, so the weapon and class " +
                             "lists will be empty. Run Tools > Manuel > Create Managers to place a " +
                             "serialized copy you can fill in in the Inspector.");
        }

        /// <summary>Puts the class ability host on the player if it is missing.</summary>
        private static void EnsureAbilityHost()
        {
            if (ClassAbilityHost.Exists) return;

            var player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogWarning("[ManagersBootstrap] No object named 'Player'; class abilities will not run.");
                return;
            }

            player.AddComponent<ClassAbilityHost>();
            Debug.Log("[ManagersBootstrap] Added ClassAbilityHost to the Player.");
        }
    }
}
