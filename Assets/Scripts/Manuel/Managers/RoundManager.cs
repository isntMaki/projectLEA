using System;
using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Owns a single round: standing both players back up when it starts, and tearing it
    /// down when someone scores.
    ///
    /// The sprint currently has one player object, so slot Two has nothing registered and
    /// respawns are skipped for it. The logic already handles both slots, so adding the
    /// second player later is just a <see cref="RegisterPlayer"/> call.
    /// </summary>
    public class RoundManager : ManagerBase<RoundManager>
    {
        private readonly Transform[] _players = new Transform[2];

        /// <summary>1-based. Zero until the first round starts.</summary>
        public int RoundNumber { get; private set; }

        public bool IsRoundActive { get; private set; }

        /// <summary>Raised with the round number that just began.</summary>
        public event Action<int> OnRoundStarted;

        /// <summary>Raised with the round number and the slot that won it.</summary>
        public event Action<int, PlayerSlot> OnRoundEnded;

        /// <summary>
        /// On a client, this machine's own player occupies the REMOTE spawn point in world
        /// space. Both machines run the same scene with the same "slot One = me" mapping, so
        /// without this the client's real body and the host's body would both stand on slot
        /// One's spawn and the two players would end up on top of each other. The host's body
        /// is at slot One's spawn, the client's is at slot Two's, and both machines then agree
        /// on where every body is. Set by the network layer; offline it stays false.
        /// </summary>
        public bool LocalPlayerUsesRemoteSpawn { private get; set; }

        protected override void OnManagerAwake()
        {
            // Slot One is the local player. Slot Two stays empty until a second player
            // object exists to register.
            var player = GameObject.Find("Player");
            if (player != null)
            {
                RegisterPlayer(PlayerSlot.One, player.transform);
            }
            else
            {
                Debug.LogWarning("[RoundManager] No object named 'Player' - slot One has no body to respawn.");
            }
        }

        public void RegisterPlayer(PlayerSlot slot, Transform player)
        {
            _players[(int)slot] = player;
        }

        public Transform GetPlayer(PlayerSlot slot) => _players[(int)slot];

        /// <summary>Clears the round counter. Called when a fresh match begins.</summary>
        public void ResetRounds()
        {
            RoundNumber = 0;
            IsRoundActive = false;
        }

        public void StartRound()
        {
            RoundNumber++;
            IsRoundActive = true;

            RespawnAll();

            Debug.Log($"[RoundManager] Round {RoundNumber} started.");
            OnRoundStarted?.Invoke(RoundNumber);
        }

        public void EndRound(PlayerSlot winner)
        {
            if (!IsRoundActive) return;

            IsRoundActive = false;
            Debug.Log($"[RoundManager] Round {RoundNumber} won by Player {(int)winner + 1}.");
            OnRoundEnded?.Invoke(RoundNumber, winner);
        }

        public void RespawnAll()
        {
            for (int i = 0; i < _players.Length; i++)
                Respawn((PlayerSlot)i);
        }

        /// <summary>Moves a player to their spawn point and turns them toward the centre.</summary>
        public void Respawn(PlayerSlot slot)
        {
            var player = _players[(int)slot];
            if (player == null) return;

            // See LocalPlayerUsesRemoteSpawn: a client's own body uses the other slot's spawn
            // point so the two machines agree on world positions. The host's proxy is mirrored
            // the other way, so it starts where the host's real body actually is and the
            // network does not have to drag it across the arena at every round start.
            PlayerSlot spawnSlot = slot;
            if (LocalPlayerUsesRemoteSpawn)
                spawnSlot = slot == PlayerSlot.One ? PlayerSlot.Two : PlayerSlot.One;

            Vector3 destination = SpawnManager.Exists
                ? SpawnManager.Instance.GetSpawnPosition(spawnSlot)
                : new Vector3((int)spawnSlot == 0 ? -8f : 8f, 1.1f, 0f);

            // A CharacterController fights direct transform writes, so park it for the move.
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            player.position = destination;

            Vector3 toCentre = -destination;
            toCentre.y = 0f;
            if (toCentre.sqrMagnitude > 0.0001f)
                player.rotation = Quaternion.LookRotation(toCentre.normalized, Vector3.up);

            if (controller != null) controller.enabled = true;

            // Standing back up means standing back up at full health.
            var health = player.GetComponent<Health>();
            if (health != null) health.ResetToFull();
        }
    }
}
