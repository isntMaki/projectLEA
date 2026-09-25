using ProjectLEA.Manuel.Managers;

namespace ProjectLEA.Manuel.Net
{
    /// <summary>
    /// The live cheat switches, flipped from a <see cref="MatchPreset"/>.
    ///
    /// Cheats are part of the preset rather than a separate menu, which means they ride the
    /// same wire as everything else: the host picks them, <see cref="LobbyNetwork"/> ships the
    /// preset as JSON inside <c>MsgStart</c>, and the client's copy of the same preset turns
    /// the same switches on. Both machines agree without a second message type.
    ///
    /// These are deliberately plain statics rather than manager state. Health, EconomyManager,
    /// ClassAbilityHost and PlayerController each read the one flag that concerns them, so a
    /// cheat never has to know about the match, the round, or the network to take effect, and
    /// a preset change is a single call.
    /// </summary>
    public static class MatchCheats
    {
        /// <summary>Every point of damage is clamped to the victim's remaining health.</summary>
        public const float OneHitKillDamage = 100000f;

        /// <summary>Applied to gravity while <see cref="lowGravity"/> is on.</summary>
        public const float LowGravityScale = 0.5f;

        /// <summary>Applied to movement speed while <see cref="superSpeed"/> is on.</summary>
        public const float SuperSpeedScale = 1.6f;

        /// <summary>Shown when the shop never runs dry.</summary>
        public const int InfiniteMoneyDisplay = 99999;

        public static bool oneHitKills;
        public static bool infiniteMoney;
        public static bool infiniteAbilities;
        public static bool lowGravity;
        public static bool superSpeed;

        /// <summary>
        /// Only the machine's own player is immune. On a client that is the client's body;
        /// on the host it is the host's. Both are slot one locally, so the same gate works
        /// everywhere without the network having to say who is who.
        /// </summary>
        public static bool localPlayerGodMode;

        /// <summary>Pushes every cheat from a preset. Null clears them all.</summary>
        public static void Apply(MatchPreset preset)
        {
            oneHitKills = preset != null && preset.oneHitKills;
            infiniteMoney = preset != null && preset.infiniteMoney;
            infiniteAbilities = preset != null && preset.infiniteAbilities;
            lowGravity = preset != null && preset.lowGravity;
            superSpeed = preset != null && preset.superSpeed;
            localPlayerGodMode = preset != null && preset.godMode;
        }

        /// <summary>Everything off, which is also the state before any preset has been picked.</summary>
        public static void Clear()
        {
            oneHitKills = false;
            infiniteMoney = false;
            infiniteAbilities = false;
            lowGravity = false;
            superSpeed = false;
            localPlayerGodMode = false;
        }

        /// <summary>True while the local player should be untouchable.</summary>
        public static bool IsGod(PlayerSlot slot) => localPlayerGodMode && slot == PlayerSlot.One;
    }
}
