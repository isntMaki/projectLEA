using System;
using ProjectLEA.Manuel.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>Where the match currently is.</summary>
    public enum MatchState
    {
        /// <summary>Nothing has started. Press P to begin.</summary>
        Idle,

        /// <summary>The shop is open and the buy timer is running. Both players can confirm to skip it.</summary>
        BuyPhase,

        /// <summary>The round is live. The clock is the only thing that ends it besides a kill.</summary>
        RoundActive,

        /// <summary>The round just resolved. A short gap so the result can be read before the
        /// next buy phase opens.</summary>
        RoundOver,

        /// <summary>Someone reached the win condition.</summary>
        MatchOver,

        /// <summary>
        /// The match has begun but neither player has locked a class yet. The class select
        /// screen owns the screen during this phase; the buy phase opens the moment both
        /// players have locked in, or when the timer runs out and the undecided are assigned
        /// a class at random - which is exactly how Valorant handles an idle picker.
        /// </summary>
        ClassSelect
    }

    /// <summary>How the round that just finished was decided, for the banner and the log.</summary>
    public enum RoundEndReason
    {
        None,

        /// <summary>Somebody was killed. In a 1v1, that ends the round outright.</summary>
        Elimination,

        /// <summary>The round timer ran out with nobody dead. The healthier player takes it.</summary>
        TimeExpired,

        /// <summary>
        /// The timer ran out with both players on exactly the same health. Nobody takes the
        /// round and nobody is paid - it is replayed instead. The winner field that travels
        /// with it is a placeholder the banner ignores.
        /// </summary>
        Draw
    }

    /// <summary>
    /// Drives the match. Owns the phase machine and the timers, and decides when rounds start
    /// and stop - but it does not own the score, the money or the players. It asks the other
    /// managers to do their job and reacts to their events.
    ///
    /// The loop is Valorant's without the objective: class select, buy phase, round, payout,
    /// repeat. First to <see cref="ScoreManager.RoundsToWin"/> rounds wins. A round ends when
    /// somebody dies, or when the clock runs out and the healthier player is awarded it.
    ///
    /// All phase timers use unscaled time, because the shop sets Time.timeScale to 0 while it
    /// is open and the buy countdown still has to run.
    /// </summary>
    public class GameManager : ManagerBase<GameManager>
    {
        [Header("Phase lengths, in seconds")]
        [Min(1f)] [SerializeField] private float classSelectSeconds = 45f;
        [Min(1f)] [SerializeField] private float buySeconds = 30f;
        [Min(1f)] [SerializeField] private float roundSeconds = 100f;      // 1:40
        [Min(0f)] [SerializeField] private float roundOverSeconds = 5f;

        [Header("Rules")]
        [Tooltip("Player two is a dummy for now, so it counts as always ready to start.")]
        [SerializeField] private bool playerTwoAlwaysReady = true;

        /// <summary>
        /// Set on a client. The host owns the phase machine and pushes it over the network,
        /// so a client never advances phases or resolves deaths of its own - it would only
        /// desync from the host's authoritative copy of the same match.
        /// </summary>
        public bool RemoteAuthoritative { get; set; }

        public MatchState State { get; private set; } = MatchState.Idle;

        /// <summary>Seconds left in the current phase.</summary>
        public float TimeRemaining => Mathf.Max(0f, _phaseEndsAt - Time.unscaledTime);

        /// <summary>Length of the current phase, for a progress bar.</summary>
        public float PhaseDuration { get; private set; }

        /// <summary>True while the shop should be open.</summary>
        public bool IsBuyPhase => State == MatchState.BuyPhase;

        /// <summary>
        /// True when the player may change class. A class is chosen on the select screen at
        /// match start and is meant to be locked in for the whole match after that - the shop
        /// shows the pick, it does not offer a different one.
        /// </summary>
        public bool ClassChangeAllowed => State == MatchState.ClassSelect;

        /// <summary>Index of the class a player has pointed at, or -1 when they have not.</summary>
        public int GetClassPick(PlayerSlot slot) => _classPick[(int)slot];

        /// <summary>Whether a player has locked their class in for this match.</summary>
        public bool IsClassLocked(PlayerSlot slot) => _classLocked[(int)slot];

        /// <summary>Who took the round that just ended, if any.</summary>
        public PlayerSlot LastRoundWinner { get; private set; }

        /// <summary>How the round that just ended was decided.</summary>
        public RoundEndReason LastRoundReason { get; private set; } = RoundEndReason.None;

        public event Action<MatchState> OnStateChanged;
        public event Action OnMatchStarted;
        public event Action<PlayerSlot> OnMatchEnded;

        /// <summary>Raised when the phase timer expires, after the state has moved on.</summary>
        public event Action<MatchState> OnPhaseEnded;

        /// <summary>
        /// Raised with the winner and the reason whenever a round resolves, so the HUD can say
        /// what happened and the host can push both to the other machine.
        /// </summary>
        public event Action<PlayerSlot, RoundEndReason> OnRoundResolved;

        private float _phaseEndsAt;
        private readonly bool[] _ready = new bool[2];
        private bool _subscribed;

        /// <summary>
        /// Class selection. Slot One is always this machine's player, so a lock we receive from
        /// the other machine lands in slot Two on both ends (see NetMatchSync).
        ///
        /// An index of -1 means "still browsing". A player may change their mind freely until
        /// they lock; the class is only applied to the body at lock, so browsing does not churn
        /// the ability host.
        /// </summary>
        private readonly int[] _classPick = { -1, -1 };
        private readonly bool[] _classLocked = new bool[2];

        /// <summary>
        /// Listens for either player dying, so a real death and the debug keys both run
        /// through RegisterDeath and a kill can only ever count once.
        /// </summary>
        private readonly bool[] _deathSubscribed = new bool[2];

        private void Start()
        {
            Subscribe();
            SubscribePlayerDeaths();
        }

        private void SubscribePlayerDeaths()
        {
            if (!RoundManager.Exists) return;

            for (int i = 0; i < 2; i++)
            {
                if (_deathSubscribed[i]) continue;

                var slot = (PlayerSlot)i;
                var player = RoundManager.Instance.GetPlayer(slot);
                if (player == null) continue;

                var health = player.GetComponent<Health>();
                if (health == null) continue;

                health.OnDied += _ => RegisterDeath(slot);
                _deathSubscribed[i] = true;
            }
        }

        // Death subscription is retried every frame until both players exist, and the round
        // can resolve through a debug key before either is registered - so the round number
        // has to be readable here for the log even when RoundManager has not started one.
        private int CurrentRoundNumber => RoundManager.Exists ? RoundManager.Instance.RoundNumber : 0;

        // Must override, not shadow: a new private OnDestroy would hide the base and the
        // singleton slot would never be cleared.
        protected override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (ScoreManager.Exists) ScoreManager.Instance.OnMatchWon += HandleMatchWon;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (ScoreManager.Exists) ScoreManager.Instance.OnMatchWon -= HandleMatchWon;
            _subscribed = false;
        }

        private void Update()
        {
            // Cheap and idempotent: picks up the second player whenever it registers.
            SubscribePlayerDeaths();

            // A client renders the host's phase machine; it does not run one of its own.
            // Its own timer would fire AdvancePhase and fight every state the host sends.
            if (RemoteAuthoritative) return;

            if (State == MatchState.Idle || State == MatchState.MatchOver) return;

            if (Time.unscaledTime < _phaseEndsAt) return;

            AdvancePhase();
        }

        /// <summary>
        /// Pushes a game-mode preset onto the real manager values. The host calls this with
        /// the preset it picked in the Create screen; the client calls this with the copy the
        /// host sent it, so both machines play by identical numbers.
        /// </summary>
        public void ApplyPreset(MatchPreset preset)
        {
            if (preset == null) return;

            buySeconds = preset.buySeconds;
            roundSeconds = preset.roundSeconds;
            roundOverSeconds = preset.roundOverSeconds;
            classSelectSeconds = preset.classSelectSeconds;

            if (ScoreManager.Exists) ScoreManager.Instance.ApplyPreset(preset);
            if (EconomyManager.Exists) EconomyManager.Instance.ApplyPreset(preset);

            // Cheats ride the preset, so they are switched here rather than in a separate
            // call. A null preset clears them, which is what leaving a lobby should do.
            MatchCheats.Apply(preset);

            Debug.Log($"[GameManager] Applied preset '{preset.name}'." +
                      (preset.CheatsEnabled ? $" Cheats: {preset.CheatSummary()}." : string.Empty));
        }

        /// <summary>
        /// Client only: adopts a state the host sent. Rebuilds the timer so the local
        /// countdown matches the host's, and raises the same event a local change would.
        /// </summary>
        public void SetRemoteState(MatchState state, float duration, float remaining)
        {
            MatchState previous = State;

            // The host re-publishes the current phase as a heartbeat, so a message that arrives
            // before this machine was listening - or a client that loaded its scene a frame late
            // - still converges instead of sitting in the wrong phase forever. Repeated state,
            // though, is only a clock refresh: re-running the transition below would, among
            // other things, wipe buy-phase purchases on every heartbeat.
            if (state == previous)
            {
                PhaseDuration = Mathf.Max(0f, duration);
                _phaseEndsAt = Time.unscaledTime + Mathf.Max(0f, remaining);
                return;
            }

            State = state;
            PhaseDuration = Mathf.Max(0f, duration);
            _phaseEndsAt = Time.unscaledTime + Mathf.Max(0f, remaining);
            _ready[0] = false;
            _ready[1] = false;

            // A fresh phase means the picks on screen are stale. Only the ClassSelect phase
            // itself keeps them: arriving there is what opens the select screen, and clearing
            // then would throw away a pick the host has already told us about.
            if (state != MatchState.ClassSelect) ResetClassLocks();

            // A round resolving on the host respawns everyone there. The client has to respawn
            // its own player at the same moment, because the host's respawn only moves the
            // bodies that exist on the host's machine.
            bool wasFighting = previous == MatchState.RoundActive;
            bool nowOver = state == MatchState.RoundOver;
            if (wasFighting && nowOver)
            {
                // Same moment the host reads it: the round is decided, the bodies are still
                // down. The next buy phase keeps this player's weapons only if they survived.
                _localPlayerDiedThisRound = IsLocalPlayerDead();
                RoundManager.Instance.RespawnAll();
            }

            ApplyLoadoutReset(previous, state);

            OnStateChanged?.Invoke(state);
        }

        /// <summary>Client only: adopts the round result the host sent, for the banner.</summary>
        public void SetRemoteRoundResult(int winner, int reason)
        {
            if (winner < 0 || winner > 1) return;

            LastRoundWinner = (PlayerSlot)winner;
            LastRoundReason = (RoundEndReason)reason;
        }

        // ------------------------------------------------------------------
        // Match flow
        // ------------------------------------------------------------------
        /// <summary>
        /// Starts the match, or starts a fresh one if the previous match already ended.
        /// Only the host calls this of its own accord - a client that did would race the
        /// host's own start and the two machines would disagree about the score.
        /// </summary>
        public void BeginMatch()
        {
            if (RemoteAuthoritative) return;
            if (State != MatchState.Idle && State != MatchState.MatchOver) return;

            if (ScoreManager.Exists) ScoreManager.Instance.ResetAll();
            if (EconomyManager.Exists) EconomyManager.Instance.ResetMoney();
            if (WeaponManager.Exists) WeaponManager.Instance.ResetPurchases();
            if (ClassManager.Exists) ClassManager.Instance.ClearSelection();
            if (RoundManager.Exists) RoundManager.Instance.ResetRounds();

            LastRoundReason = RoundEndReason.None;
            LastRoundWinner = PlayerSlot.One;
            _localPlayerDiedThisRound = true;

            Debug.Log("[GameManager] Match starting.");
            OnMatchStarted?.Invoke();

            EnterClassSelect();
        }

        /// <summary>
        /// Clears score, money, purchases and rounds back to their starting state, without
        /// starting a phase. The host runs this as part of BeginMatch; a client runs it alone,
        /// because the host will tell it which phase to display.
        /// </summary>
        public void ResetMatchState()
        {
            if (ScoreManager.Exists) ScoreManager.Instance.ResetAll();
            if (EconomyManager.Exists) EconomyManager.Instance.ResetMoney();
            if (WeaponManager.Exists) WeaponManager.Instance.ResetPurchases();
            if (ClassManager.Exists) ClassManager.Instance.ClearSelection();
            if (RoundManager.Exists) RoundManager.Instance.ResetRounds();
        }

        /// <summary>
        /// A player died. In a 1v1 that ends the round outright - there is nobody left on the
        /// dead player's side. The other slot takes the round.
        /// </summary>
        public void RegisterDeath(PlayerSlot victim)
        {
            // Only the host resolves deaths. A client that resolved its own would award a
            // kill the host knows nothing about, and the two machines would drift apart.
            // The client reports its deaths to the host, which reports the outcome back as
            // a phase change that SetRemoteState applies.
            if (RemoteAuthoritative) return;

            if (State != MatchState.RoundActive) return;

            var killer = ScoreManager.Other(victim);

            if (EconomyManager.Exists) EconomyManager.Instance.AwardKill(killer);
            if (ScoreManager.Exists) ScoreManager.Instance.RegisterKill(killer);

            EndRound(killer, RoundEndReason.Elimination);
        }

        /// <summary>A player confirmed they are ready. Both ready ends the buy phase early.</summary>
        public void ConfirmReady(PlayerSlot slot)
        {
            if (!IsBuyPhase) return;

            _ready[(int)slot] = true;

            bool bothReady = _ready[0] && (_ready[1] || playerTwoAlwaysReady);
            if (!bothReady) return;

            Debug.Log("[GameManager] Both players ready - starting the round early.");
            StartRound();
        }

        private void EnterBuyPhase()
        {
            _ready[0] = false;
            _ready[1] = false;

            // The loadout reset happens in SetPhase, where the previous state is known - a
            // player who survived the last round keeps what they bought.
            SetPhase(MatchState.BuyPhase, buySeconds);
        }

        /// <summary>
        /// Opens the class select phase. The roster is cleared first, so nobody carries a
        /// class in from a previous match.
        /// </summary>
        private void EnterClassSelect()
        {
            ResetClassLocks();

            if (ClassManager.Exists) ClassManager.Instance.ClearSelection();

            SetPhase(MatchState.ClassSelect, classSelectSeconds);

            // Offline, the other slot is the training dummy and picks instantly - there is
            // nobody on the other end to keep the screen waiting. Online, the client's lock
            // arrives as a message and lands in slot Two.
            if (!LobbyNetwork.IsActive) LockClass(PlayerSlot.Two, RandomClassIndex());
        }

        /// <summary>
        /// A player locked in their class. Records the pick, applies it to the body when the
        /// pick is ours, and - on the host only - advances to the buy phase once both sides
        /// are locked. A client never advances: it adopts the buy phase when the host sends it.
        /// </summary>
        public void LockClass(PlayerSlot slot, int classIndex)
        {
            if (State != MatchState.ClassSelect) return;

            int i = (int)slot;

            _classPick[i] = classIndex;
            _classLocked[i] = true;

            // Only this machine's own player gets a class applied. The other player's body is
            // a network proxy whose real Health and abilities live on its owner's machine.
            if (slot == PlayerSlot.One && ClassManager.Exists && ClassManager.Instance.IsValidIndex(classIndex))
                ClassManager.Instance.SelectClass(classIndex);

            Debug.Log($"[GameManager] Player {(int)slot + 1} locked class {classIndex}.");

            if (!RemoteAuthoritative && _classLocked[0] && _classLocked[1])
            {
                Debug.Log("[GameManager] Both classes locked - opening the buy phase.");
                EnterBuyPhase();
            }
        }

        /// <summary>Points at a class without committing to it, so the screen can preview.</summary>
        public void HoverClass(PlayerSlot slot, int classIndex)
        {
            if (State != MatchState.ClassSelect) return;
            if (_classLocked[(int)slot]) return;

            _classPick[(int)slot] = classIndex;
        }

        /// <summary>
        /// Assigned a class to everyone who never locked, which is what happens when the timer
        /// runs out on somebody reading the roster. Only meaningful on the host: a client's
        /// timer is decorative, and its phase arrives in a message.
        /// </summary>
        private void AutoLockRemaining()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_classLocked[i]) continue;

                var slot = (PlayerSlot)i;
                int index = _classPick[i] >= 0 ? _classPick[i] : RandomClassIndex();

                Debug.Log($"[GameManager] Player {i + 1} did not lock in time - assigning a class.");
                LockClass(slot, index);
            }
        }

        /// <summary>Any class, uniformly. Used for the dummy and for anyone idle at the timer.</summary>
        private static int RandomClassIndex()
        {
            if (!ClassManager.Exists) return 0;

            int count = ClassManager.Instance.Classes.Count;
            return count > 0 ? UnityEngine.Random.Range(0, count) : 0;
        }

        private void ResetClassLocks()
        {
            _classPick[0] = -1;
            _classPick[1] = -1;
            _classLocked[0] = false;
            _classLocked[1] = false;
        }

        private void StartRound()
        {
            SetPhase(MatchState.RoundActive, roundSeconds);

            if (RoundManager.Exists) RoundManager.Instance.StartRound();
        }

        private void AdvancePhase()
        {
            var ended = State;
            OnPhaseEnded?.Invoke(ended);

            switch (ended)
            {
                case MatchState.ClassSelect:
                    // Time ran out on the roster. Anyone still browsing gets a class assigned,
                    // then the shop opens for real. Assigning the last one may already have
                    // opened it - both players locking at once does - so only move if it did not.
                    AutoLockRemaining();
                    if (State == MatchState.ClassSelect) EnterBuyPhase();
                    break;

                case MatchState.BuyPhase:
                    StartRound();
                    break;

                case MatchState.RoundActive:
                    // Nobody died before the clock ran out. Without an objective to attack or
                    // defend, the healthier player takes the round, and an exact tie replays it
                    // with nobody paid - camping each other to a standstill should not hand
                    // either player a free round.
                    if (!ResolveTimeExpiry(out PlayerSlot timeWinner))
                    {
                        Debug.Log($"[GameManager] Round {CurrentRoundNumber} expired in a dead tie - " +
                                  "no winner, the round is replayed.");

                        // Reported so the other machine's banner agrees, but EndRound is not
                        // called: no payout, no streak change, no round on the board.
                        LastRoundReason = RoundEndReason.Draw;
                        OnRoundResolved?.Invoke(PlayerSlot.One, RoundEndReason.Draw);

                        SetPhase(MatchState.RoundOver, roundOverSeconds);
                        break;
                    }

                    Debug.Log($"[GameManager] Round {CurrentRoundNumber} timer expired - " +
                              $"player {(int)timeWinner + 1} takes it on health.");
                    EndRound(timeWinner, RoundEndReason.TimeExpired);
                    break;

                case MatchState.RoundOver:
                    EnterBuyPhase();
                    break;
            }
        }

        /// <summary>
        /// Resolves the round: pays both players out, books the streaks, hands the round to the
        /// winner, and either ends the match or opens the gap before the next buy phase.
        ///
        /// Safe to call more than once for the same round, because a resolved round is in
        /// RoundOver or MatchOver and both bail out at the top.
        /// </summary>
        public void EndRound(PlayerSlot winner, RoundEndReason reason)
        {
            if (RemoteAuthoritative) return;
            if (State == MatchState.MatchOver || State == MatchState.RoundOver) return;
            if (ScoreManager.Exists && ScoreManager.Instance.HasWinner) return;

            LastRoundWinner = winner;
            LastRoundReason = reason;
            OnRoundResolved?.Invoke(winner, reason);

            Debug.Log($"[GameManager] Round {CurrentRoundNumber} over. " +
                      $"Player {(int)winner + 1} wins by {reason}.");

            // Payouts first, streak bookkeeping after: the losing streak bonus is read from the
            // streak as it was BEFORE this loss landed, which is what makes the tiers come out
            // at 1900, 2400 and 2900 rather than one step early.
            if (EconomyManager.Exists)
            {
                EconomyManager.Instance.AwardRound(winner, won: true);
                EconomyManager.Instance.AwardRound(ScoreManager.Other(winner), won: false);
            }

            if (ScoreManager.Exists) ScoreManager.Instance.RecordRoundResult(winner);

            // Read who is down before the respawn stands everyone back up and heals them,
            // because the survivor keeps their weapons into the next round and the dead do not.
            _localPlayerDiedThisRound = IsLocalPlayerDead();

            if (RoundManager.Exists)
            {
                RoundManager.Instance.EndRound(winner);
                RoundManager.Instance.RespawnAll();
            }

            // AwardRound is what raises OnRoundScored and, if the match is over, OnMatchWon - which
            // HandleMatchWon turns into the MatchOver phase. If that happened, the state has
            // already moved and there is no buy phase to open.
            bool wonMatch = ScoreManager.Exists && ScoreManager.Instance.AwardRound(winner);
            if (wonMatch) return;

            SetPhase(MatchState.RoundOver, roundOverSeconds);
        }

        // ------------------------------------------------------------------
        // Match end
        // ------------------------------------------------------------------
        /// <summary>
        /// Breaks a round that ran out of time. False when both players are on exactly the same
        /// health, which is the only case the round is worth replaying rather than awarding.
        /// </summary>
        private bool ResolveTimeExpiry(out PlayerSlot winner)
        {
            winner = PlayerSlot.One;

            if (!RoundManager.Exists) return true;

            float one = PlayerHealth(PlayerSlot.One);
            float two = PlayerHealth(PlayerSlot.Two);

            if (Mathf.Approximately(one, two)) return false;

            winner = one > two ? PlayerSlot.One : PlayerSlot.Two;
            return true;
        }

        private static float PlayerHealth(PlayerSlot slot)
        {
            var player = RoundManager.Instance.GetPlayer(slot);
            if (player == null) return 0f;

            var health = player.GetComponent<Health>();
            return health == null ? 0f : health.Current;
        }

        private void HandleMatchWon(PlayerSlot winner)
        {
            SetPhase(MatchState.MatchOver, 0f);
            OnMatchEnded?.Invoke(winner);
        }

        private void SetPhase(MatchState next, float seconds)
        {
            ApplyLoadoutReset(State, next);

            State = next;
            PhaseDuration = Mathf.Max(0f, seconds);
            _phaseEndsAt = Time.unscaledTime + PhaseDuration;

            OnStateChanged?.Invoke(next);
        }

        // ------------------------------------------------------------------
        // Loadout persistence
        // ------------------------------------------------------------------
        /// <summary>
        /// Whether the player on this machine died in the round that just ended. Valorant lets
        /// a survivor keep their weapons into the next round; only a player who went down has
        /// to buy again. Freshly true, so the first buy phase of a match resets everything.
        /// </summary>
        private bool _localPlayerDiedThisRound = true;

        /// <summary>
        /// Empties the loadout when the shop opens, but only for the dead. Read while the
        /// bodies are still down - the moment a round resolves, before anyone is stood back up
        /// and healed to full, which would erase the answer.
        /// </summary>
        private static bool IsLocalPlayerDead()
        {
            if (!RoundManager.Exists) return false;

            var player = RoundManager.Instance.GetPlayer(PlayerSlot.One);
            if (player == null) return false;

            var health = player.GetComponent<Health>();
            return health == null || health.IsDead;
        }

        /// <summary>
        /// Called on every phase change on both machines, because a client adopts its phases
        /// through <see cref="SetRemoteState"/> rather than through <see cref="SetPhase"/>.
        /// </summary>
        private void ApplyLoadoutReset(MatchState previous, MatchState next)
        {
            if (next != MatchState.BuyPhase) return;
            if (!WeaponManager.Exists) return;

            // Only a real arrival at the shop counts. Anything else reaching BuyPhase is a
            // match that has not had a round yet, and resets unconditionally.
            bool afterARound = previous == MatchState.RoundOver;
            if (!afterARound)
            {
                WeaponManager.Instance.ResetPurchases();
                return;
            }

            if (_localPlayerDiedThisRound) WeaponManager.Instance.ResetPurchases();
        }
    }
}
