using System.Collections.Generic;
using ProjectLEA.Manuel.Managers;
using ProjectLEA.Manuel.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectLEA.Manuel.Net
{
    /// <summary>
    /// The lobby: what appears after you press PLAY.
    ///
    ///   Root   - JOIN or CREATE
    ///   Create - pick a game-mode preset, then start hosting and wait in the lobby
    ///   Join   - every lobby heard on the network, plus a manual address box for when
    ///            discovery does not see one
    ///   Lobby  - both players' names, and a START button that only the host can press
    ///
    /// Everything is drawn in code for the same reason the settings panel is: no EventSystem
    /// exists anywhere in this project, and hand-rolling the hit-testing keeps it that way.
    /// The single design rule is that this never touches a socket itself - it calls into
    /// <see cref="LobbyNetwork"/> and listens to that component's events.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1200f);

        [SerializeField] private int titleFontSize = 46;
        [SerializeField] private int bodyFontSize = 24;
        [SerializeField] private int smallFontSize = 18;
        [SerializeField] private int buttonFontSize = 28;

        [SerializeField] private float buttonWidth = 460f;
        [SerializeField] private float buttonHeight = 74f;
        [SerializeField] private float buttonSpacing = 16f;

        [SerializeField] private Color backdropColor = new Color(0.03f, 0.04f, 0.06f, 0.96f);
        [SerializeField] private Color panelColor = new Color(0.07f, 0.08f, 0.11f, 0.98f);
        [SerializeField] private Color buttonColor = new Color(1f, 1f, 1f, 0.07f);
        [SerializeField] private Color buttonHoverColor = new Color(0.20f, 0.78f, 0.62f, 0.85f);
        [SerializeField] private Color textColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        [SerializeField] private Color dimColor = new Color(0.60f, 0.64f, 0.70f, 1f);
        [SerializeField] private Color accentColor = new Color(0.20f, 0.78f, 0.62f, 1f);
        [SerializeField] private Color rowColor = new Color(1f, 1f, 1f, 0.05f);
        [SerializeField] private Color rowHoverColor = new Color(1f, 1f, 1f, 0.12f);

        [Header("Headings and cheats")]
        [SerializeField] private Color headingColor = new Color(0.20f, 0.78f, 0.62f, 1f);
        [SerializeField] private Color ruleColor = new Color(1f, 1f, 1f, 0.10f);
        [SerializeField] private Color onColor = new Color(0.20f, 0.78f, 0.62f, 1f);
        [SerializeField] private Color offColor = new Color(0.62f, 0.66f, 0.72f, 1f);
        [SerializeField] private Color trackOffColor = new Color(1f, 1f, 1f, 0.12f);
        [SerializeField] private Color knobColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        [SerializeField] private Color hintColor = new Color(0.55f, 0.59f, 0.65f, 1f);

        [Tooltip("Presets are looked up here so the host can offer every game mode that exists.")]
        [SerializeField] private string presetSearchPath = "Manuel/Presets";

        [Tooltip("Scene the host loads when it starts the match.")]
        [SerializeField] private string matchSceneName = "Manuel";

        // Named LobbyScreen on purpose: a plain "Screen" would shadow UnityEngine.Screen,
        // and every Screen.width / Screen.height reference below would stop compiling.
        private enum LobbyScreen { Closed, Root, Create, Join, Lobby }

        private class Button
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public System.Action OnClick;
        }

        private class Row
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public Text Value;
            public System.Action OnClick;
        }

        /// <summary>A setting the host steps through left-to-right by clicking the row.</summary>
        private class StepRow
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public Text Value;
            public System.Func<float> Read;
            public System.Action<float> Write;
            public float[] Steps;
            public System.Func<float, string> Format;
        }

        /// <summary>A cheat: clicking flips it, and the knob slides to match.</summary>
        private class ToggleRow
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public Text State;
            public RectTransform Knob;
            public System.Func<bool> Read;
            public System.Action<bool> Write;
        }

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<StepRow> _steps = new List<StepRow>();
        private readonly List<ToggleRow> _toggles = new List<ToggleRow>();

        /// <summary>
        /// The preset the host is currently editing. Always a clone, never the asset on disk:
        /// moving a slider would otherwise rewrite Standard.asset in the Editor.
        /// </summary>
        private MatchPreset _editing;

        // Horizontal divider rules under the section headings.
        private readonly List<Image> _rules = new List<Image>();

        private GameObject _canvasObject;
        private RectTransform _panel;
        private RectTransform _glow;
        private RectTransform _headerRule;
        private Text _title;
        private Text _status;
        private Font _font;

        private LobbyScreen _screen = LobbyScreen.Closed;
        private readonly List<MatchPreset> _presets = new List<MatchPreset>();
        private int _presetIndex;

        // Manual address entry, for lobbies discovery cannot see.
        private string _manualAddress = "127.0.0.1";
        private bool _addressFocused;

        // The name the player types on the root screen, before they host or join. Pushed into
        // the session as soon as it changes, so the host always sees who actually joined.
        private string _playerName;
        private bool _nameFocused;

        // Lobby chat: a rolling log plus the line the player is typing.
        private readonly List<string> _chatLog = new List<string>();
        private string _chatInput = string.Empty;
        private bool _chatFocused;

        private const int MaxNameLength = 16;
        private const int MaxChatLength = 80;
        private const int MaxChatLines = 9;

        // Rebuilt only when the set of lobbies actually changes, to avoid churning the UI.
        private string _lastLobbySignature = string.Empty;

        private LobbyNetwork _net;
        private float _uiScale = 1f;
        private int _builtWidth;
        private int _builtHeight;

        private float S(float v) => v * _uiScale;
        private int F(int v) => Mathf.Max(1, Mathf.RoundToInt(v * _uiScale));

        /// <summary>True while this UI is showing anything. The menu uses it to keep out of the way.</summary>
        public bool IsOpen => _screen != LobbyScreen.Closed;

        private void Awake()
        {
            _font = GetBuiltinFont();

            // The network session needs its own root object: it survives the scene load into
            // the match, and it must not take this menu, the settings panel or the lobby UI
            // along for the ride.
            if (_net == null)
            {
                // A session that outlived a previous match is reused rather than replaced, so
                // a stale socket is not left holding a dead LobbyUI's event handlers.
                _net = LobbyNetwork.Instance;

                if (_net == null)
                {
                    var host = new GameObject("LobbyNetwork");
                    _net = host.AddComponent<LobbyNetwork>();
                }
            }

            ComputeScale();
            Build();
            Close();

            // The session seeds the name from the machine name; the player edits it on the
            // root screen from there.
            _playerName = _net != null ? _net.LocalPlayerName : "Player";
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_net == null) return;

            _net.OnConnected += HandleConnected;
            _net.OnDisconnected += HandleDisconnected;
            _net.OnRemoteNamed += HandleRemoteNamed;
            _net.OnStartMatch += HandleStartMatch;
            _net.OnChat += HandleChatReceived;
        }

        private void Unsubscribe()
        {
            if (_net == null) return;

            _net.OnConnected -= HandleConnected;
            _net.OnDisconnected -= HandleDisconnected;
            _net.OnRemoteNamed -= HandleRemoteNamed;
            _net.OnStartMatch -= HandleStartMatch;
            _net.OnChat -= HandleChatReceived;
        }

        private void Update()
        {
            if (Screen.width != _builtWidth || Screen.height != _builtHeight) Rebuild();

            HandleTyping();
            RefreshLobbyList();
            HandlePointer();

            // Clicks rebuild the UI, which clears the very lists the pointer loop is walking.
            // Deferring the rebuild to here keeps that loop safe.
            if (_pendingRebuild)
            {
                _pendingRebuild = false;
                Rebuild();
            }
        }

        private bool _pendingRebuild;

        // ------------------------------------------------------------------
        // Opening / closing
        // ------------------------------------------------------------------
        /// <summary>Shows the root: JOIN or CREATE.</summary>
        public void Open()
        {
            _screen = LobbyScreen.Root;
            ClearFocus();
            Rebuild();
        }

        public void Close()
        {
            _screen = LobbyScreen.Closed;
            ClearFocus();
            Rebuild();
        }

        private void Show(LobbyScreen screen)
        {
            _screen = screen;
            ClearFocus();
            Rebuild();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------
        private void ComputeScale()
        {
            _uiScale = Mathf.Max(0.2f, Mathf.Min(Screen.width / referenceResolution.x,
                                                 Screen.height / referenceResolution.y));
            _builtWidth = Screen.width;
            _builtHeight = Screen.height;
        }

        private void Rebuild()
        {
            if (_canvasObject != null) Destroy(_canvasObject);
            _buttons.Clear();
            _rows.Clear();
            _steps.Clear();
            _toggles.Clear();
            _rules.Clear();

            ComputeScale();
            Build();
            ApplyVisibility();
        }

        private void Build()
        {
            _canvasObject = new GameObject("LobbyCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.transform.SetParent(transform, false);

            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            var root = (RectTransform)_canvasObject.transform;

            var backdrop = CreateRect("Backdrop", root);
            Stretch(backdrop, 0f);
            var backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.sprite = RoundedSpriteFactory.Gradient;
            backdropImage.type = Image.Type.Simple;
            backdropImage.color = backdropColor;

            // A soft glow plate behind the panel: larger, translucent, offset down a touch so
            // it reads as a drop shadow rather than a border.
            var glow = CreateRect("PanelGlow", root);
            glow.anchorMin = new Vector2(0.5f, 0.5f);
            glow.anchorMax = new Vector2(0.5f, 0.5f);
            glow.pivot = new Vector2(0.5f, 0.5f);
            glow.sizeDelta = new Vector2(S(1020f), S(860f));
            glow.anchoredPosition = new Vector2(S(8f), -S(10f));
            _glow = glow;
            var glowImage = glow.gameObject.AddComponent<Image>();
            glowImage.sprite = RoundedSpriteFactory.Rounded;
            glowImage.type = Image.Type.Sliced;
            glowImage.color = new Color(0f, 0f, 0f, 0.45f);

            _panel = CreateRect("Panel", root);
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(S(980f), S(820f));
            _panel.anchoredPosition = Vector2.zero;
            var panelImage = _panel.gameObject.AddComponent<Image>();
            panelImage.sprite = RoundedSpriteFactory.Rounded;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = panelColor;

            // A thin accent rule across the very top of the panel, as a header line.
            var header = CreateRect("HeaderRule", _panel);
            header.anchorMin = new Vector2(0.5f, 1f);
            header.anchorMax = new Vector2(0.5f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(S(900f), S(4f));
            header.anchoredPosition = new Vector2(0f, -S(2f));
            _headerRule = header;
            var headerImage = header.gameObject.AddComponent<Image>();
            headerImage.sprite = RoundedSpriteFactory.Small;
            headerImage.type = Image.Type.Sliced;
            headerImage.color = accentColor;

            _title = AddLabel(_panel, string.Empty, new Vector2(S(0f), -S(36f)), new Vector2(S(980f), S(56f)),
                              F(titleFontSize), TextAnchor.UpperCenter, accentColor);

            _status = AddLabel(_panel, string.Empty, new Vector2(S(0f), -S(100f)), new Vector2(S(920f), S(30f)),
                               F(smallFontSize), TextAnchor.UpperCenter, dimColor);

            switch (_screen)
            {
                case LobbyScreen.Root: BuildRoot(); break;
                case LobbyScreen.Create: BuildCreate(); break;
                case LobbyScreen.Join: BuildJoin(); break;
                case LobbyScreen.Lobby: BuildLobby(); break;
            }
        }

        private void ApplyVisibility()
        {
            bool visible = _screen != LobbyScreen.Closed;
            _canvasObject.SetActive(visible);

            if (visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // ------------------------------------------------------------------
        // Screens
        // ------------------------------------------------------------------
        /// <summary>The two-column screens need a wider panel than the stacked ones, and the
        /// glow plate and header rule are sized for the standard panel in Build().</summary>
        private void UseWidePanel()
        {
            _panel.sizeDelta = new Vector2(S(1300f), S(1060f));

            if (_glow != null)
                _glow.sizeDelta = _panel.sizeDelta + new Vector2(S(40f), S(40f));

            if (_headerRule != null)
                _headerRule.sizeDelta = new Vector2(_panel.sizeDelta.x - S(80f), S(4f));
        }

        private void BuildRoot()
        {
            _title.text = "FIND A MATCH";
            _status.text = "Set your name, then create a lobby or join one.";

            float y = -S(170f);

            // The name first, so the host always knows who joined. It seeds from the machine
            // name and is pushed into the session as it changes.
            AddTextField("PlayerName", _playerName, y, S(620f), _nameFocused,
                         () => FocusField(ref _nameFocused));
            y -= S(96f);

            AddButton(_panel, "CREATE", y, BeginCreate); y -= S(buttonHeight + buttonSpacing);
            AddButton(_panel, "JOIN", y, () => Show(LobbyScreen.Join)); y -= S(buttonHeight + buttonSpacing);
            AddButton(_panel, "BACK", y, Close);
        }

        private void BuildCreate()
        {
            _presets.Clear();
            _presets.AddRange(LoadPresets());

            // The settings screen is wider and taller than the other screens, because it holds
            // two columns of rows rather than a single stacked list.
            UseWidePanel();

            _title.text = "CREATE A LOBBY";
            _status.text = "Click a value to step it. Right-click steps back. The other player gets the same rules.";

            if (_presets.Count == 0)
            {
                AddLabel(_panel, "No presets found. Create one via Assets > Create > ProjectLEA > Match Preset.",
                         new Vector2(S(0f), -S(180f)), new Vector2(S(1200f), S(60f)),
                         F(bodyFontSize), TextAnchor.UpperCenter, dimColor);
            }
            else
            {
                if (_presetIndex < 0 || _presetIndex >= _presets.Count) _presetIndex = 0;

                // Re-clone whenever the selected asset changed, so switching presets discards
                // the previous one's edits rather than carrying them across.
                var selected = _presets[_presetIndex];
                if (_editing == null || _editing.name != (selected != null ? selected.name : "None"))
                {
                    _editing = selected != null ? selected.Clone() : FreshDefaultPreset();
                }

                float leftX = -S(305f);
                float rightX = S(305f);
                float y = -S(160f);

                // ---- Left column: the rules -------------------------------------------
                AddPresetRow(leftX, y);
                y -= S(122f);

                y = AddHeading(leftX, y, "MATCH RULES");

                y = AddStep(leftX, y, "Rounds to win", () => _editing.roundsToWin,
                            v => _editing.roundsToWin = Mathf.RoundToInt(v), RoundsSteps, FormatCount);
                y = AddStep(leftX, y, "Round length", () => _editing.roundSeconds,
                            v => _editing.roundSeconds = v, RoundSteps, FormatTime);
                y = AddStep(leftX, y, "Buy phase", () => _editing.buySeconds,
                            v => _editing.buySeconds = v, BuySteps, FormatTime);

                // ---- Right column: the fun switches, then the economy ----------------
                float ry = -S(160f);

                ry = AddHeading(rightX, ry, "CHEATS");

                ry = AddToggle(rightX, ry, "One-hit kills", () => _editing.oneHitKills,
                               v => _editing.oneHitKills = v, "Every hit is lethal");
                ry = AddToggle(rightX, ry, "Infinite money", () => _editing.infiniteMoney,
                               v => _editing.infiniteMoney = v, "The shop never runs dry");
                ry = AddToggle(rightX, ry, "Infinite abilities", () => _editing.infiniteAbilities,
                               v => _editing.infiniteAbilities = v, "No ability cooldowns");
                ry = AddToggle(rightX, ry, "Low gravity", () => _editing.lowGravity,
                               v => _editing.lowGravity = v, "Higher, slower jumps");
                ry = AddToggle(rightX, ry, "Super speed", () => _editing.superSpeed,
                               v => _editing.superSpeed = v, "1.6x movement speed");
                ry = AddToggle(rightX, ry, "God mode", () => _editing.godMode,
                               v => _editing.godMode = v, "Your body takes no damage");

                ry -= S(6f);

                ry = AddHeading(rightX, ry, "ECONOMY");

                ry = AddStep(rightX, ry, "Starting money", () => _editing.startingMoney,
                            v => _editing.startingMoney = Mathf.RoundToInt(v), MoneySteps, FormatMoney);
                ry = AddStep(rightX, ry, "Kill reward", () => _editing.killReward,
                            v => _editing.killReward = Mathf.RoundToInt(v), RewardSteps, FormatMoney);
                ry = AddStep(rightX, ry, "Round win", () => _editing.roundWinReward,
                            v => _editing.roundWinReward = Mathf.RoundToInt(v), RewardSteps, FormatMoney);
                ry = AddStep(rightX, ry, "Round loss", () => _editing.roundLossReward,
                            v => _editing.roundLossReward = Mathf.RoundToInt(v), RewardSteps, FormatMoney);
                ry = AddStep(rightX, ry, "Credit cap", () => _editing.creditCap,
                            v => _editing.creditCap = Mathf.RoundToInt(v), CapSteps, FormatMoney);

                // ---- Footer under the rules: what the cheats mean for this match -------
                AddLabel(_panel, _editing.CheatsEnabled ? "Cheats: " + _editing.CheatSummary()
                                                        : "No cheats - a clean match.",
                         new Vector2(leftX, y - S(14f)), new Vector2(S(560f), S(30f)),
                         F(smallFontSize), TextAnchor.UpperLeft,
                         _editing.CheatsEnabled ? accentColor : hintColor);

                AddLabel(_panel, "Cheats apply to both players and ride the same rules packet.",
                         new Vector2(leftX, y - S(52f)), new Vector2(S(560f), S(30f)),
                         F(smallFontSize), TextAnchor.UpperLeft, hintColor);
            }

            // Side by side, so the whole column of settings stays visible above them.
            AddButton(_panel, "HOST", -S(880f), HostWithCurrentPreset, -S(305f), S(560f));
            AddButton(_panel, "BACK", -S(880f), () => Show(LobbyScreen.Root), S(305f), S(560f));
        }

        // ------------------------------------------------------------------
        // Settings rows
        // ------------------------------------------------------------------
        // Curated ladders rather than free sliders: a slider over 60-300 seconds spends most of
        // its travel on numbers nobody picks, and steps keep every option one click away.
        private static readonly float[] RoundSteps = { 60f, 80f, 100f, 120f, 150f, 180f, 240f };
        private static readonly float[] BuySteps = { 15f, 20f, 30f, 45f, 60f };
        private static readonly float[] RoundsSteps = { 1f, 3f, 5f, 7f, 9f, 11f, 12f, 13f, 15f, 21f, 25f };
        private static readonly float[] MoneySteps = { 0f, 400f, 800f, 1200f, 1600f, 2000f, 3000f, 5000f, 8000f };
        private static readonly float[] RewardSteps = { 0f, 100f, 150f, 200f, 300f, 400f, 500f, 1000f, 2000f, 3000f, 5000f };
        private static readonly float[] CapSteps = { 3000f, 5000f, 7000f, 8000f, 9000f, 12000f, 16000f };

        private static string FormatTime(float seconds)
        {
            int whole = Mathf.RoundToInt(seconds);
            int minutes = whole / 60;
            int rest = whole % 60;
            return minutes > 0 ? $"{minutes}:{rest:00}" : $"{whole}s";
        }

        private static string FormatCount(float value) => Mathf.RoundToInt(value).ToString();

        private static string FormatMoney(float value) => "${0}".Replace("{0}", Mathf.RoundToInt(value).ToString());

        /// <summary>A preset built from the code defaults, for when none exist on disk.</summary>
        private static MatchPreset FreshDefaultPreset()
        {
            var preset = ScriptableObject.CreateInstance<MatchPreset>();
            preset.name = "Default";
            return preset;
        }

        /// <summary>The preset picker, full-width in its column, with the description below.</summary>
        private void AddPresetRow(float x, float y)
        {
            var preset = _presets[_presetIndex];
            AddRow("Game mode", preset != null ? preset.name : "None",
                   () => { _presetIndex = (_presetIndex + 1) % _presets.Count; Rebuild(); },
                   y, true, x, S(560f));

            AddLabel(_panel, preset != null ? preset.Describe() : string.Empty,
                     new Vector2(x, y - S(80f)), new Vector2(S(560f), S(36f)),
                     F(smallFontSize), TextAnchor.UpperLeft, hintColor);
        }

        /// <summary>A section title with a hairline rule under it, to break the column up.</summary>
        private float AddHeading(float x, float y, string text)
        {
            AddLabel(_panel, text, new Vector2(x, y), new Vector2(S(560f), S(28f)),
                     F(bodyFontSize), TextAnchor.UpperLeft, headingColor);

            var rule = CreateRect("Rule", _panel);
            rule.anchorMin = new Vector2(0.5f, 1f);
            rule.anchorMax = new Vector2(0.5f, 1f);
            rule.pivot = new Vector2(0.5f, 1f);
            rule.sizeDelta = new Vector2(S(560f), S(2f));
            rule.anchoredPosition = new Vector2(x, y - S(32f));
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.sprite = RoundedSpriteFactory.Small;
            ruleImage.type = Image.Type.Sliced;
            ruleImage.color = ruleColor;
            _rules.Add(ruleImage);

            return y - S(58f);
        }

        /// <summary>A setting stepped by clicking. Left advances, right goes back.</summary>
        private float AddStep(float x, float y, string name,
                              System.Func<float> read, System.Action<float> write,
                              float[] steps, System.Func<float, string> format)
        {
            var row = CreateRect(name, _panel);
            row.anchorMin = new Vector2(0.5f, 1f);
            row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(S(560f), S(52f));
            row.anchoredPosition = new Vector2(x, y);

            var background = row.gameObject.AddComponent<Image>();
            background.sprite = RoundedSpriteFactory.Small;
            background.type = Image.Type.Sliced;
            background.color = rowColor;

            AddLabel(row, name, new Vector2(-S(140f), -S(6f)), new Vector2(S(260f), S(40f)),
                     F(bodyFontSize), TextAnchor.MiddleLeft, textColor);

            // Arrows either side of the value make it obvious the whole row is clickable.
            AddLabel(row, "<", new Vector2(S(70f), -S(6f)), new Vector2(S(30f), S(40f)),
                     F(bodyFontSize), TextAnchor.MiddleCenter, hintColor);
            var value = AddLabel(row, format(read()), new Vector2(S(160f), -S(6f)),
                                 new Vector2(S(140f), S(40f)), F(bodyFontSize),
                                 TextAnchor.MiddleRight, accentColor);
            AddLabel(row, ">", new Vector2(S(245f), -S(6f)), new Vector2(S(30f), S(40f)),
                     F(bodyFontSize), TextAnchor.MiddleCenter, hintColor);

            _steps.Add(new StepRow
            {
                Rect = row,
                Background = background,
                Label = null,
                Value = value,
                Read = read,
                Write = write,
                Steps = steps,
                Format = format
            });

            return y - S(58f);
        }

        /// <summary>A cheat with a sliding switch. Clicking anywhere on the row flips it.</summary>
        private float AddToggle(float x, float y, string name,
                                System.Func<bool> read, System.Action<bool> write,
                                string description)
        {
            var row = CreateRect(name, _panel);
            row.anchorMin = new Vector2(0.5f, 1f);
            row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(S(560f), S(52f));
            row.anchoredPosition = new Vector2(x, y);

            var background = row.gameObject.AddComponent<Image>();
            background.sprite = RoundedSpriteFactory.Small;
            background.type = Image.Type.Sliced;
            background.color = rowColor;

            // Name above, description below: the row is only 52 tall, but two small lines fit
            // and it keeps the wide switch clear of the text.
            AddLabel(row, name, new Vector2(-S(150f), -S(4f)), new Vector2(S(300f), S(24f)),
                     F(bodyFontSize), TextAnchor.MiddleLeft, textColor);
            AddLabel(row, description, new Vector2(-S(150f), -S(28f)), new Vector2(S(300f), S(20f)),
                     F(smallFontSize), TextAnchor.MiddleLeft, hintColor);

            // The switch: a pill track with a knob that slides right when on.
            var track = CreateRect("Track", row);
            track.anchorMin = new Vector2(1f, 0.5f);
            track.anchorMax = new Vector2(1f, 0.5f);
            track.pivot = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(S(86f), S(34f));
            track.anchoredPosition = new Vector2(-S(16f), 0f);
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.sprite = RoundedSpriteFactory.Small;
            trackImage.type = Image.Type.Sliced;
            trackImage.color = trackOffColor;

            var knob = CreateRect("Knob", track);
            knob.anchorMin = new Vector2(0f, 0.5f);
            knob.anchorMax = new Vector2(0f, 0.5f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            knob.sizeDelta = new Vector2(S(26f), S(26f));
            knob.anchoredPosition = new Vector2(S(17f), 0f);
            var knobImage = knob.gameObject.AddComponent<Image>();
            knobImage.sprite = RoundedSpriteFactory.Small;
            knobImage.type = Image.Type.Sliced;
            knobImage.color = knobColor;

            // The ON/OFF text sits just left of the track, so the switch and its label read as
            // one control.
            var state = AddLabel(row, read() ? "ON" : "OFF",
                                 new Vector2(S(108f), -S(6f)), new Vector2(S(70f), S(40f)),
                                 F(smallFontSize), TextAnchor.MiddleRight,
                                 read() ? onColor : offColor);

            _toggles.Add(new ToggleRow
            {
                Rect = row,
                Background = background,
                Label = null,
                State = state,
                Knob = knob,
                Read = read,
                Write = write
            });

            return y - S(58f);
        }

        private void BuildJoin()
        {
            _title.text = "JOIN A LOBBY";
            _status.text = "Lobbies on your network appear here automatically.";

            float y = -S(160f);

            // Discovered lobbies fill the middle of the panel. The list is rebuilt by
            // RefreshLobbyList when the set changes, so this only lays out the chrome.
            AddLabel(_panel, "AVAILABLE LOBBIES",
                     new Vector2(S(0f), y), new Vector2(S(900f), S(30f)),
                     F(smallFontSize), TextAnchor.UpperCenter, dimColor);
            y -= S(46f);

            _lobbyListTop = y;
            _lobbyListBottom = -S(560f);

            y = _lobbyListBottom;

            // Manual entry: the escape hatch for a LAN the broadcast cannot cross.
            AddLabel(_panel, "OR CONNECT DIRECTLY",
                     new Vector2(S(0f), y), new Vector2(S(900f), S(30f)),
                     F(smallFontSize), TextAnchor.UpperCenter, dimColor);
            y -= S(44f);

            AddTextField("Address", _manualAddress, y, S(620f), _addressFocused,
                         () => FocusField(ref _addressFocused));

            y -= S(80f);

            AddButton(_panel, "CONNECT", y, JoinManual);
            y -= S(buttonHeight + buttonSpacing);

            AddButton(_panel, "BACK", y, () => Show(LobbyScreen.Root));

            // Draw whatever is already known about, so the list is never blank on entry.
            PopulateLobbyRows();
        }

        private float _lobbyListTop;
        private float _lobbyListBottom;

        private void BuildLobby()
        {
            UseWidePanel();

            _title.text = "IN LOBBY";

            bool host = _net != null && _net.IsHost;
            string other = _net != null ? _net.RemotePlayerName : null;

            _status.text = host
                ? (string.IsNullOrEmpty(other) ? "Waiting for a player to join..." : "Both players are here. Start when you are ready.")
                : "Waiting for the host to start the game...";

            // ---- Left column: who is here, and what we are playing ----------------
            float leftX = -S(305f);
            float y = -S(170f);

            AddRow("You", _net != null ? _net.LocalPlayerName : "Player", null, y, false, leftX, S(560f));
            y -= S(92f);

            AddRow("Opponent", string.IsNullOrEmpty(other) ? "..." : other, null, y, false, leftX, S(560f));
            y -= S(92f);

            string presetName = host
                ? (_net.SelectedPreset != null ? _net.SelectedPreset.name : "Standard")
                : (_net.PendingPreset != null ? _net.PendingPreset.name : "Standard");

            AddRow("Game mode", presetName, null, y, false, leftX, S(560f));
            y -= S(110f);

            if (host)
            {
                // The host may only start once somebody is actually on the other end.
                bool canStart = !string.IsNullOrEmpty(other);
                AddButton(_panel, "START GAME", y, StartMatch, leftX, S(560f), canStart);

                if (!canStart)
                {
                    AddLabel(_panel, "START unlocks when a player has joined.",
                             new Vector2(leftX, y - S(buttonHeight + 14f)), new Vector2(S(560f), S(28f)),
                             F(smallFontSize), TextAnchor.UpperCenter, dimColor);
                }

                y -= S(buttonHeight + buttonSpacing + 30f);
            }
            else
            {
                AddLabel(_panel, "The host picks the rules and starts the match.",
                         new Vector2(leftX, y), new Vector2(S(560f), S(40f)),
                         F(smallFontSize), TextAnchor.UpperCenter, hintColor);
                y -= S(80f);
            }

            AddButton(_panel, "LEAVE", y, LeaveLobby, leftX, S(560f));

            // ---- Right column: the chat ------------------------------------------
            BuildChatPanel(S(305f), -S(170f));
        }

        /// <summary>
        /// The chat box: a rolling log of recent lines and a field to type into. Both players
        /// are on this screen together, which is why the chat lives here rather than in the
        /// menus either side of it.
        /// </summary>
        private void BuildChatPanel(float x, float y)
        {
            const float width = 560f;
            const float height = 700f;

            var box = CreateRect("Chat", _panel);
            box.anchorMin = new Vector2(0.5f, 1f);
            box.anchorMax = new Vector2(0.5f, 1f);
            box.pivot = new Vector2(0.5f, 1f);
            box.sizeDelta = new Vector2(S(width), S(height));
            box.anchoredPosition = new Vector2(x, y);

            var boxImage = box.gameObject.AddComponent<Image>();
            boxImage.sprite = RoundedSpriteFactory.Rounded;
            boxImage.type = Image.Type.Sliced;
            boxImage.color = new Color(panelColor.r, panelColor.g, panelColor.b, 1f);

            // Everything inside is laid out from the box's own top, so x=0 is its centre.
            AddLabel(box, "CHAT", new Vector2(S(0f), -S(16f)), new Vector2(S(width - 40f), S(28f)),
                     F(bodyFontSize), TextAnchor.UpperLeft, headingColor);

            var rule = CreateRect("ChatRule", box);
            rule.anchorMin = new Vector2(0.5f, 1f);
            rule.anchorMax = new Vector2(0.5f, 1f);
            rule.pivot = new Vector2(0.5f, 1f);
            rule.sizeDelta = new Vector2(S(width - 40f), S(2f));
            rule.anchoredPosition = new Vector2(0f, -S(52f));
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.sprite = RoundedSpriteFactory.Small;
            ruleImage.type = Image.Type.Sliced;
            ruleImage.color = ruleColor;

            // The log, newest at the bottom so the conversation reads top to bottom.
            float lineY = -S(66f);
            int startIndex = Mathf.Max(0, _chatLog.Count - MaxChatLines);

            for (int i = startIndex; i < _chatLog.Count; i++)
            {
                AddLabel(box, _chatLog[i], new Vector2(S(0f), lineY),
                         new Vector2(S(width - 40f), S(28f)),
                         F(smallFontSize), TextAnchor.UpperLeft,
                         i == _chatLog.Count - 1 ? textColor : dimColor);

                lineY -= S(32f);
            }

            if (_chatLog.Count == 0)
            {
                AddLabel(box, "Say hello. Enter sends, backspace deletes.",
                         new Vector2(S(0f), lineY), new Vector2(S(width - 40f), S(28f)),
                         F(smallFontSize), TextAnchor.UpperLeft, hintColor);
            }

            // The input box is pinned to the bottom of the chat panel, inside it.
            var input = CreateRect("ChatInput", box);
            input.anchorMin = new Vector2(0.5f, 0f);
            input.anchorMax = new Vector2(0.5f, 0f);
            input.pivot = new Vector2(0.5f, 0f);
            input.sizeDelta = new Vector2(S(width - 40f), S(58f));
            input.anchoredPosition = new Vector2(0f, S(16f));

            var inputImage = input.gameObject.AddComponent<Image>();
            inputImage.sprite = RoundedSpriteFactory.Small;
            inputImage.type = Image.Type.Sliced;
            inputImage.color = _chatFocused ? rowHoverColor : rowColor;

            AddLabel(input, string.IsNullOrEmpty(_chatInput) && !_chatFocused
                            ? "Type a message..."
                            : (_chatFocused ? _chatInput + "_" : _chatInput),
                     new Vector2(S(0f), -S(8f)), new Vector2(S(width - 70f), S(42f)),
                     F(bodyFontSize), TextAnchor.MiddleLeft,
                     _chatFocused ? accentColor : hintColor);

            _rows.Add(new Row
            {
                Rect = input,
                Background = inputImage,
                Label = null,
                Value = null,
                OnClick = () => FocusField(ref _chatFocused)
            });
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------
        private void BeginCreate()
        {
            Show(LobbyScreen.Create);
        }

        private void HostWithCurrentPreset()
        {
            // Hosts the clone the host has been editing, never the asset on disk.
            MatchPreset preset = _editing;

            if (preset == null)
            {
                // Rather than refuse to host, fall back to the defaults baked into the
                // managers. The match still runs; it just runs on the authored numbers.
                Debug.LogWarning("[LobbyUI] No preset selected - hosting with manager defaults.");
                preset = FreshDefaultPreset();
            }

            _net.StartHost(_net.LocalPlayerName, preset);
            Show(LobbyScreen.Lobby);
        }

        private void JoinManual()
        {
            _addressFocused = false;
            _net.Join(_manualAddress.Trim(), LobbyNetwork.GamePort, _net.LocalPlayerName);
            Show(LobbyScreen.Lobby);
        }

        private void JoinLobby(string address, int port)
        {
            _net.Join(address, port, _net.LocalPlayerName);
            Show(LobbyScreen.Lobby);
        }

        private void StartMatch()
        {
            if (!(_net != null && _net.IsHost)) return;

            // The host loads the match scene directly; the client gets pulled across by the
            // start message. Both sides then find NetMatchSync already in the scene.
            _net.StartMatch(matchSceneName);
            SceneManager.LoadScene(matchSceneName);
        }

        private void LeaveLobby()
        {
            _net.Shutdown();
            Show(LobbyScreen.Root);
        }

        // ------------------------------------------------------------------
        // Network events
        // ------------------------------------------------------------------
        private void HandleConnected()
        {
            // The roster only matters once we are in; the host announces itself, the client
            // will have already sent its own hello back.
            if (_screen == LobbyScreen.Closed) return;

            Rebuild();
        }

        private void HandleDisconnected()
        {
            if (_screen == LobbyScreen.Closed) return;

            // Kick back to the root rather than sitting in a lobby that no longer exists.
            Show(LobbyScreen.Root);
        }

        private void HandleRemoteNamed(string name)
        {
            if (_screen == LobbyScreen.Lobby) Rebuild();
        }

        private void HandleStartMatch(MatchPreset preset, string scene)
        {
            if (string.IsNullOrEmpty(scene)) return;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogWarning($"[LobbyUI] Host started a match in '{scene}', which is not in Build Settings.");
                return;
            }

            // The preset is applied when NetMatchSync comes up in the match scene.
            SceneManager.LoadScene(scene);
        }

        // ------------------------------------------------------------------
        // Discovery list
        // ------------------------------------------------------------------
        private void RefreshLobbyList()
        {
            if (_screen != LobbyScreen.Join) return;

            var lobbies = _net.GetLobbies();

            string signature = string.Empty;
            foreach (var lobby in lobbies) signature += lobby.Address + ":" + lobby.Port + "|";

            if (signature == _lastLobbySignature) return;
            _lastLobbySignature = signature;

            PopulateLobbyRows();
        }

        /// <summary>Draws the discovered lobbies between the header and the manual box.</summary>
        private void PopulateLobbyRows()
        {
            // Clear only the lobby rows: the manual-address row and the buttons stay.
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                if (_rows[i].Rect != null && _rows[i].Rect.name == "LobbyEntry")
                {
                    Destroy(_rows[i].Rect.gameObject);
                    _rows.RemoveAt(i);
                }
            }

            var lobbies = _net.GetLobbies();
            float y = _lobbyListTop;

            foreach (var lobby in lobbies)
            {
                var row = CreateRect("LobbyEntry", _panel);
                row.anchorMin = new Vector2(0.5f, 1f);
                row.anchorMax = new Vector2(0.5f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(S(760f), S(64f));
                row.anchoredPosition = new Vector2(0f, y);
                var entryImage = row.gameObject.AddComponent<Image>();
                entryImage.sprite = RoundedSpriteFactory.Small;
                entryImage.type = Image.Type.Sliced;
                entryImage.color = rowColor;

                AddLabel(row, lobby.Name, new Vector2(-S(136f), -S(10f)), new Vector2(S(440f), S(44f)),
                         F(bodyFontSize), TextAnchor.MiddleLeft, textColor);
                AddLabel(row, lobby.Address, new Vector2(S(226f), -S(10f)), new Vector2(S(260f), S(44f)),
                         F(smallFontSize), TextAnchor.MiddleRight, dimColor);

                // Captured per iteration: a captured loop variable would point every entry
                // at the last lobby in the list.
                string address = lobby.Address;
                int port = lobby.Port;

                _rows.Add(new Row
                {
                    Rect = row,
                    Background = row.GetComponent<Image>(),
                    Label = null,
                    Value = null,
                    OnClick = () => JoinLobby(address, port)
                });

                y -= S(74f);
            }

            if (lobbies.Count == 0)
            {
                AddLabel(_panel, "No lobbies found yet - keep looking, or connect directly below.",
                         new Vector2(S(0f), y), new Vector2(S(900f), S(30f)),
                         F(smallFontSize), TextAnchor.UpperCenter, dimColor);
            }
        }

        // ------------------------------------------------------------------
        // Typing
        // ------------------------------------------------------------------
        private void HandleTyping()
        {
            if (_screen == LobbyScreen.Closed) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            bool enter = keyboard[Key.Enter].wasPressedThisFrame || keyboard[Key.NumpadEnter].wasPressedThisFrame;
            bool changed = false;

            // Only one field is ever live at a time; Enter closes whichever it was.
            if (_nameFocused)
            {
                if (TypeInto(ref _playerName, MaxNameLength))
                {
                    if (_net != null) _net.LocalPlayerName = _playerName;
                    changed = true;
                }
                if (enter) { _nameFocused = false; changed = true; }
            }
            else if (_addressFocused)
            {
                if (TypeInto(ref _manualAddress, MaxNameLength)) changed = true;
                if (enter) { _addressFocused = false; changed = true; }
            }
            else if (_chatFocused)
            {
                if (TypeInto(ref _chatInput, MaxChatLength)) changed = true;

                // Enter sends. An empty line just closes the box.
                if (enter)
                {
                    string text = _chatInput.Trim();
                    _chatInput = string.Empty;
                    _chatFocused = false;

                    if (!string.IsNullOrEmpty(text) && _net != null)
                    {
                        _net.SendChat(text);
                        AppendChatLine(_net.LocalPlayerName, text);
                    }

                    changed = true;
                }
            }

            if (changed) Rebuild();
        }

        /// <summary>
        /// Letters, digits, space, dot and backspace into the given string. No Enter handling:
        /// the caller decides what a finished line means (send, or just close the field).
        /// </summary>
        private static bool TypeInto(ref string text, int maxLength)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;

            bool changed = false;
            bool shift = keyboard[Key.LeftShift].isPressed || keyboard[Key.RightShift].isPressed;

            // A..Z are contiguous in the Key enum, so a letter is an offset from Key.A.
            for (int letter = 0; letter < 26; letter++)
            {
                var key = (Key)((int)Key.A + letter);
                if (!keyboard[key].wasPressedThisFrame) continue;
                if (text.Length >= maxLength) break;

                char c = (char)('a' + letter);
                if (shift) c = char.ToUpper(c, System.Globalization.CultureInfo.InvariantCulture);

                text += c.ToString();
                changed = true;
            }

            for (int digit = 0; digit < 10; digit++)
            {
                var key = DigitKey(digit);
                if (key == Key.None || !keyboard[key].wasPressedThisFrame) continue;
                if (text.Length >= maxLength) break;

                text += digit.ToString();
                changed = true;
            }

            if (keyboard[Key.Space].wasPressedThisFrame && text.Length < maxLength)
            {
                text += " ";
                changed = true;
            }

            if (keyboard[Key.Period].wasPressedThisFrame && text.Length < maxLength)
            {
                text += ".";
                changed = true;
            }

            if (keyboard[Key.Backspace].wasPressedThisFrame && text.Length > 0)
            {
                text = text.Substring(0, text.Length - 1);
                changed = true;
            }

            return changed;
        }

        /// <summary>Key.Digit1 through Key.Digit9, then Key.Digit0.</summary>
        private static Key DigitKey(int digit)
        {
            switch (digit)
            {
                case 0: return Key.Digit0;
                case 1: return Key.Digit1;
                case 2: return Key.Digit2;
                case 3: return Key.Digit3;
                case 4: return Key.Digit4;
                case 5: return Key.Digit5;
                case 6: return Key.Digit6;
                case 7: return Key.Digit7;
                case 8: return Key.Digit8;
                case 9: return Key.Digit9;
                default: return Key.None;
            }
        }

        // ------------------------------------------------------------------
        // Chat
        // ------------------------------------------------------------------
        /// <summary>A line arrived from the other machine. Raised on the main thread.</summary>
        private void HandleChatReceived(string sender, string text)
        {
            AppendChatLine(sender, text);
        }

        /// <summary>Adds one line and keeps the log from growing without bound.</summary>
        private void AppendChatLine(string sender, string text)
        {
            string name = string.IsNullOrEmpty(sender) ? "Player" : sender;
            string line = $"{name}: {text}";

            _chatLog.Add(line);
            while (_chatLog.Count > MaxChatLines) _chatLog.RemoveAt(0);

            // Rebuild so the new line appears. Deferred: this can arrive inside the pointer
            // loop's iteration, and rebuilding there would destroy the lists being walked.
            _pendingRebuild = true;
        }

        /// <summary>Takes the typing focus. Only one field is live at a time.</summary>
        private void FocusField(ref bool field)
        {
            _nameFocused = false;
            _addressFocused = false;
            _chatFocused = false;
            field = true;

            // Redraw so the caret and the highlight appear at once. Safe to defer: this only
            // ever runs from a click handler, which are already collected and replayed after
            // the pointer loops, so nothing is iterating the lists this rebuilds.
            _pendingRebuild = true;
        }

        /// <summary>Drops the typing focus. Called on every screen change, so keys the player
        /// presses while navigating never land in a field they can no longer see.</summary>
        private void ClearFocus()
        {
            _nameFocused = false;
            _addressFocused = false;
            _chatFocused = false;
        }

        /// <summary>
        /// A clickable box showing live text the player types into. The value is re-read on
        /// every rebuild, and each keystroke rebuilds, so the box always shows what is typed.
        /// </summary>
        private RectTransform AddTextField(string rowName, string value, float y, float width,
                                           bool focused, System.Action onClick)
        {
            var row = CreateRect(rowName, _panel);
            row.anchorMin = new Vector2(0.5f, 1f);
            row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(S(width), S(62f));
            row.anchoredPosition = new Vector2(0f, y);

            var image = row.gameObject.AddComponent<Image>();
            image.sprite = RoundedSpriteFactory.Small;
            image.type = Image.Type.Sliced;
            image.color = focused ? rowHoverColor : rowColor;

            // A focused field shows a caret, so it is obvious which box the keys go into.
            AddLabel(row, focused ? value + "_" : value,
                     new Vector2(S(0f), -S(10f)), new Vector2(S(width - 40f), S(42f)),
                     F(bodyFontSize), TextAnchor.MiddleLeft, focused ? accentColor : textColor);

            _rows.Add(new Row
            {
                Rect = row,
                Background = image,
                Label = null,
                Value = null,
                OnClick = onClick
            });

            return row;
        }

        // ------------------------------------------------------------------
        // Pointer
        // ------------------------------------------------------------------
        private void HandlePointer()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pointer = mouse.position.ReadValue();
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            bool rightPressed = mouse.rightButton.wasPressedThisFrame;

            // Clicks are collected and run after the loops: a click usually rebuilds the UI,
            // which clears these lists while they are being walked.
            var clicks = new System.Collections.Generic.List<System.Action>(4);

            foreach (var button in _buttons)
            {
                if (button.Rect == null) continue;

                bool hover = Hit(button.Rect, pointer);
                button.Background.color = hover ? buttonHoverColor : buttonColor;
                button.Label.color = hover ? backdropColor : textColor;

                if (hover && pressed && button.OnClick != null) clicks.Add(button.OnClick);
            }

            foreach (var row in _rows)
            {
                if (row.Rect == null) continue;

                bool hover = Hit(row.Rect, pointer);
                if (row.Background != null) row.Background.color = hover ? rowHoverColor : rowColor;

                if (hover && pressed && row.OnClick != null) clicks.Add(row.OnClick);
            }

            foreach (var step in _steps)
            {
                if (step.Rect == null) continue;

                bool hover = Hit(step.Rect, pointer);
                step.Background.color = hover ? rowHoverColor : rowColor;
                if (step.Value != null) step.Value.color = hover ? Color.white : accentColor;

                if (!hover) continue;

                if (pressed) clicks.Add(() => StepSetting(step, forward: true));
                else if (rightPressed) clicks.Add(() => StepSetting(step, forward: false));
            }

            foreach (var toggle in _toggles)
            {
                if (toggle.Rect == null) continue;

                bool hover = Hit(toggle.Rect, pointer);
                toggle.Background.color = hover ? rowHoverColor : rowColor;

                bool on = toggle.Read();

                // The knob slides and the track fills in over a couple of frames, so a flip
                // reads as a switch throwing rather than a label swapping.
                float knobTarget = on ? S(69f) : S(17f);
                Vector2 position = toggle.Knob.anchoredPosition;
                position.x = Mathf.Lerp(position.x, knobTarget, 1f - Mathf.Exp(-18f * Time.deltaTime));
                toggle.Knob.anchoredPosition = position;

                var track = toggle.Knob.parent.GetComponent<Image>();
                if (track != null)
                {
                    track.color = Color.Lerp(track.color, on ? onColor : trackOffColor,
                                             1f - Mathf.Exp(-18f * Time.deltaTime));
                }

                if (toggle.State != null)
                {
                    toggle.State.text = on ? "ON" : "OFF";
                    toggle.State.color = on ? onColor : offColor;
                }

                if (hover && pressed) clicks.Add(() => { toggle.Write(!toggle.Read()); RefreshToggles(); });
            }

            foreach (var click in clicks)
            {
                try { click.Invoke(); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Steps a setting forward or back along its ladder, wrapping at the ends.</summary>
        private void StepSetting(StepRow step, bool forward)
        {
            float current = step.Read();
            int index = System.Array.IndexOf(step.Steps, current);

            // Snap a value that is not on the ladder to the nearest step above or below, so a
            // preset authored by hand still behaves when clicked.
            if (index < 0)
            {
                index = 0;
                for (int i = 0; i < step.Steps.Length; i++)
                {
                    if (step.Steps[i] <= current) index = i;
                    else break;
                }
                if (forward && index < step.Steps.Length - 1) index++;
            }
            else
            {
                index += forward ? 1 : -1;
                if (index >= step.Steps.Length) index = 0;
                if (index < 0) index = step.Steps.Length - 1;
            }

            step.Write(step.Steps[index]);

            if (step.Value != null) step.Value.text = step.Format(step.Read());

            // The preset's cheat summary line depends on the toggles, not the steps, so the
            // steps only need to refresh their own label.
        }

        /// <summary>Rebuilds the create screen so a toggle's dependants update in one place.</summary>
        private void RefreshToggles()
        {
            if (_screen == LobbyScreen.Create) _pendingRebuild = true;
        }

        private static bool Hit(RectTransform rect, Vector2 pointer)
        {
            return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null);
        }

        // ------------------------------------------------------------------
        // Presets
        // ------------------------------------------------------------------
        /// <summary>Every preset under the search path, so the host offers the full set.</summary>
        private List<MatchPreset> LoadPresets()
        {
            var result = new List<MatchPreset>();

            // The search path is where Tools > Manuel > Create Standard Preset writes them,
            // so a Resources lookup is all that is needed to see them at runtime.
            var found = Resources.LoadAll<MatchPreset>(presetSearchPath);
            if (found != null) result.AddRange(found);

            // Fall back to any preset anywhere, in case one was created outside the folder.
            if (result.Count == 0)
            {
                var fallback = Resources.FindObjectsOfTypeAll<MatchPreset>();
                if (fallback != null) result.AddRange(fallback);
            }

            result.Sort((a, b) => string.CompareOrdinal(a != null ? a.name : string.Empty,
                                                        b != null ? b.name : string.Empty));
            return result;
        }

        // ------------------------------------------------------------------
        // uGUI helpers
        // ------------------------------------------------------------------
        private void AddButton(RectTransform parent, string caption, float y, System.Action onClick,
                               float x = 0f, float width = -1f, bool enabled = true)
        {
            float useWidth = width > 0f ? width : buttonWidth;

            var rect = CreateRect(caption, parent);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(S(useWidth), S(buttonHeight));
            rect.anchoredPosition = new Vector2(x, y);

            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = RoundedSpriteFactory.Small;
            background.type = Image.Type.Sliced;
            background.color = enabled ? buttonColor : new Color(0.15f, 0.16f, 0.18f, 0.5f);

            var labelRect = CreateRect("Label", rect);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = labelRect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = F(buttonFontSize);
            text.text = caption;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = enabled ? textColor : dimColor;
            text.raycastTarget = false;

            _buttons.Add(new Button
            {
                Rect = rect,
                Background = background,
                Label = text,
                OnClick = enabled ? onClick : null
            });
        }

        private void AddRow(string name, string value, System.Action onClick, float y,
                            bool clickable, float x = 0f, float width = 820f)
        {
            var row = CreateRect(name, _panel);
            row.anchorMin = new Vector2(0.5f, 1f);
            row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(S(width), S(72f));
            row.anchoredPosition = new Vector2(x, y);
            var rowImage = row.gameObject.AddComponent<Image>();
            rowImage.sprite = RoundedSpriteFactory.Small;
            rowImage.type = Image.Type.Sliced;
            rowImage.color = clickable ? rowColor : new Color(1f, 1f, 1f, 0.02f);

            // Both labels are placed from the row's own edges rather than its centre, so the
            // same call works at 560 and 820 wide without the two texts colliding.
            AddLabel(row, name, new Vector2(-S(width * 0.5f - 154f), -S(14f)),
                     new Vector2(S(260f), S(44f)),
                     F(bodyFontSize), TextAnchor.MiddleLeft, dimColor);
            AddLabel(row, value, new Vector2(S(width * 0.5f - 144f), -S(14f)),
                     new Vector2(S(240f), S(44f)),
                     F(bodyFontSize), TextAnchor.MiddleRight, textColor);

            _rows.Add(new Row
            {
                Rect = row,
                Background = rowImage,
                Label = null,
                Value = null,
                OnClick = clickable ? onClick : null
            });
        }

        private Text AddLabel(RectTransform parent, string content, Vector2 position, Vector2 size,
                              int fontSize, TextAnchor anchor, Color color)
        {
            var rect = CreateRect("Label", parent);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt, float padding)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        private static Font GetBuiltinFont()
        {
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            return font;
        }
    }
}
