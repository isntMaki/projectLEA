using System;
using ProjectLEA.Manuel.Net;
using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>Which of the two players a call refers to.</summary>
    public enum PlayerSlot
    {
        One = 0,
        Two = 1
    }

    /// <summary>
    /// Owns the score. The single authority for who is winning.
    ///
    /// The model is Valorant's without the objective: each round won is a round on the board
    /// and first to <see cref="roundsToWin"/> wins the match. Kills and deaths are tracked for
    /// the scoreboard, but they do not convert into anything - a round is won by being the last
    /// one standing, not by a kill count.
    ///
    /// Loss streaks live here because the payout depends on them - a player who keeps losing
    /// gets progressively more credits so the match does not run away from them.
    /// </summary>
    public class ScoreManager : ManagerBase<ScoreManager>
    {
        [Tooltip("Rounds a player needs to win the match.")]
        [Min(1)]
        [SerializeField] private int roundsToWin = 13;

        private readonly int[] _roundsWon = new int[2];
        private readonly int[] _kills = new int[2];
        private readonly int[] _deaths = new int[2];
        private readonly int[] _lossStreak = new int[2];

        public int RoundsToWin => roundsToWin;

        /// <summary>Applies a game-mode preset. See <see cref="GameManager.ApplyPreset"/>.</summary>
        public void ApplyPreset(MatchPreset preset)
        {
            if (preset == null) return;
            roundsToWin = preset.roundsToWin;
        }

        public bool HasWinner { get; private set; }
        public PlayerSlot Winner { get; private set; }

        /// <summary>Raised with the slot that got the kill and their new kill count.</summary>
        public event Action<PlayerSlot, int> OnKillScored;

        /// <summary>
        /// Raised with the slot that won a round and their new round total. Replaces the old
        /// OnPointScored - a round is the unit of progress now, not a kill threshold.
        /// </summary>
        public event Action<PlayerSlot, int> OnRoundScored;

        /// <summary>Raised once, when a slot reaches the win condition.</summary>
        public event Action<PlayerSlot> OnMatchWon;

        public int GetRoundsWon(PlayerSlot slot) => _roundsWon[(int)slot];
        public int GetKills(PlayerSlot slot) => _kills[(int)slot];
        public int GetDeaths(PlayerSlot slot) => _deaths[(int)slot];
        public int GetLossStreak(PlayerSlot slot) => _lossStreak[(int)slot];

        /// <summary>Kills divided by deaths, as a ratio. Zero deaths reads as the kill count.</summary>
        public float GetKdr(PlayerSlot slot)
        {
            int deaths = _deaths[(int)slot];
            return deaths <= 0 ? _kills[(int)slot] : (float)_kills[(int)slot] / deaths;
        }

        /// <summary>Rounds won by both players added together - how far into the match we are.</summary>
        public int TotalRoundsPlayed => _roundsWon[0] + _roundsWon[1];

        /// <summary>
        /// Records a kill for the given slot. A kill is bookkeeping only now - it does not end
        /// the round, and it never converts into a round. The phase machine decides rounds.
        ///
        /// It does not touch losing streaks either. The loss bonus is paid for consecutive
        /// rounds lost, and in Valorant being killed inside a round you went on to lose does
        /// not reset it - only winning a round does, which <see cref="RecordRoundResult"/>
        /// handles when the round resolves.
        /// </summary>
        public void RegisterKill(PlayerSlot killer)
        {
            if (HasWinner) return;

            int index = (int)killer;
            _kills[index]++;

            // A kill is also a death for the other side. Booked here, alongside the kill,
            // because the host is the only machine allowed to resolve either.
            _deaths[(int)Other(killer)]++;

            OnKillScored?.Invoke(killer, _kills[index]);
        }

        /// <summary>
        /// Records a round win. Returns true when that round also won the player the match,
        /// which is the signal to stop playing. Also raises <see cref="OnRoundScored"/>.
        ///
        /// Streak bookkeeping is deliberately NOT done here - the economy has to read the
        /// streak as it was before this loss, so <see cref="RecordRoundResult"/> runs after
        /// the payouts, not inside this call.
        /// </summary>
        public bool AwardRound(PlayerSlot slot)
        {
            if (HasWinner) return false;

            int index = (int)slot;
            _roundsWon[index]++;

            OnRoundScored?.Invoke(slot, _roundsWon[index]);

            if (_roundsWon[index] >= roundsToWin)
            {
                HasWinner = true;
                Winner = slot;
                Debug.Log($"[ScoreManager] Player {(int)slot + 1} wins the match " +
                          $"{_roundsWon[0]} - {_roundsWon[1]}.");
                OnMatchWon?.Invoke(slot);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Called after the round's payouts, to grow or clear each player's loss streak. The
        /// loser's streak climbs, the winner's resets, and the economy reads the result next
        /// round.
        /// </summary>
        public void RecordRoundResult(PlayerSlot winner)
        {
            _lossStreak[(int)winner] = 0;
            _lossStreak[(int)Other(winner)]++;
        }

        public void ResetAll()
        {
            for (int i = 0; i < 2; i++)
            {
                _roundsWon[i] = 0;
                _kills[i] = 0;
                _deaths[i] = 0;
                _lossStreak[i] = 0;
            }

            HasWinner = false;
        }

        /// <summary>
        /// Client only: adopts the score the host sent. Slot One is this machine's player
        /// locally, so the caller has already flipped the host's numbers around.
        ///
        /// Deliberately raises no events: this is a paint job, not a match decision. The host
        /// owns the rounds-to-win conversion and the win, and it tells us about both through
        /// the phase machine - raising OnRoundScored here would make the client's copy of the
        /// match celebrate a round the host may have handled differently.
        /// </summary>
        public void SetRemoteScore(int localRounds, int remoteRounds, int localKills, int remoteKills,
                                   int localDeaths, int remoteDeaths)
        {
            _roundsWon[0] = Mathf.Max(0, localRounds);
            _roundsWon[1] = Mathf.Max(0, remoteRounds);
            _kills[0] = Mathf.Max(0, localKills);
            _kills[1] = Mathf.Max(0, remoteKills);
            _deaths[0] = Mathf.Max(0, localDeaths);
            _deaths[1] = Mathf.Max(0, remoteDeaths);
        }

        public static PlayerSlot Other(PlayerSlot slot) =>
            slot == PlayerSlot.One ? PlayerSlot.Two : PlayerSlot.One;
    }
}
