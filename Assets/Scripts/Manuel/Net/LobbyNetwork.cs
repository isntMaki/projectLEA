using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ProjectLEA.Manuel.Net
{
    /// <summary>
    /// The whole multiplayer layer, in one component and with no external packages.
    ///
    /// Two machines on the same router (so: the same internet connection, as the brief puts
    /// it) find each other like this:
    ///
    ///   1. The host starts a TCP listener on <see cref="GamePort"/> and shouts a UDP beacon
    ///      at the broadcast address every second.
    ///   2. The client listens on <see cref="BeaconPort"/>, collects those beacons, and
    ///      shows each one as a joinable lobby.
    ///   3. The client connects over TCP. From then on both machines have a single reliable
    ///      stream, and the lobby is invisible to anyone else.
    ///
    /// Everything is deliberately host-authoritative: the host owns the phase machine and the
    /// score, and pushes them at the client. The client owns nothing but its own avatar and
    /// its own hits, which it reports as damage and death messages back to the host.
    ///
    /// Threading: all socket work happens on background tasks. Inbound messages are queued
    /// onto a concurrent queue and pumped on the main thread in Update, because Unity API
    /// calls (scene loads, component reads) are only legal there.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyNetwork : MonoBehaviour
    {
        public static LobbyNetwork Instance { get; private set; }

        /// <summary>True when a network session is running - used by the match to decide
        /// whether to wire up its network components.</summary>
        public static bool IsActive => Instance != null && Instance.Role != NetRole.None;

        public const int BeaconPort = 7778;
        public const int GamePort = 7777;

        private const float LobbyTimeoutSeconds = 6f;
        private const int MaxMessageBytes = 64 * 1024;
        private const int ConnectTimeoutMs = 5000;

        public enum NetRole { None, Host, Client }

        private NetRole _role = NetRole.None;

        /// <summary>What this machine is doing. Set by StartHost / Join, cleared by Shutdown.</summary>
        public NetRole Role => _role;

        public bool IsHost => _role == NetRole.Host;
        public bool IsClient => _role == NetRole.Client;
        public bool IsConnected => _client != null && _client.Connected;

        public string LocalPlayerName { get; set; }
        public string RemotePlayerName { get; private set; }

        /// <summary>The preset the host picked in the Create screen.</summary>
        public MatchPreset SelectedPreset { get; private set; }

        /// <summary>
        /// The preset the host sent us when it started the match. Only meaningful on a client;
        /// applied to the local managers when the match scene comes up.
        /// </summary>
        public MatchPreset PendingPreset { get; private set; }

        /// <summary>One lobby the client has heard a beacon from.</summary>
        public struct DiscoveredLobby
        {
            public string Name;
            public string Address;
            public int Port;
            public int LastSeenMs;
        }

        // ------------------------------------------------------------------
        // Events - all raised on the main thread, from PumpInbound.
        // ------------------------------------------------------------------
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnRemoteNamed;
        public event Action<string[]> OnLobbyRoster;
        public event Action<MatchPreset, string> OnStartMatch;
        public event Action<MsgTransform> OnTransform;
        public event Action<MsgPhase> OnPhase;
        public event Action<MsgScore> OnScore;
        public event Action<MsgRound> OnRound;
        public event Action<float, Vector3> OnDamage;
        public event Action OnRemoteDeath;
        public event Action<MsgZone> OnZone;
        public event Action<int, bool> OnClassLock;
        public event Action<string, string> OnChat;

        private readonly ConcurrentQueue<string> _inbound = new ConcurrentQueue<string>();

        private readonly Dictionary<string, DiscoveredLobby> _lobbies =
            new Dictionary<string, DiscoveredLobby>();

        private UdpClient _beaconUdp;
        private System.Threading.CancellationTokenSource _beaconCancel;

        private TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;

        private readonly string _processId = Guid.NewGuid().ToString();
        private string _localIp = "127.0.0.1";

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            // This has to be a root object: DontDestroyOnLoad on a child of the MainMenu
            // object would carry the menu, the settings panel and the lobby UI into the
            // match scene along with the socket.
            if (transform.parent != null)
            {
                Debug.LogWarning("[LobbyNetwork] Was created as a child object; the session should own its own root.");
            }

            DontDestroyOnLoad(gameObject);

            LocalPlayerName = string.IsNullOrEmpty(Environment.MachineName)
                ? "Player"
                : Environment.MachineName;

            _localIp = GetLocalIp();
        }

        private void Update()
        {
            PumpInbound();
            PruneLobbies();
        }

        private void OnApplicationQuit() => Shutdown();

        private void OnDestroy() => Shutdown();

        // ------------------------------------------------------------------
        // Session control
        // ------------------------------------------------------------------
        /// <summary>Becomes the host: advertises the lobby and waits for one client.</summary>
        public void StartHost(string displayName, MatchPreset preset)
        {
            Shutdown();

            _role = NetRole.Host;
            LocalPlayerName = displayName;
            SelectedPreset = preset;

            StartBroadcasting();
            StartListening();
        }

        /// <summary>Connects to a host at the given address. Called from the lobby list.</summary>
        public void Join(string address, int port, string displayName)
        {
            Shutdown();

            _role = NetRole.Client;
            LocalPlayerName = displayName;

            StartDiscovery();
            ConnectTo(address, port);
        }

        /// <summary>Host only: loads the match and tells the client to do the same.</summary>
        public void StartMatch(string sceneName)
        {
            if (!IsHost || SelectedPreset == null) return;

            // The client applies the same preset, so both machines play by the same numbers
            // without the client having been shown the host's Inspector.
            Send(new MsgStart
            {
                presetJson = JsonUtility.ToJson(SelectedPreset),
                scene = sceneName
            });
        }

        public void Shutdown()
        {
            _role = NetRole.None;
            RemotePlayerName = null;
            PendingPreset = null;

            try { _beaconCancel?.Cancel(); } catch { }
            _beaconCancel = null;

            CloseBeacon();
            CloseConnection();

            lock (_lobbies) _lobbies.Clear();
            _inbound.Clear();

            // Without this the last match's cheats would still be active in the next one,
            // because nothing else clears them - a preset only ever sets them.
            MatchCheats.Clear();
        }

        private void CloseBeacon()
        {
            try { _beaconUdp?.Close(); } catch { }
            _beaconUdp = null;
        }

        private void CloseConnection()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }

            _stream = null;
            _client = null;
            _listener = null;
        }

        // ------------------------------------------------------------------
        // Discovery
        // ------------------------------------------------------------------
        /// <summary>Host: shouts a beacon at the broadcast address once a second.</summary>
        private void StartBroadcasting()
        {
            _beaconCancel = new System.Threading.CancellationTokenSource();
            _beaconUdp = new UdpClient { EnableBroadcast = true, ExclusiveAddressUse = false };

            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new MsgBeacon
            {
                id = _processId,
                name = LocalPlayerName,
                address = _localIp,
                port = GamePort
            }));

            var endpoint = new IPEndPoint(IPAddress.Broadcast, BeaconPort);
            var token = _beaconCancel.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    // The (byte[], IPEndPoint) overload does not exist in the class library
                    // Unity ships: only (byte[], int) and (byte[], int, IPEndPoint).
                    try { await _beaconUdp.SendAsync(bytes, bytes.Length, endpoint); }
                    catch { /* shut down, or the socket went away - either way, stop */ }

                    try { await Task.Delay(1000, token); }
                    catch { return; }
                }
            }, token);
        }

        /// <summary>Client: listens for beacons and files each unique one as a lobby.</summary>
        private void StartDiscovery()
        {
            _beaconCancel = new System.Threading.CancellationTokenSource();
            _beaconUdp = new UdpClient { ExclusiveAddressUse = false };
            _beaconUdp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _beaconUdp.Client.Bind(new IPEndPoint(IPAddress.Any, BeaconPort));

            var token = _beaconCancel.Token;

            Task.Run(() =>
            {
                var from = new IPEndPoint(IPAddress.Any, 0);

                while (!token.IsCancellationRequested)
                {
                    byte[] data;
                    try { data = _beaconUdp.Receive(ref from); }
                    catch (ObjectDisposedException) { return; }
                    catch (SocketException) { return; }

                    MsgBeacon beacon;
                    try { beacon = JsonUtility.FromJson<MsgBeacon>(Encoding.UTF8.GetString(data)); }
                    catch { continue; }

                    // Our own beacon, if the machine is somehow hearing it.
                    if (beacon == null || beacon.id == _processId) continue;

                    string key = beacon.address + ":" + beacon.port;

                    lock (_lobbies)
                    {
                        _lobbies[key] = new DiscoveredLobby
                        {
                            Name = beacon.name,
                            Address = beacon.address,
                            Port = beacon.port,
                            LastSeenMs = Environment.TickCount
                        };
                    }
                }
            }, token);
        }

        /// <summary>Lobbies still being heard from, newest first. Main thread only.</summary>
        public List<DiscoveredLobby> GetLobbies()
        {
            var result = new List<DiscoveredLobby>();

            lock (_lobbies)
            {
                foreach (var pair in _lobbies)
                {
                    if (Environment.TickCount - pair.Value.LastSeenMs < LobbyTimeoutSeconds * 1000)
                        result.Add(pair.Value);
                }
            }

            result.Sort((a, b) => b.LastSeenMs.CompareTo(a.LastSeenMs));
            return result;
        }

        private void PruneLobbies()
        {
            // Cheap: only sweep when the queue has grown past a reasonable size.
            if (_lobbies.Count <= 8) return;

            lock (_lobbies)
            {
                var stale = new List<string>();
                foreach (var pair in _lobbies)
                {
                    if (Environment.TickCount - pair.Value.LastSeenMs >= LobbyTimeoutSeconds * 1000)
                        stale.Add(pair.Key);
                }

                foreach (var key in stale) _lobbies.Remove(key);
            }
        }

        // ------------------------------------------------------------------
        // Connection
        // ------------------------------------------------------------------
        private void StartListening()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, GamePort);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.ExclusiveAddressUse = false;
                _listener.Start();
                _listener.BeginAcceptTcpClient(OnClientAccepted, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyNetwork] Could not listen on port {GamePort}. Is another copy of the game " +
                               $"already hosting? {e.Message}");
            }
        }

        private void OnClientAccepted(IAsyncResult result)
        {
            try
            {
                _client = _listener.EndAcceptTcpClient(result);
                _stream = _client.GetStream();
            }
            catch
            {
                return;
            }

            // Stop advertising once somebody is in, so a third player cannot pile in.
            CloseBeacon();

            Send(new MsgHello { name = LocalPlayerName });
            StartReadLoop();

            _inbound.Enqueue(MarkerConnected);
        }

        private void ConnectTo(string address, int port)
        {
            var token = _beaconCancel?.Token ?? System.Threading.CancellationToken.None;

            Task.Run(async () =>
            {
                TcpClient client = null;
                try
                {
                    client = new TcpClient();
                    var connect = client.ConnectAsync(address, port);
                    var timeout = Task.Delay(ConnectTimeoutMs, token);

                    if (await Task.WhenAny(connect, timeout) != connect)
                    {
                        Debug.LogWarning($"[LobbyNetwork] Timed out connecting to {address}:{port}.");
                        client.Close();
                        _inbound.Enqueue(MarkerDisconnected);
                        return;
                    }

                    // Surface the connection failure rather than silently never connecting.
                    if (client.Connected)
                    {
                        _client = client;
                        _stream = client.GetStream();
                        Send(new MsgHello { name = LocalPlayerName });
                        StartReadLoop();
                        _inbound.Enqueue(MarkerConnected);
                    }
                    else
                    {
                        client.Close();
                        _inbound.Enqueue(MarkerDisconnected);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LobbyNetwork] Connection to {address}:{port} failed: {e.Message}");
                    client?.Close();
                    _inbound.Enqueue(MarkerDisconnected);
                }
            }, token);
        }

        private void StartReadLoop()
        {
            // The client stops looking for lobbies once it is in one.
            if (IsClient) CloseBeacon();

            Task.Run(() =>
            {
                try
                {
                    while (_client != null && _client.Connected)
                    {
                        byte[] header = ReadExact(4);
                        if (header == null) break;

                        int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                        if (length <= 0 || length > MaxMessageBytes) break;

                        byte[] body = ReadExact(length);
                        if (body == null) break;

                        _inbound.Enqueue(Encoding.UTF8.GetString(body));
                    }
                }
                catch { /* the other end went away; the marker below reports it */ }

                _inbound.Enqueue(MarkerDisconnected);
            });
        }

        /// <summary>Null means "the peer disconnected", as opposed to a null message.</summary>
        private const string MarkerConnected = "<<connected>>";
        private const string MarkerDisconnected = "<<disconnected>>";

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int read = 0;

            while (read < count)
            {
                int n = _stream.Read(buffer, read, count - read);
                if (n <= 0) return null;
                read += n;
            }

            return buffer;
        }

        /// <summary>Sends one message. Safe to call from the main thread.</summary>
        public void Send(object message)
        {
            if (_stream == null || !_stream.CanWrite) return;

            string json;
            try { json = JsonUtility.ToJson(message); }
            catch { return; }

            byte[] body = Encoding.UTF8.GetBytes(json);
            if (body.Length == 0 || body.Length > MaxMessageBytes) return;

            // Big-endian length, then the payload. Anything reading this can frame it again.
            int length = body.Length;
            byte[] header = new byte[4];
            header[0] = (byte)(length >> 24);
            header[1] = (byte)(length >> 16);
            header[2] = (byte)(length >> 8);
            header[3] = (byte)length;

            try
            {
                _stream.Write(header, 0, 4);
                _stream.Write(body, 0, body.Length);
            }
            catch { /* connection is gone; the read loop will report it */ }
        }

        // ------------------------------------------------------------------
        // Inbound - always handled on the main thread
        // ------------------------------------------------------------------
        private void PumpInbound()
        {
            while (_inbound.TryDequeue(out string json))
            {
                if (json == MarkerConnected) { HandleConnected(); continue; }
                if (json == MarkerDisconnected) { HandleDisconnected(); continue; }
                HandleMessage(json);
            }
        }

        private void HandleConnected()
        {
            Debug.Log($"[LobbyNetwork] Connected as {_role}.");
            OnConnected?.Invoke();
        }

        private void HandleDisconnected()
        {
            bool wasConnected = IsConnected || _stream != null;
            CloseConnection();
            CloseBeacon();

            if (wasConnected)
            {
                Debug.Log("[LobbyNetwork] The other player disconnected.");
                OnDisconnected?.Invoke();
            }
        }

        private void HandleMessage(string json)
        {
            MsgKind kind;
            try { kind = JsonUtility.FromJson<MsgKind>(json); }
            catch { return; }

            if (kind == null || string.IsNullOrEmpty(kind.t)) return;

            switch (kind.t)
            {
                case "hello":
                    var hello = JsonUtility.FromJson<MsgHello>(json);
                    RemotePlayerName = hello?.name;
                    if (!string.IsNullOrEmpty(RemotePlayerName)) OnRemoteNamed?.Invoke(RemotePlayerName);
                    break;

                case "lobby":
                    var lobby = JsonUtility.FromJson<MsgLobby>(json);
                    OnLobbyRoster?.Invoke(lobby?.names ?? Array.Empty<string>());
                    break;

                case "start":
                    var start = JsonUtility.FromJson<MsgStart>(json);
                    MatchPreset preset = null;
                    if (start != null && !string.IsNullOrEmpty(start.presetJson))
                    {
                        try { preset = JsonUtility.FromJson<MatchPreset>(start.presetJson); }
                        catch { preset = null; }
                    }
                    PendingPreset = preset;
                    OnStartMatch?.Invoke(preset, start?.scene);
                    break;

                case "tf":
                    OnTransform?.Invoke(JsonUtility.FromJson<MsgTransform>(json));
                    break;

                case "phase":
                    OnPhase?.Invoke(JsonUtility.FromJson<MsgPhase>(json));
                    break;

                case "score":
                    OnScore?.Invoke(JsonUtility.FromJson<MsgScore>(json));
                    break;

                case "round":
                    OnRound?.Invoke(JsonUtility.FromJson<MsgRound>(json));
                    break;

                case "damage":
                    var dmg = JsonUtility.FromJson<MsgDamage>(json);
                    if (dmg != null) OnDamage?.Invoke(dmg.amount, new Vector3(dmg.x, dmg.y, dmg.z));
                    break;

                case "death":
                    OnRemoteDeath?.Invoke();
                    break;

                case "zone":
                    OnZone?.Invoke(JsonUtility.FromJson<MsgZone>(json));
                    break;

                case "lock":
                    var pick = JsonUtility.FromJson<MsgClassLock>(json);
                    if (pick != null) OnClassLock?.Invoke(pick.index, pick.locked);
                    break;

                case "chat":
                    var chat = JsonUtility.FromJson<MsgChat>(json);
                    if (chat != null && !string.IsNullOrEmpty(chat.text))
                        OnChat?.Invoke(chat.sender ?? "Player", chat.text);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Outbound helpers, so callers do not have to know the message shape
        // ------------------------------------------------------------------
        public void SendLobbyRoster(string[] names) => Send(new MsgLobby { names = names });

        public void SendTransform(Vector3 position, Quaternion rotation)
        {
            if (!IsConnected) return;

            var e = rotation.eulerAngles;
            Send(new MsgTransform { x = position.x, y = position.y, z = position.z,
                                    rx = e.x, ry = e.y, rz = e.z });
        }

        public void SendPhase(int state, float duration, float remaining) =>
            Send(new MsgPhase { state = state, duration = duration, remaining = remaining });

        public void SendScore(MsgScore score) => Send(score);

        /// <summary>Tells the other machine who took the round and how.</summary>
        public void SendRound(int winner, int reason) =>
            Send(new MsgRound { winner = winner, reason = reason });

        public void SendDamage(float amount, Vector3 point)
        {
            if (!IsConnected) return;
            Send(new MsgDamage { amount = amount, x = point.x, y = point.y, z = point.z });
        }

        public void SendDeath() => Send(new MsgDeath());

        /// <summary>
        /// Tells the other machine that an ability zone was deployed, so it can spawn its own
        /// copy of the smoke, the wall or the field. Skipped when not connected - an offline
        /// match simply has the one local zone, which is correct.
        /// </summary>
        public void SendZone(int kind, Vector3 point, float radius, float duration,
                             float tickDamage, float slow, float pull)
        {
            if (!IsConnected) return;

            Send(new MsgZone
            {
                kind = kind,
                x = point.x, y = point.y, z = point.z,
                radius = radius,
                duration = duration,
                tickDamage = tickDamage,
                slow = slow,
                pull = pull
            });
        }

        /// <summary>
        /// Tells the other machine which class our player is on, and whether they have locked
        /// it. Only sent while connected - offline there is nobody to show the pick to, and
        /// the class is applied locally regardless.
        /// </summary>
        public void SendClassLock(int index, bool locked)
        {
            if (!IsConnected) return;

            Send(new MsgClassLock { index = index, locked = locked });
        }

        /// <summary>Sends a lobby chat line. The local name is stamped here, so a message
        /// always carries who said it even after it crosses the wire.</summary>
        public void SendChat(string text)
        {
            if (!IsConnected) return;
            if (string.IsNullOrEmpty(text)) return;

            Send(new MsgChat { sender = LocalPlayerName, text = text });
        }

        // ------------------------------------------------------------------
        // Networking utilities
        // ------------------------------------------------------------------
        private static string GetLocalIp()
        {
            try
            {
                var entry = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in entry.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
                }
            }
            catch { }

            return "127.0.0.1";
        }
    }
}
