using ProjectLEA.Manuel.Managers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectLEA.Manuel.Net
{
    /// <summary>
    /// The bridge between the network session and the match.
    ///
    /// Two machines each load the same match scene and run their own copy of every manager.
    /// Exactly one of them - the host - is allowed to make decisions. This component is what
    /// keeps the two copies from arguing:
    ///
    ///   Host side   - pushes the phase machine and the score across as they change, and
    ///                 hands the preset it picked to the client.
    ///   Client side - switches its own GameManager into RemoteAuthoritative mode, adopts
    ///                 every state the host sends, and reports its own local hits and death
    ///                 back to the host instead of resolving them.
    ///
    /// Slots are local: on each machine slot One is "the player on this machine" and slot Two
    /// is "the body of the other one". On the host that matches the authoritative model
    /// exactly. On a client the host's slot Two means "me", so score is flipped on arrival.
    ///
    /// Money on a client needs one care: the host does not know what the client spent during
    /// a buy phase, so a money sync would silently hand it back. Locally spent credits are
    /// tracked and re-applied on top of every authoritative value.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetMatchSync : MonoBehaviour
    {
        [Tooltip("Scene to return to if the other player drops out mid-match.")]
        [SerializeField] private string mainMenuScene = "MainMenu";

        private LobbyNetwork _net;

        // Client money bookkeeping, see the class summary.
        private int _clientLocalSpend;
        private int _clientBaselineMoney = -1;
        private bool _applyingNetworkMoney;

        // Set once both avatar components are in place; see Update.
        private bool _avatarsReady;

        [Tooltip("How often the host re-publishes the current phase. The phase is sent on every " +
                 "change anyway; this is the safety net for a client that was not listening yet " +
                 "when the change went out, so it still converges instead of sitting in the " +
                 "wrong phase forever.")]
        [SerializeField] private float phaseHeartbeatInterval = 1f;

        private float _phaseHeartbeat;

        private void Start()
        {
            _net = LobbyNetwork.Instance;
            if (_net == null)
            {
                Debug.LogWarning("[NetMatchSync] No network session; disabling.");
                enabled = false;
                return;
            }

            InstallAvatarComponents();

            if (_net.IsHost) StartHostSide();
            else StartClientSide();

            _net.OnPhase += HandlePhase;
            _net.OnScore += HandleScore;
            _net.OnRound += HandleRound;
            _net.OnTransform += HandleTransform;
            _net.OnDamage += HandleDamage;
            _net.OnRemoteDeath += HandleRemoteDeath;
            _net.OnZone += HandleZone;
            _net.OnClassLock += HandleClassLock;
            _net.OnDisconnected += HandleDisconnected;
        }

        private void Update()
        {
            // The slot-Two body registers itself from its own Start, which may run after ours.
            // Keep trying until both avatars are wired, rather than silently never relaying
            // transforms to a proxy that did not exist yet.
            if (!_avatarsReady) InstallAvatarComponents();

            // The host owns the phase machine and pushes every change. A client that was not
            // subscribed yet when the change went out - its scene loading a frame late, or the
            // message arriving mid-load - would otherwise miss it entirely and never see the
            // class select screen. Repeating the phase slowly closes that gap; the client treats
            // a repeat as a clock refresh, not a transition, so it is free.
            //
            // This has to count in UNSCALED time. The class select screen and the shop both push
            // MatchUiPause, which zeroes Time.timeScale for as long as they are up - and the
            // class select phase is exactly the phase where a late client needs the heartbeat
            // most. A scaled decrement would freeze at zero for the whole phase, so a client
            // that missed the one-shot phase change would sit in Idle and never see the roster.
            if (_net.IsHost)
            {
                _phaseHeartbeat -= Time.unscaledDeltaTime;
                if (_phaseHeartbeat <= 0f)
                {
                    _phaseHeartbeat = phaseHeartbeatInterval;
                    SendPhase();
                }
            }
        }

        private void OnDestroy()
        {
            if (_net == null) return;

            _net.OnPhase -= HandlePhase;
            _net.OnScore -= HandleScore;
            _net.OnRound -= HandleRound;
            _net.OnTransform -= HandleTransform;
            _net.OnDamage -= HandleDamage;
            _net.OnRemoteDeath -= HandleRemoteDeath;
            _net.OnZone -= HandleZone;
            _net.OnClassLock -= HandleClassLock;
            _net.OnDisconnected -= HandleDisconnected;

            if (EconomyManager.Exists) EconomyManager.Instance.OnMoneyChanged -= HandleLocalMoney;
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------
        /// <summary>
        /// The local player publishes its transform; the slot-Two body becomes the proxy that
        /// the other machine drives.
        /// </summary>
        private void InstallAvatarComponents()
        {
            if (!RoundManager.Exists) return;

            var local = RoundManager.Instance.GetPlayer(PlayerSlot.One);
            if (local != null && local.GetComponent<NetPlayer>() == null)
                local.gameObject.AddComponent<NetPlayer>();

            var remote = RoundManager.Instance.GetPlayer(PlayerSlot.Two);
            if (remote != null && remote.GetComponent<NetProxyPlayer>() == null)
                remote.gameObject.AddComponent<NetProxyPlayer>();

            _avatarsReady = local != null && remote != null
                            && local.GetComponent<NetPlayer>() != null
                            && remote.GetComponent<NetProxyPlayer>() != null;
        }

        private void StartHostSide()
        {
            if (GameManager.Exists)
            {
                // The host's own preset, picked in the Create screen.
                GameManager.Instance.ApplyPreset(_net.SelectedPreset);

                GameManager.Instance.OnStateChanged += state =>
                {
                    SendPhase();
                    SendScore();
                };
            }

            if (ScoreManager.Exists)
            {
                ScoreManager.Instance.OnKillScored += (_, _) => SendScore();
                ScoreManager.Instance.OnRoundScored += (_, _) => SendScore();
                ScoreManager.Instance.OnMatchWon += _ => SendScore();
            }

            // A client's purchases are its own business until it tells us otherwise, so only
            // the host's balance is worth publishing.
            if (EconomyManager.Exists)
                EconomyManager.Instance.OnMoneyChanged += (slot, _) => { if (slot == PlayerSlot.One) SendScore(); };

            if (GameManager.Exists)
            {
                GameManager.Instance.OnRoundResolved +=
                    (winner, reason) => _net.SendRound((int)winner, (int)reason);
                GameManager.Instance.BeginMatch();
            }
        }

        private void StartClientSide()
        {
            if (GameManager.Exists)
            {
                GameManager.Instance.RemoteAuthoritative = true;
                GameManager.Instance.ApplyPreset(_net.PendingPreset);

                // Mirror the resets BeginMatch does on the host, without starting a phase:
                // the host tells us which phase we are in.
                GameManager.Instance.ResetMatchState();
            }

            if (EconomyManager.Exists)
                EconomyManager.Instance.OnMoneyChanged += HandleLocalMoney;

            // This machine's own body stands on the far spawn point (see RoundManager), so the
            // host's body and ours are on opposite sides of the arena on both machines.
            if (RoundManager.Exists)
                RoundManager.Instance.LocalPlayerUsesRemoteSpawn = true;

            ReportLocalDeath();
        }

        // ------------------------------------------------------------------
        // Outbound (host)
        // ------------------------------------------------------------------
        private void SendPhase()
        {
            if (!GameManager.Exists) return;

            var game = GameManager.Instance;
            _net.SendPhase((int)game.State, game.PhaseDuration, game.TimeRemaining);
        }

        /// <summary>
        /// A client's own player just died. The client is not allowed to resolve that, so it
        /// reports it and lets the host pay the round out and move the phase on. The host's
        /// own death is resolved locally by its GameManager, so there is nothing to send.
        /// </summary>
        private void ReportLocalDeath()
        {
            if (!_net.IsClient) return;
            if (!RoundManager.Exists) return;

            var local = RoundManager.Instance.GetPlayer(PlayerSlot.One);
            if (local == null) return;

            var health = local.GetComponent<Health>();
            if (health == null) return;

            health.OnDied += _ => _net.SendDeath();
        }

        private void SendScore()
        {
            if (!ScoreManager.Exists) return;

            var score = ScoreManager.Instance;
            int moneyOne = EconomyManager.Exists ? EconomyManager.Instance.GetMoney(PlayerSlot.One) : 0;
            int moneyTwo = EconomyManager.Exists ? EconomyManager.Instance.GetMoney(PlayerSlot.Two) : 0;

            _net.SendScore(new MsgScore
            {
                roundsOne = score.GetRoundsWon(PlayerSlot.One),
                roundsTwo = score.GetRoundsWon(PlayerSlot.Two),
                killsOne = score.GetKills(PlayerSlot.One),
                killsTwo = score.GetKills(PlayerSlot.Two),
                deathsOne = score.GetDeaths(PlayerSlot.One),
                deathsTwo = score.GetDeaths(PlayerSlot.Two),
                moneyOne = moneyOne,
                moneyTwo = moneyTwo
            });
        }

        // ------------------------------------------------------------------
        // Inbound
        // ------------------------------------------------------------------
        private void HandlePhase(MsgPhase msg)
        {
            if (msg == null || !GameManager.Exists) return;

            GameManager.Instance.SetRemoteState((MatchState)msg.state, msg.duration, msg.remaining);
        }

        /// <summary>
        /// The other machine told us where its player is standing. Slot Two is that player's
        /// body on this machine, so the snapshot goes straight to its proxy - nothing here may
        /// simulate the opponent, only paint what their machine is authoritative for.
        /// </summary>
        private void HandleTransform(MsgTransform msg)
        {
            if (msg == null) return;
            if (!RoundManager.Exists) return;

            var remote = RoundManager.Instance.GetPlayer(PlayerSlot.Two);
            if (remote == null) return;

            var proxy = remote.GetComponent<NetProxyPlayer>();
            if (proxy == null) return;

            var position = new Vector3(msg.x, msg.y, msg.z);
            var rotation = Quaternion.Euler(msg.rx, msg.ry, msg.rz);

            proxy.ApplySnapshot(position, rotation);
        }

        private void HandleScore(MsgScore msg)
        {
            if (msg == null) return;
            if (_net == null) return;

            // Slot One is always "this machine" locally. On a client, the host's slot Two is
            // this machine, so the two slots trade places on arrival.
            bool flip = _net.IsClient;

            int localRounds = flip ? msg.roundsTwo : msg.roundsOne;
            int remoteRounds = flip ? msg.roundsOne : msg.roundsTwo;
            int localKills = flip ? msg.killsTwo : msg.killsOne;
            int remoteKills = flip ? msg.killsOne : msg.killsTwo;
            int localDeaths = flip ? msg.deathsTwo : msg.deathsOne;
            int remoteDeaths = flip ? msg.deathsOne : msg.deathsTwo;

            if (ScoreManager.Exists)
                ScoreManager.Instance.SetRemoteScore(localRounds, remoteRounds, localKills, remoteKills,
                                                     localDeaths, remoteDeaths);

            ApplyRemoteMoney(flip ? msg.moneyTwo : msg.moneyOne);
        }

        /// <summary>
        /// The host resolved a round. Adopted for the banner and the log only - the phase
        /// message that arrives with it is what moves our state machine.
        /// </summary>
        private void HandleRound(MsgRound msg)
        {
            if (msg == null) return;
            if (!GameManager.Exists) return;

            GameManager.Instance.SetRemoteRoundResult(msg.winner, msg.reason);
        }

        private void ApplyRemoteMoney(int authoritative)
        {
            if (!EconomyManager.Exists) return;

            // Keep what the client spent during the buy phase; the host has no idea it happened.
            int target = Mathf.Max(0, authoritative - _clientLocalSpend);

            _applyingNetworkMoney = true;
            try
            {
                EconomyManager.Instance.SetMoney(PlayerSlot.One, target);
                _clientLocalSpend = 0;
                _clientBaselineMoney = target;
            }
            finally
            {
                _applyingNetworkMoney = false;
            }
        }

        /// <summary>Tracks credits the local player spent that the host does not know about.</summary>
        private void HandleLocalMoney(PlayerSlot slot, int value)
        {
            if (slot != PlayerSlot.One) return;
            if (_applyingNetworkMoney) return;

            if (_clientBaselineMoney < 0)
            {
                _clientBaselineMoney = value;
                return;
            }

            if (value < _clientBaselineMoney) _clientLocalSpend += _clientBaselineMoney - value;
            _clientBaselineMoney = value;
        }

        private void HandleDamage(float amount, Vector3 point)
        {
            // The other machine hit our local player. Their Health lives here.
            if (!RoundManager.Exists) return;

            var local = RoundManager.Instance.GetPlayer(PlayerSlot.One);
            if (local == null) return;

            var health = local.GetComponent<Health>();
            if (health == null) return;

            var info = DamageInfo.Direct(amount, point, Vector3.forward, null, null);
            health.ApplyDamage(info);
        }

        private void HandleRemoteDeath()
        {
            // The client's player died on the client's machine. Only the host may resolve a
            // death - it pays the round out, moves the phase on, and then tells us about both.
            if (!_net.IsHost) return;
            if (!GameManager.Exists) return;

            GameManager.Instance.RegisterDeath(PlayerSlot.Two);
        }

        /// <summary>
        /// The other player deployed an ability zone. We spawn our own copy so the smoke and the
        /// wall are visible here too; our copy only affects our own local body, which is how a
        /// mirrored zone lands its damage exactly once. The caster is null on our side - we do
        /// not own the body that cast it, and the zone does not need it.
        /// </summary>
        private void HandleZone(MsgZone msg)
        {
            if (msg == null) return;

            var point = new Vector3(msg.x, msg.y, msg.z);

            ProjectLEA.Manuel.Abilities.ZoneAbility.SpawnZone(
                point,
                (ProjectLEA.Manuel.Abilities.ZoneKind)msg.kind,
                caster: null,
                radius: msg.radius,
                duration: msg.duration,
                tickDamage: msg.tickDamage,
                slow: msg.slow,
                pull: msg.pull,
                barrierSize: new Vector3(6f, 3f, 0.5f));
        }

        /// <summary>
        /// The other player told us what they are picking. Slot Two is the other player's body
        /// on this machine, whichever of us is hosting - the mapping is the same both ways, so
        /// this does not need to know the role.
        ///
        /// The pick is recorded for the select screen; it is never applied here. Their class
        /// runs on their machine, where their body and its Health actually live.
        /// </summary>
        private void HandleClassLock(int index, bool locked)
        {
            if (!GameManager.Exists) return;

            if (locked) GameManager.Instance.LockClass(PlayerSlot.Two, index);
            else GameManager.Instance.HoverClass(PlayerSlot.Two, index);
        }

        private void HandleDisconnected()
        {
            Debug.Log("[NetMatchSync] The other player left. Returning to the main menu.");

            // Drop the session before leaving, so the menu does not inherit a half-open socket.
            _net.Shutdown();

            if (!string.IsNullOrEmpty(mainMenuScene) && Application.CanStreamedLevelBeLoaded(mainMenuScene))
                SceneManager.LoadScene(mainMenuScene);
        }
    }
}
