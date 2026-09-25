using System.Collections.Generic;
using ProjectLEA.Manuel.Net;
using ProjectLEA.Manuel.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// The in-match readout: phase and countdown at the top, both players' kills and points
    /// either side, and the winner screen when the match ends.
    ///
    /// Uses the same approach as the shop - a canvas at scale factor 1 with the layout
    /// scaling itself - so text is rasterised at the size it is drawn at and stays sharp.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1200f);

        [SerializeField] private int phaseFontSize = 40;
        [SerializeField] private int timerFontSize = 52;
        [SerializeField] private int scoreFontSize = 26;
        [SerializeField] private int detailFontSize = 20;
        [SerializeField] private int bannerFontSize = 64;
        [SerializeField] private int weaponFontSize = 26;
        [SerializeField] private int promptFontSize = 22;
        [SerializeField] private int abilityFontSize = 22;

        [SerializeField] private Color textColor = new Color(0.95f, 0.97f, 0.99f, 1f);
        [SerializeField] private Color dimColor = new Color(0.66f, 0.70f, 0.75f, 1f);
        [SerializeField] private Color accentColor = new Color(0.20f, 0.78f, 0.62f, 1f);
        [SerializeField] private Color warningColor = new Color(0.95f, 0.55f, 0.25f, 1f);

        [Header("Scoreboard")]
        [Tooltip("Hold this key to bring the scoreboard up. Lets go to hide it again.")]
        [SerializeField] private Key scoreboardKey = Key.Tab;

        [SerializeField] private Color scoreboardPanelColor = new Color(0.05f, 0.06f, 0.09f, 0.98f);
        [SerializeField] private Color localRowColor = new Color(0.20f, 0.78f, 0.62f, 0.18f);
        [SerializeField] private Color remoteRowColor = new Color(1f, 1f, 1f, 0.05f);

        private GameObject _canvasObject;
        private Font _font;

        private Text _phaseLabel;
        private Text _timerLabel;
        private Text _playerOneLabel;
        private Text _playerTwoLabel;
        private Text _playerOneMoney;
        private Text _playerTwoMoney;
        private Text _bannerLabel;
        private Text _weaponLabel;
        private Text _promptLabel;

        // Bottom left, under the ability rows: how much health the local body has left, as a
        // bar plus a number. Without it there is no way to tell a fight is going badly until
        // it is over.
        private Text _healthLabel;
        private RectTransform _healthFill;
        private RectTransform _shieldFill;

        // The local player's health, cached the same way as the weapon user.
        private Health _localHealth;

        // The crosshair: five small plates parented to one transform, so the whole thing can be
        // hidden in one call when the player is not aiming.
        private GameObject _crosshair;

        // Bottom left, one row per class ability: the key, the name, and whether it is ready.
        // Without this there is no way to tell what is castable and when - the cooldown timers
        // exist on ClassAbilityHost but nothing read them.
        private readonly List<Text> _abilityKeyLabels = new List<Text>();
        private readonly List<Text> _abilityNameLabels = new List<Text>();
        private readonly List<Text> _abilityStateLabels = new List<Text>();
        private string _abilitySignature;

        // The local player's weapon user, for the ammo readout. Found once and cached; the
        // player object outlives the HUD's rebuilds but not the other way around.
        private WeaponUser _localWeapons;

        // The scoreboard. Held open with scoreboardKey; rebuilt like everything else.
        private RectTransform _scoreboardRoot;
        private bool _scoreboardVisible;
        private readonly Text[] _sbNames = new Text[2];
        private readonly Text[] _sbKills = new Text[2];
        private readonly Text[] _sbDeaths = new Text[2];
        private readonly Text[] _sbKdr = new Text[2];
        private readonly Text[] _sbPoints = new Text[2];
        private readonly RectTransform[] _sbBars = new RectTransform[2];

        private float _uiScale = 1f;
        private int _builtWidth;
        private int _builtHeight;

        private float S(float v) => v * _uiScale;
        private int F(int v) => Mathf.Max(1, Mathf.RoundToInt(v * _uiScale));

        private void Awake()
        {
            _font = GetBuiltinFont();
            ComputeScale();
            Build();
        }

        private void Update()
        {
            if (Screen.width != _builtWidth || Screen.height != _builtHeight) Rebuild();

            // Hold to view, let go to hide. Toggling the object rather than rebuilding keeps
            // the panel from flashing back in every time the score changes underneath it.
            bool held = IsScoreboardHeld();
            if (held != _scoreboardVisible)
            {
                _scoreboardVisible = held;
                if (_scoreboardRoot != null) _scoreboardRoot.gameObject.SetActive(held);
            }

            Refresh();
        }

        private bool IsScoreboardHeld()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[EffectiveScoreboardKey].isPressed;
        }

        /// <summary>
        /// The player's binding, or the authored default. Read every frame so a rebind in the
        /// settings menu applies without the scoreboard having to be rebuilt.
        /// </summary>
        private Key EffectiveScoreboardKey => KeyBindings.Get("Scoreboard", scoreboardKey);

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
            ComputeScale();
            Build();
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------
        private void Build()
        {
            _canvasObject = new GameObject("MatchHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.transform.SetParent(transform, false);

            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;

            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            var root = (RectTransform)_canvasObject.transform;

            _phaseLabel = MakeLabel(root, "Phase", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                    new Vector2(0f, -S(28f)), new Vector2(S(600f), S(44f)),
                                    F(phaseFontSize), TextAnchor.MiddleCenter, accentColor);

            _timerLabel = MakeLabel(root, "Timer", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                    new Vector2(0f, -S(74f)), new Vector2(S(600f), S(60f)),
                                    F(timerFontSize), TextAnchor.MiddleCenter, textColor);

            _playerOneLabel = MakeLabel(root, "P1", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                        new Vector2(S(40f), -S(28f)), new Vector2(S(500f), S(40f)),
                                        F(scoreFontSize), TextAnchor.UpperLeft, textColor);

            _playerOneMoney = MakeLabel(root, "P1Money", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                        new Vector2(S(40f), -S(68f)), new Vector2(S(500f), S(32f)),
                                        F(detailFontSize), TextAnchor.UpperLeft, dimColor);

            _playerTwoLabel = MakeLabel(root, "P2", new Vector2(1f, 1f), new Vector2(1f, 1f),
                                        new Vector2(-S(40f), -S(28f)), new Vector2(S(500f), S(40f)),
                                        F(scoreFontSize), TextAnchor.UpperRight, textColor);

            _playerTwoMoney = MakeLabel(root, "P2Money", new Vector2(1f, 1f), new Vector2(1f, 1f),
                                        new Vector2(-S(40f), -S(68f)), new Vector2(S(500f), S(32f)),
                                        F(detailFontSize), TextAnchor.UpperRight, dimColor);

            _bannerLabel = MakeLabel(root, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     new Vector2(0f, S(40f)), new Vector2(S(1400f), S(90f)),
                                     F(bannerFontSize), TextAnchor.MiddleCenter, textColor);
            _bannerLabel.text = "";

            // Bottom right: what is in the player's hands and how much it has left.
            _weaponLabel = MakeLabel(root, "Weapon", new Vector2(1f, 0f), new Vector2(1f, 0f),
                                     new Vector2(-S(40f), S(26f)), new Vector2(S(460f), S(40f)),
                                     F(weaponFontSize), TextAnchor.LowerRight, textColor);

            // Bottom centre: what the interaction key would do right now.
            _promptLabel = MakeLabel(root, "Prompt", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                     new Vector2(0f, S(26f)), new Vector2(S(900f), S(34f)),
                                     F(promptFontSize), TextAnchor.MiddleCenter, accentColor);

            // The ability rows are built on demand: no class is picked when the HUD first goes
            // up, and the bar has to follow a re-pick mid-match.
            ClearAbilityRows();

            BuildHealthBar(root);
            BuildCrosshair(root);

            BuildScoreboard(root);
        }

        // ------------------------------------------------------------------
        // Health bar and crosshair
        // ------------------------------------------------------------------
        /// <summary>
        /// A track in the bottom left with a fill that follows the local body's health, a
        /// thinner shield layer over it, and the number itself. Placed under the ability rows
        /// so the corner reads as one panel of "things about me".
        /// </summary>
        private void BuildHealthBar(RectTransform root)
        {
            const float barWidth = 240f;
            const float barHeight = 26f;
            const float x = 40f;
            const float y = 26f;

            var track = MakeRect(root, "HealthBar", new Vector2(0f, 0f), new Vector2(0f, 0f),
                                 new Vector2(S(x), S(y)), new Vector2(S(barWidth), S(barHeight)));
            MakeImage(track, "Track", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, track.sizeDelta, new Color(0f, 0f, 0f, 0.55f), RoundedSpriteFactory.Small);

            // Anchored to the track's left edge and sized as a fraction of it in the refresh,
            // so the bar shrinks toward the side the number is running from.
            var fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(track, false);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0.5f, 0.5f);
            fillRect.sizeDelta = new Vector2(0f, S(barHeight - 6f));
            fillRect.anchoredPosition = new Vector2(S(3f), 0f);
            var fillImage = fillRect.gameObject.AddComponent<Image>();
            fillImage.sprite = RoundedSpriteFactory.Small;
            fillImage.type = Image.Type.Sliced;
            fillImage.color = accentColor;
            fillImage.raycastTarget = false;
            _healthFill = fillRect;

            // Shield renders as a paler layer on top of the same track, wider than the health
            // fill can ever be, so a shield is always readable as an extra bar.
            var shield = new GameObject("Shield", typeof(RectTransform));
            shield.transform.SetParent(track, false);
            var shieldRect = (RectTransform)shield.transform;
            shieldRect.anchorMin = new Vector2(0f, 0.5f);
            shieldRect.anchorMax = new Vector2(0f, 0.5f);
            shieldRect.pivot = new Vector2(0.5f, 0.5f);
            shieldRect.sizeDelta = new Vector2(0f, S(barHeight - 12f));
            shieldRect.anchoredPosition = new Vector2(S(3f), 0f);
            var shieldImage = shieldRect.gameObject.AddComponent<Image>();
            shieldImage.sprite = RoundedSpriteFactory.Small;
            shieldImage.type = Image.Type.Sliced;
            shieldImage.color = new Color(0.55f, 0.75f, 1f, 0.75f);
            shieldImage.raycastTarget = false;
            _shieldFill = shieldRect;

            _healthLabel = MakeLabel(track, "Health", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     Vector2.zero, new Vector2(S(barWidth), S(barHeight)),
                                     F(detailFontSize), TextAnchor.MiddleCenter, textColor);
            _healthLabel.text = "100";
        }

        /// <summary>
        /// Four ticks and a dot around the exact centre. Drawn rather than a sprite because no
        /// crosshair asset exists and the shapes are two rectangles and a square.
        /// </summary>
        private void BuildCrosshair(RectTransform root)
        {
            _crosshair = new GameObject("Crosshair", typeof(RectTransform));
            _crosshair.transform.SetParent(root, false);

            var group = (RectTransform)_crosshair.transform;
            group.anchorMin = new Vector2(0.5f, 0.5f);
            group.anchorMax = new Vector2(0.5f, 0.5f);
            group.pivot = new Vector2(0.5f, 0.5f);
            group.sizeDelta = Vector2.zero;
            group.anchoredPosition = Vector2.zero;

            const float thick = 2f;
            const float len = 9f;
            const float gap = 5f;
            Color color = new Color(0.94f, 0.96f, 0.98f, 0.9f);

            // Dot, then four ticks set out along each axis with a gap in the middle.
            AddCrosshairBit(group, 0f, 0f, thick, thick, color);
            AddCrosshairBit(group, 0f, gap + len * 0.5f, thick, len, color);          // up
            AddCrosshairBit(group, 0f, -(gap + len * 0.5f), thick, len, color);       // down
            AddCrosshairBit(group, gap + len * 0.5f, 0f, len, thick, color);          // right
            AddCrosshairBit(group, -(gap + len * 0.5f), 0f, len, thick, color);       // left
        }

        private static void AddCrosshairBit(RectTransform parent, float x, float y,
                                           float width, float height, Color color)
        {
            var rect = MakeRect(parent, "Tick", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                Vector2.zero, Vector2.zero);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        // ------------------------------------------------------------------
        // Scoreboard
        // ------------------------------------------------------------------
        /// <summary>
        /// The full table: both players' name, kills, deaths, K/D and points, with a bar
        /// showing how close each is to the win. Hidden until the key is held.
        ///
        /// Column x values are measured from the LEFT edge of a 920-wide row plate, because
        /// the labels are anchored to it rather than to the panel.
        /// </summary>
        private void BuildScoreboard(RectTransform root)
        {
            const float panelWidth = 960f;
            const float panelHeight = 380f;
            const float plateWidth = 920f;

            _scoreboardRoot = MakeRect(root, "Scoreboard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                       Vector2.zero, new Vector2(S(panelWidth), S(panelHeight)));

            MakeImage(_scoreboardRoot, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, _scoreboardRoot.sizeDelta, scoreboardPanelColor, RoundedSpriteFactory.Rounded);

            MakeLabel(_scoreboardRoot, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      new Vector2(0f, -S(24f)), new Vector2(S(panelWidth), S(44f)),
                      F(phaseFontSize), TextAnchor.MiddleCenter, accentColor).text = "SCOREBOARD";

            // Column headers, aligned with the numbers below them.
            float headerY = -S(86f);
            AddScoreboardColumn(_scoreboardRoot, "NameHeader", headerY, 40f, 360f, "PLAYER", dimColor, TextAnchor.MiddleLeft);
            AddScoreboardColumn(_scoreboardRoot, "KillsHeader", headerY, 450f, 100f, "KILLS", dimColor, TextAnchor.MiddleCenter);
            AddScoreboardColumn(_scoreboardRoot, "DeathsHeader", headerY, 580f, 100f, "DEATHS", dimColor, TextAnchor.MiddleCenter);
            AddScoreboardColumn(_scoreboardRoot, "KdrHeader", headerY, 710f, 100f, "K/D", dimColor, TextAnchor.MiddleCenter);
            AddScoreboardColumn(_scoreboardRoot, "PointsHeader", headerY, 810f, 100f, "ROUNDS", dimColor, TextAnchor.MiddleCenter);

            AddScoreboardRow(PlayerSlot.One, -S(134f), plateWidth, localRowColor);
            AddScoreboardRow(PlayerSlot.Two, -S(208f), plateWidth, remoteRowColor);

            MakeLabel(_scoreboardRoot, "Hint", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      new Vector2(0f, -S(310f)), new Vector2(S(panelWidth), S(28f)),
                      F(detailFontSize), TextAnchor.MiddleCenter, dimColor)
                .text = $"Hold {EffectiveScoreboardKey} to keep this open";

            // Starts hidden; Update raises it while the key is held.
            _scoreboardRoot.gameObject.SetActive(false);
        }

        /// <summary>One player's row: a tinted plate carrying the five columns and a bar.</summary>
        private void AddScoreboardRow(PlayerSlot slot, float y, float plateWidth, Color plateColor)
        {
            int i = (int)slot;

            var plate = MakeRect(_scoreboardRoot, $"Row{slot}", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 new Vector2(0f, y), new Vector2(S(plateWidth), S(62f)));
            MakeImage(plate, "Plate", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, plate.sizeDelta, plateColor, RoundedSpriteFactory.Small);

            // Points progress, a thin bar along the bottom of the plate. Anchored to the
            // plate's left edge and growing rightward, so its width in the refresh is a
            // fraction of the plate's width and nothing else has to be recomputed.
            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(plate, false);

            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 0f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(0f, S(6f));
            barRect.anchoredPosition = new Vector2(S(10f), S(6f));

            var barImage = barRect.gameObject.AddComponent<Image>();
            barImage.sprite = RoundedSpriteFactory.Small;
            barImage.type = Image.Type.Sliced;
            barImage.color = accentColor;
            barImage.raycastTarget = false;
            barRect.gameObject.SetActive(false);
            _sbBars[i] = barRect;

            // Anchored to the plate's left edge, so x is measured from there.
            Vector2 leftAnchor = new Vector2(0f, 1f);
            _sbNames[i] = AddScoreboardColumn(plate, "Name", -S(8f), 40f, 360f, string.Empty, textColor, TextAnchor.MiddleLeft);
            _sbKills[i] = AddScoreboardColumn(plate, "Kills", -S(8f), 450f, 100f, string.Empty, textColor, TextAnchor.MiddleCenter);
            _sbDeaths[i] = AddScoreboardColumn(plate, "Deaths", -S(8f), 580f, 100f, string.Empty, textColor, TextAnchor.MiddleCenter);
            _sbKdr[i] = AddScoreboardColumn(plate, "Kdr", -S(8f), 710f, 100f, string.Empty, textColor, TextAnchor.MiddleCenter);
            _sbPoints[i] = AddScoreboardColumn(plate, "Points", -S(8f), 810f, 100f, string.Empty, accentColor, TextAnchor.MiddleCenter);

            // The local player's name carries a marker, so you can find yourself at a glance.
            _sbNames[i].text = slot == PlayerSlot.One ? "YOU" : "OPPONENT";
        }

        /// <summary>One cell of the scoreboard, at the given x within its parent's width.</summary>
        private Text AddScoreboardColumn(RectTransform parent, string name, float y, float x, float width,
                                         string content, Color color, TextAnchor anchor)
        {
            var label = MakeLabel(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                                  new Vector2(S(x), y), new Vector2(S(width), S(46f)),
                                  F(scoreFontSize), anchor, color);
            label.text = content;
            return label;
        }

        /// <summary>Fills the table from the score manager. Called every frame while held.</summary>
        private void RefreshScoreboard()
        {
            if (!ScoreManager.Exists) return;

            var score = ScoreManager.Instance;

            for (int i = 0; i < 2; i++)
            {
                var slot = (PlayerSlot)i;

                SetIfChanged(_sbNames[i], DisplayName(slot));
                SetIfChanged(_sbKills[i], score.GetKills(slot).ToString());
                SetIfChanged(_sbDeaths[i], score.GetDeaths(slot).ToString());
                SetIfChanged(_sbKdr[i], score.GetKdr(slot).ToString("F1"));
                SetIfChanged(_sbPoints[i], $"{score.GetRoundsWon(slot)} / {score.RoundsToWin}");

                // The bar fills with rounds toward the win, so who is ahead is visible from
                // across the room rather than by reading the numbers.
                float t = score.RoundsToWin > 0
                          ? Mathf.Clamp01((float)score.GetRoundsWon(slot) / score.RoundsToWin)
                          : 0f;

                var bar = _sbBars[i];
                if (bar != null)
                {
                    float full = S(920f) - S(20f);
                    Vector2 size = bar.sizeDelta;
                    size.x = Mathf.Max(S(6f), full * t);
                    bar.sizeDelta = size;
                    bar.gameObject.SetActive(t > 0f);
                }
            }
        }

        /// <summary>
        /// Slot One is always this machine's player. Online, both names come from the session;
        /// offline slot Two is the training dummy and there is nobody to name it after.
        /// </summary>
        private static string DisplayName(PlayerSlot slot)
        {
            bool online = LobbyNetwork.IsActive;

            if (slot == PlayerSlot.One)
            {
                string local = online ? LobbyNetwork.Instance.LocalPlayerName : null;
                return !string.IsNullOrEmpty(local) ? local : "Player 1";
            }

            string remote = online ? LobbyNetwork.Instance.RemotePlayerName : null;
            return !string.IsNullOrEmpty(remote) ? remote : "Player 2";
        }

        /// <summary>A plain RectTransform child, positioned by anchor and pivot like the labels.</summary>
        private static RectTransform MakeRect(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                              Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x, anchorMax.y);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static Image MakeImage(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                       Vector2 position, Vector2 size, Color color, Sprite sprite)
        {
            var rect = MakeRect(parent, name, anchorMin, anchorMax, position, size);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text MakeLabel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                               Vector2 position, Vector2 size, int fontSize, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x, anchorMax.y);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = "";
            return text;
        }

        // ------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------
        private void Refresh()
        {
            if (!GameManager.Exists)
            {
                _phaseLabel.text = "No GameManager";
                return;
            }

            var game = GameManager.Instance;
            var state = game.State;

            SetIfChanged(_phaseLabel, PhaseName(state));

            // Idle and MatchOver have no clock. The banner already names the winner, and the
            // match is started from the lobby now - there is no key to press here.
            if (state == MatchState.Idle || state == MatchState.MatchOver)
                SetIfChanged(_timerLabel, string.Empty);
            else
                SetIfChanged(_timerLabel, FormatTime(game.TimeRemaining));

            _timerLabel.color = state == MatchState.RoundActive && game.TimeRemaining < 30f
                ? warningColor
                : textColor;

            RefreshPlayer(PlayerSlot.One, _playerOneLabel, _playerOneMoney);
            RefreshPlayer(PlayerSlot.Two, _playerTwoLabel, _playerTwoMoney);
            RefreshBanner(state, game);
            RefreshWeapon();
            RefreshPrompt();
            RefreshAbilities();
            RefreshHealth();
            RefreshCrosshair(state);

            if (_scoreboardVisible) RefreshScoreboard();
        }

        private void RefreshPlayer(PlayerSlot slot, Text scoreLabel, Text moneyLabel)
        {
            int number = (int)slot + 1;

            if (!ScoreManager.Exists)
            {
                SetIfChanged(scoreLabel, $"P{number}");
                SetIfChanged(moneyLabel, "");
                return;
            }

            var score = ScoreManager.Instance;

            SetIfChanged(scoreLabel, $"P{number}   {score.GetRoundsWon(slot)} rounds");

            int money = EconomyManager.Exists ? EconomyManager.Instance.GetMoney(slot) : 0;
            int streak = score.GetLossStreak(slot);
            SetIfChanged(moneyLabel, $"{money} cr" + (streak > 0 ? $"   -{streak} streak" : ""));
        }

        /// <summary>
        /// The mid-screen message. It names how the round was decided, which is the part the
        /// phase name alone cannot tell you - "ROUND OVER" does not say whether the clock ran
        /// out or somebody got shot.
        /// </summary>
        private void RefreshBanner(MatchState state, GameManager game)
        {
            if (state == MatchState.MatchOver && ScoreManager.Exists && ScoreManager.Instance.HasWinner)
            {
                var winner = ScoreManager.Instance.Winner;
                SetIfChanged(_bannerLabel, $"PLAYER {(int)winner + 1} WINS");
                _bannerLabel.color = accentColor;
                return;
            }

            if (state == MatchState.RoundOver)
            {
                SetIfChanged(_bannerLabel, RoundOverMessage(game));
                _bannerLabel.color = warningColor;
                return;
            }

            SetIfChanged(_bannerLabel, "");
        }

        /// <summary>Who won the round that just ended, and what they won it with.</summary>
        private static string RoundOverMessage(GameManager game)
        {
            int winner = (int)game.LastRoundWinner + 1;

            switch (game.LastRoundReason)
            {
                case RoundEndReason.Elimination:
                    return $"PLAYER {winner} WINS THE ROUND - ELIMINATION";
                case RoundEndReason.TimeExpired:
                    return $"PLAYER {winner} WINS THE ROUND - TIME EXPIRED";
                case RoundEndReason.Draw:
                    return "ROUND DRAWN - DEAD TIE ON HEALTH, THE ROUND IS REPLAYED";
                default:
                    return $"PLAYER {winner} WINS THE ROUND";
            }
        }

        /// <summary>
        /// The equipped weapon and its ammo, bottom right. Without this there is no way to
        /// tell a Vandal from a knife until you pull the trigger, and no way to know a reload
        /// is halfway through.
        /// </summary>
        private void RefreshWeapon()
        {
            if (!WeaponManager.Exists || WeaponManager.Instance.Equipped == null)
            {
                SetIfChanged(_weaponLabel, "");
                return;
            }

            var weapon = WeaponManager.Instance.Equipped;
            string name = weapon.displayName.ToUpperInvariant();

            if (!weapon.UsesAmmo)
            {
                SetIfChanged(_weaponLabel, name);
                return;
            }

            var user = LocalWeapons;
            if (user == null)
            {
                SetIfChanged(_weaponLabel, $"{name}  {weapon.magazineSize}/{weapon.reserveAmmo}");
                return;
            }

            // A reload in progress shows how far along it is, so the readout explains the
            // silence instead of looking frozen on an empty magazine.
            string ammo = user.IsReloading
                ? $"RELOADING {user.ReloadProgress * 100f:0}%"
                : $"{user.Magazine} / {user.Reserve}";

            SetIfChanged(_weaponLabel, $"{name}   {ammo}");
        }

        /// <summary>
        /// Bottom centre. The shop's key lives on the shop itself, but the shop is the only
        /// thing in the match with a context key, so the prompt is how a new player finds out
        /// it exists - and it only shows while the shop can actually be opened.
        /// </summary>
        private void RefreshPrompt()
        {
            string prompt = string.Empty;

            if (LoadoutUI.ExistsOnScene && LoadoutUI.Instance.CanOpen)
            {
                prompt = $"Press {LoadoutUI.Instance.ToggleKeyName} to open the shop";
            }

            SetIfChanged(_promptLabel, prompt);
        }

        /// <summary>
        /// The local body's health, as a bar and a number. The bar is a fraction of the track
        /// and the shield sits on top of it, so a shield reads as an extra layer rather than as
        /// health the player does not actually have.
        /// </summary>
        private void RefreshHealth()
        {
            var health = LocalHealth;

            if (health == null)
            {
                if (_healthFill != null) _healthFill.gameObject.SetActive(false);
                if (_shieldFill != null) _shieldFill.gameObject.SetActive(false);
                SetIfChanged(_healthLabel, string.Empty);
                return;
            }

            float max = Mathf.Max(1f, health.Max);

            if (_healthFill != null)
            {
                float width = S(240f) * Mathf.Clamp01(health.Current / max) - S(6f);
                Vector2 size = _healthFill.sizeDelta;
                size.x = Mathf.Max(0f, width);
                _healthFill.sizeDelta = size;
                _healthFill.gameObject.SetActive(health.Current > 0f);

                // Healthy is the accent green; badly hurt turns the bar the colour of danger so
                // the player's own state is legible without reading the number.
                float t = health.Current / max;
                _healthFill.GetComponent<Image>().color = t > 0.5f
                    ? accentColor
                    : Color.Lerp(warningColor, Color.red, 1f - t / 0.5f);
            }

            if (_shieldFill != null)
            {
                float width = S(240f) * Mathf.Clamp01(health.Shield / max) - S(6f);
                Vector2 size = _shieldFill.sizeDelta;
                size.x = Mathf.Max(0f, width);
                _shieldFill.sizeDelta = size;
                _shieldFill.gameObject.SetActive(health.HasShield);
            }

            SetIfChanged(_healthLabel, health.IsDead ? "DOWN" : Mathf.RoundToInt(health.Current).ToString());
        }

        /// <summary>
        /// The crosshair only belongs in a live round. The shop and the roster cover the screen
        /// anyway, and it makes no sense to offer an aim point in a phase where nobody shoots.
        /// </summary>
        private void RefreshCrosshair(MatchState state)
        {
            if (_crosshair == null) return;

            bool aiming = state == MatchState.RoundActive;
            if (_crosshair.activeSelf != aiming) _crosshair.SetActive(aiming);
        }

        /// <summary>Looks up the local player's health once and keeps it.</summary>
        private Health LocalHealth
        {
            get
            {
                if (_localHealth == null)
                {
                    var player = GameObject.Find("Player");
                    if (player != null) _localHealth = player.GetComponent<Health>();
                }
                return _localHealth;
            }
        }

        // ------------------------------------------------------------------
        // Ability bar
        // ------------------------------------------------------------------
        /// <summary>
        /// Bottom left, one row per class ability: its key, its name, and whether it is ready
        /// or how many seconds are left. This is the answer to "how do I know what I can use
        /// when" - the timers were already tracked on ClassAbilityHost, but nothing in the
        /// match read them, so every ability looked permanently available or permanently dead.
        /// </summary>
        private void RefreshAbilities()
        {
            var host = ClassAbilityHost.Exists ? ClassAbilityHost.Instance : null;
            var defs = host != null ? host.Definitions : null;

            // Rebuild the rows only when the class or its ability count changes - the thing
            // that moves every frame is the cooldown number, and that updates in place.
            string signature = host != null && host.CurrentClass != null && defs != null
                ? host.CurrentClass.displayName + "|" + defs.Count
                : string.Empty;

            if (signature != _abilitySignature)
            {
                _abilitySignature = signature;
                RebuildAbilityRows(host, defs);
            }

            if (defs == null) return;

            for (int i = 0; i < defs.Count && i < _abilityStateLabels.Count; i++)
            {
                if (defs[i] == null) continue;

                float remaining = host.GetCooldownRemaining(i);

                // READY is the accent colour so a castable ability is the brightest thing in
                // the corner; a cooling one dims out and counts down.
                SetIfChanged(_abilityStateLabels[i],
                             remaining > 0f ? $"{remaining:F1}s" : "READY");
                _abilityStateLabels[i].color = remaining > 0f ? dimColor : accentColor;
            }
        }

        /// <summary>Recreates the row objects. Called on a class pick, a re-pick and a rebuild.</summary>
        private void RebuildAbilityRows(ClassAbilityHost host, IReadOnlyList<AbilityDefinition> defs)
        {
            ClearAbilityRows();

            if (_canvasObject == null || defs == null || defs.Count == 0) return;

            var root = (RectTransform)_canvasObject.transform;

            const float rowHeight = 34f;
            const float keyWidth = 62f;
            const float nameWidth = 280f;

            for (int i = 0; i < defs.Count; i++)
            {
                var ability = defs[i];
                if (ability == null) continue;

                // Stacked up from the bottom left, above the health bar and clear of the
                // scoreboard key prompt and the weapon readout on the right.
                float y = S(70f) + S(rowHeight) * i;
                var anchor = new Vector2(0f, 0f);

                string key = host != null && host.CurrentClass != null
                    ? KeyLabel(KeyBindings.GetAbilityKey(host.CurrentClass, ability.activationKey))
                    : KeyLabel(ability.activationKey);

                var keyLabel = MakeLabel(root, $"AbilityKey{i}", anchor, anchor,
                                         new Vector2(S(40f), y), new Vector2(S(keyWidth), S(rowHeight)),
                                         F(abilityFontSize), TextAnchor.UpperLeft, accentColor);
                keyLabel.text = $"[{key}]";
                _abilityKeyLabels.Add(keyLabel);

                var nameLabel = MakeLabel(root, $"AbilityName{i}", anchor, anchor,
                                          new Vector2(S(40f + keyWidth), y),
                                          new Vector2(S(nameWidth), S(rowHeight)),
                                          F(abilityFontSize), TextAnchor.UpperLeft, textColor);
                nameLabel.text = ability.displayName;
                _abilityNameLabels.Add(nameLabel);

                var stateLabel = MakeLabel(root, $"AbilityState{i}", anchor, anchor,
                                           new Vector2(S(40f + keyWidth + nameWidth), y),
                                           new Vector2(S(110f), S(rowHeight)),
                                           F(abilityFontSize), TextAnchor.UpperLeft, dimColor);
                stateLabel.text = "READY";
                _abilityStateLabels.Add(stateLabel);
            }
        }

        private void ClearAbilityRows()
        {
            foreach (var label in _abilityKeyLabels) if (label != null) Destroy(label.gameObject);
            foreach (var label in _abilityNameLabels) if (label != null) Destroy(label.gameObject);
            foreach (var label in _abilityStateLabels) if (label != null) Destroy(label.gameObject);

            _abilityKeyLabels.Clear();
            _abilityNameLabels.Clear();
            _abilityStateLabels.Clear();
        }

        /// <summary>The engine's Key enum spells letters as their index; A..Z is 15..40.</summary>
        private static string KeyLabel(Key key)
        {
            int value = (int)key;
            if (value >= 15 && value <= 40)
                return ((char)('A' + value - 15)).ToString();

            return key.ToString().ToUpperInvariant();
        }

        /// <summary>Looks up the local player's weapon user once and keeps it.</summary>
        private WeaponUser LocalWeapons
        {
            get
            {
                if (_localWeapons == null)
                {
                    var player = GameObject.Find("Player");
                    if (player != null) _localWeapons = player.GetComponent<WeaponUser>();
                }
                return _localWeapons;
            }
        }

        private static string PhaseName(MatchState state)
        {
            switch (state)
            {
                case MatchState.Idle: return "WAITING";
                case MatchState.ClassSelect: return "CLASS SELECT";
                case MatchState.BuyPhase: return "BUY PHASE";
                case MatchState.RoundActive: return "ROUND";
                case MatchState.RoundOver: return "ROUND OVER";
                case MatchState.MatchOver: return "MATCH OVER";
                default: return "";
            }
        }

        /// <summary>
        /// Only touches the label when the string actually changed. Assigning Text.text
        /// every frame rebuilds the UI mesh every frame, which is a real cost for no gain.
        /// </summary>
        private static void SetIfChanged(Text label, string value)
        {
            if (label == null) return;
            if (label.text == value) return;

            label.text = value;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(seconds);
            return $"{total / 60:0}:{total % 60:00}";
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
