using UnityEngine;

namespace ProjectLEA.Manuel.Net
{
    /// <summary>
    /// A named game mode: every tunable that decides how a match plays, in one asset.
    ///
    /// The host picks one of these in the Create screen, its values are pushed onto the
    /// real managers when the match starts, and the same asset is sent to the client, so
    /// both players are playing by identical rules without either of them having to configure
    /// anything by hand.
    ///
    /// Defaults are the real September 2026 Valorant numbers, minus the objective: 800 to
    /// start, 200 per kill, 3000 for a round win, 1900/2400/2900 for consecutive losses, 9000
    /// credit cap, first to 13 rounds.
    /// </summary>
    [CreateAssetMenu(menuName = "ProjectLEA/Match Preset", fileName = "Standard")]
    public class MatchPreset : ScriptableObject
    {
        [Header("Round structure")]
        [Tooltip("Rounds a player needs to win the match.")]
        [Min(1)] public int roundsToWin = 13;

        [Header("Phase lengths, in seconds")]
        [Tooltip("How long the class select screen gives both players to lock in a class. " +
                 "Anyone still reading the roster when this runs out is assigned one at random.")]
        [Min(1f)] public float classSelectSeconds = 45f;

        [Min(1f)] public float buySeconds = 30f;
        [Min(1f)] public float roundSeconds = 100f;       // 1:40

        [Tooltip("The gap between a round resolving and the next buy phase, so the result " +
                 "can be read before the shop reopens.")]
        [Min(0f)] public float roundOverSeconds = 5f;

        [Header("Economy")]
        [Min(0)] public int startingMoney = 800;
        [Min(0)] public int killReward = 200;

        [Tooltip("Paid to the player who won the round.")]
        [Min(0)] public int roundWinReward = 3000;

        [Tooltip("Base paid to the player who lost the round, before any streak bonus. " +
                 "With the defaults this reads as 1900 for a first loss, 2400 for a second " +
                 "consecutive loss and 2900 from the third on.")]
        [Min(0)] public int roundLossReward = 1900;

        [Tooltip("Extra credits per consecutive loss.")]
        [Min(0)] public int lossStreakBonus = 500;

        [Tooltip("Cap on the streak bonus. 1000 is what turns the base 1900 into 2900 and " +
                 "stops it climbing any further.")]
        [Min(0)] public int maxLossStreakBonus = 1000;

        [Tooltip("Nobody can hold more credits than this. Valorant's carry cap is 9000.")]
        [Min(0)] public int creditCap = 9000;

        [Header("Cheats - sandbox fun, off by default")]
        [Tooltip("Every hit is lethal, to anything. For when you just want to see things explode.")]
        public bool oneHitKills;

        [Tooltip("The shop never runs out of credits.")]
        public bool infiniteMoney;

        [Tooltip("Abilities (dash, and anything else on a cooldown) fire as often as you press them.")]
        public bool infiniteAbilities;

        [Tooltip("Half gravity - higher jumps, slower falls, longer dashes.")]
        public bool lowGravity;

        [Tooltip("1.6x movement speed. Genuinely silly velocity.")]
        public bool superSpeed;

        [Tooltip("This machine's player takes no damage at all. The opponent still can.")]
        public bool godMode;

        /// <summary>True if any cheat is on, so the lobby can say so out loud.</summary>
        public bool CheatsEnabled =>
            oneHitKills || infiniteMoney || infiniteAbilities ||
            lowGravity || superSpeed || godMode;

        /// <summary>Comma-separated cheat names, or an empty string when the match is clean.</summary>
        public string CheatSummary()
        {
            var names = new System.Collections.Generic.List<string>(6);
            if (oneHitKills) names.Add("one-hit kills");
            if (infiniteMoney) names.Add("infinite money");
            if (infiniteAbilities) names.Add("infinite abilities");
            if (lowGravity) names.Add("low gravity");
            if (superSpeed) names.Add("super speed");
            if (godMode) names.Add("god mode");
            return string.Join(", ", names);
        }

        /// <summary>
        /// A runtime copy. The host edits a preset's numbers in the lobby; without this, those
        /// edits would write straight back into the asset on disk in the Editor, and the
        /// Standard preset would slowly drift away from what it shipped as.
        /// </summary>
        public MatchPreset Clone()
        {
            var clone = ScriptableObject.CreateInstance<MatchPreset>();
            clone.roundsToWin = roundsToWin;
            clone.classSelectSeconds = classSelectSeconds;
            clone.buySeconds = buySeconds;
            clone.roundSeconds = roundSeconds;
            clone.roundOverSeconds = roundOverSeconds;
            clone.startingMoney = startingMoney;
            clone.killReward = killReward;
            clone.roundWinReward = roundWinReward;
            clone.roundLossReward = roundLossReward;
            clone.lossStreakBonus = lossStreakBonus;
            clone.maxLossStreakBonus = maxLossStreakBonus;
            clone.creditCap = creditCap;
            clone.oneHitKills = oneHitKills;
            clone.infiniteMoney = infiniteMoney;
            clone.infiniteAbilities = infiniteAbilities;
            clone.lowGravity = lowGravity;
            clone.superSpeed = superSpeed;
            clone.godMode = godMode;
            clone.name = name;
            return clone;
        }

        /// <summary>One line for the preset list, so the player can see what they are choosing.</summary>
        public string Describe()
        {
            int roundMinutes = Mathf.FloorToInt(roundSeconds / 60f);
            int roundRest = Mathf.RoundToInt(roundSeconds % 60f);

            string text = $"{name}: first to {roundsToWin} rounds, " +
                          $"{roundMinutes}:{roundRest:00} per round, ${startingMoney} start";

            string cheats = CheatSummary();
            if (!string.IsNullOrEmpty(cheats)) text += $" | {cheats}";
            return text;
        }
    }
}
