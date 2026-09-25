using System.Collections.Generic;
using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Owns the arena's spawn points and chooses one on respawn.
    ///
    /// Spawn points are any scene objects whose name starts with <see cref="spawnPointName"/>.
    /// If the scene has none - which is the case right now, since the arena is empty - it
    /// falls back to two generated points on opposite sides of the origin so the game stays
    /// playable before any geometry exists.
    /// </summary>
    public class SpawnManager : ManagerBase<SpawnManager>
    {
        [Tooltip("Scene objects whose name starts with this are treated as spawn points.")]
        [SerializeField] private string spawnPointName = "SpawnPoint";

        [Tooltip("How far above the spawn point to place the player, so they drop onto the floor.")]
        [SerializeField] private float spawnHeight = 1.1f;

        [Tooltip("Separation of the two generated fallback spawns, either side of the origin.")]
        [SerializeField] private float fallbackRadius = 8f;

        private readonly List<Transform> _points = new List<Transform>();

        public int SpawnPointCount => _points.Count;

        protected override void OnManagerAwake()
        {
            DiscoverSpawnPoints();
        }

        private void DiscoverSpawnPoints()
        {
            _points.Clear();

            foreach (var candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (candidate.name.StartsWith(spawnPointName)) _points.Add(candidate);
            }

            if (_points.Count == 0)
            {
                Debug.Log($"[SpawnManager] No '{spawnPointName}*' objects in the scene; " +
                          "using two generated spawns either side of the origin.");
                return;
            }

            // Slot N uses point N, so the order has to be deterministic or a re-ordering of the
            // hierarchy would silently swap which body spawns where - and on a client that is
            // exactly the bug that puts both players on the same point.
            _points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            Debug.Log($"[SpawnManager] Found {_points.Count} spawn point(s): " +
                      $"{string.Join(", ", _points.ConvertAll(p => p.name))}.");
        }

        /// <summary>Where the given slot should respawn.</summary>
        public Vector3 GetSpawnPosition(PlayerSlot slot)
        {
            if (_points.Count > 0)
            {
                var point = _points[(int)slot % _points.Count];
                return point.position + Vector3.up * spawnHeight;
            }

            float x = (int)slot == 0 ? -fallbackRadius : fallbackRadius;
            return new Vector3(x, spawnHeight, 0f);
        }
    }
}
