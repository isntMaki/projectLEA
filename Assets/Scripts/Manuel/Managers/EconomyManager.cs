using System;
using ProjectLEA.Manuel.Net;
using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Owns match money, per player.
    ///
    /// Two balances rather than one, because it is a 1v1: each slot is paid separately, the
    /// round winner is paid more than the loser, and the loser gets a growing bonus for a
    /// losing streak so the match does not run away from them.
    ///
    /// <see cref="Money"/> is shorthand for the local player's balance, which is what the
    /// shop spends.
    /// </summary>
    public class EconomyManager : ManagerBase<EconomyManager>
    {
        [Header("Starting")]
        [Tooltip("Credits each player starts the match with.")]
        [Min(0)] [SerializeField] private int startingMoney = 800;

        [Header("Per kill")]
        [Min(0)] [SerializeField] private int killReward = 200;

        [Header("Per round")]
        [Tooltip("Paid to the player who won the round.")]
        [Min(0)] [SerializeField] private int roundWinReward = 3000;

        [Tooltip("Base paid to the player who lost the round, before any streak bonus.")]
        [Min(0)] [SerializeField] private int roundLossReward = 1900;

        [Header("Losing streak")]
        [Tooltip("Extra credits per consecutive loss.")]
        [Min(0)] [SerializeField] private int lossStreakBonus = 500;

        [Tooltip("Cap on the streak bonus. 1000 with the defaults reads as 1900, then 2400, " +
                 "then 2900 and no higher.")]
        [Min(0)] [SerializeField] private int maxLossStreakBonus = 1000;

        [Header("Carry limit")]
        [Tooltip("Nobody banks more than this. Valorant's cap is 9000.")]
        [Min(0)] [SerializeField] private int creditCap = 9000;

        /// <summary>Applies a game-mode preset. See <see cref="GameManager.ApplyPreset"/>.</summary>
        public void ApplyPreset(MatchPreset preset)
        {
            if (preset == null) return;
            startingMoney = preset.startingMoney;
            killReward = preset.killReward;
            roundWinReward = preset.roundWinReward;
            roundLossReward = preset.roundLossReward;
            lossStreakBonus = preset.lossStreakBonus;
            maxLossStreakBonus = preset.maxLossStreakBonus;
            creditCap = preset.creditCap;
        }

        private readonly int[] _money = new int[2];

        /// <summary>
        /// The local player's balance, which is what the shop spends. Reads as a large constant
        /// while the infinite-money cheat is on, so the counter and the affordability check stay
        /// in step: the balance never moves, and nothing is ever too expensive.
        /// </summary>
        public int Money => MatchCheats.infiniteMoney
            ? MatchCheats.InfiniteMoneyDisplay
            : GetMoney(PlayerSlot.One);

        /// <summary>Raised with the slot whose balance changed, and the new value.</summary>
        public event Action<PlayerSlot, int> OnMoneyChanged;

        protected override void OnManagerAwake()
        {
            ResetMoney();
        }

        public int GetMoney(PlayerSlot slot) => _money[(int)slot];

        public bool CanAfford(int cost) => MatchCheats.infiniteMoney || Money >= cost;

        /// <summary>Spends from the local player's balance if affordable.</summary>
        public bool TrySpend(int cost)
        {
            if (cost <= 0) return true;
            if (MatchCheats.infiniteMoney) return true;
            if (!CanAfford(cost)) return false;

            SetMoney(PlayerSlot.One, _money[(int)PlayerSlot.One] - cost);
            return true;
        }

        public void Add(int amount) => AddTo(PlayerSlot.One, amount);

        public void AddTo(PlayerSlot slot, int amount)
        {
            if (amount == 0) return;
            SetMoney(slot, _money[(int)slot] + amount);
        }

        /// <summary>Pays the kill bonus to whoever got the kill.</summary>
        public int AwardKill(PlayerSlot killer)
        {
            AddTo(killer, killReward);
            return killReward;
        }

        /// <summary>
        /// Pays the end-of-round reward. The winner gets the flat win amount; the loser gets
        /// the base loss amount plus a bonus that grows with their losing streak.
        /// </summary>
        public int AwardRound(PlayerSlot slot, bool won)
        {
            int amount = won ? roundWinReward : roundLossReward;

            if (!won)
            {
                int streak = ScoreManager.Exists ? ScoreManager.Instance.GetLossStreak(slot) : 0;
                int bonus = Mathf.Min(maxLossStreakBonus, streak * lossStreakBonus);
                amount += bonus;

                if (bonus > 0)
                {
                    Debug.Log($"[EconomyManager] Player {(int)slot + 1} loss streak {streak} " +
                              $"- bonus {bonus}.");
                }
            }

            AddTo(slot, amount);
            return amount;
        }

        public void SetMoney(PlayerSlot slot, int amount)
        {
            int index = (int)slot;

            // The floor is zero, the ceiling is the carry limit. A reward that would take a
            // player over the cap is simply cut off there, the way it is in the real game.
            int clamped = Mathf.Clamp(amount, 0, creditCap);
            if (_money[index] == clamped) return;

            _money[index] = clamped;
            OnMoneyChanged?.Invoke(slot, clamped);
        }

        public void ResetMoney()
        {
            for (int i = 0; i < 2; i++)
            {
                // The starting grant is itself capped, so a preset with a silly starting value
                // cannot put a player above the carry limit before the match begins.
                _money[i] = Mathf.Clamp(startingMoney, 0, creditCap);
                OnMoneyChanged?.Invoke((PlayerSlot)i, _money[i]);
            }
        }

        /// <summary>The most credits either player is allowed to hold this match.</summary>
        public int CreditCap => creditCap;
    }
}
