using System.Collections.Generic;
using ProjectLEA.Manuel.Net;
using ProjectLEA.Manuel.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// The pre-match class select screen: every class in the roster, grouped into columns by
    /// role, and a LOCK IN that commits the choice for the whole match.
    ///
    /// It opens the moment the match enters the <see cref="MatchState.ClassSelect"/> phase and
    /// closes the moment the phase moves on, so it is driven by the same phase machine as
    /// everything else - the host owns that machine, and a client opens and closes on the
    /// states it is handed, never on a local timer of its own.
    ///
    /// Both players pick at once. A click points at a class (which the other player sees, so
    /// the two chips read "Kestrel" and "Volley" while the pick is still being made); LOCK IN
    /// commits it. Whoever has not locked when the timer runs out is assigned the class they
    /// were looking at, or a random one if they never looked at all.
    ///
    /// Like the shop, this is drawn entirely in code and hit-tested by hand: no EventSystem
    /// exists anywhere in this project, and keeping it that way is worth a few extra helpers.
    /// </summary>
    public class ClassSelectUI : MonoBehaviour
    {
        [Header("Layout - authored at the reference resolution below")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1200f);

        [SerializeField] private float columnWidth = 430f;
        [SerializeField] private float columnGap = 14f;
        [SerializeField] private float cardHeight = 62f;
        [SerializeField] private float cardGap = 8f;
        [SerializeField] private float gridTop = -196f;
        [SerializeField] private float detailTop = -806f;
        [SerializeField] private float detailHeight = 254f;
        [SerializeField] private float sideMargin = 80f;

        [Header("Font sizes - in reference pixels")]
        [SerializeField] private int titleFontSize = 46;
        [SerializeField] private int timerFontSize = 32;
        [SerializeField] private int chipNameFontSize = 22;
        [SerializeField] private int chipStateFontSize = 18;
        [SerializeField] private int headingFontSize = 22;
        [SerializeField] private int cardNameFontSize = 20;
        [SerializeField] private int cardKeysFontSize = 16;
        [SerializeField] private int detailNameFontSize = 30;
        [SerializeField] private int detailLineFontSize = 17;
        [SerializeField] private int statusFontSize = 19;
        [SerializeField] private int buttonFontSize = 26;

        [Header("Colours")]
        [SerializeField] private Color backdropColor = new Color(0.03f, 0.04f, 0.06f, 0.97f);
        [SerializeField] private Color plateColor = new Color(0.07f, 0.08f, 0.11f, 0.98f);
        [SerializeField] private Color cardColor = new Color(1f, 1f, 1f, 0.05f);
        [SerializeField] private Color cardHoverColor = new Color(1f, 1f, 1f, 0.14f);
        [SerializeField] private Color cardPickedColor = new Color(0.20f, 0.55f, 0.90f, 0.45f);
        [SerializeField] private Color textColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        [SerializeField] private Color dimColor = new Color(0.64f, 0.68f, 0.74f, 1f);
        [SerializeField] private Color hintColor = new Color(0.55f, 0.59f, 0.65f, 1f);
        [SerializeField] private Color accentColor = new Color(0.20f, 0.78f, 0.62f, 1f);
        [SerializeField] private Color accentDisabledColor = new Color(0.35f, 0.38f, 0.42f, 0.55f);
        [SerializeField] private Color ruleColor = new Color(1f, 1f, 1f, 0.10f);

        [Tooltip("Colours the four role columns are tinted, matching Valorant's role palette.")]
        [SerializeField] private Color duelistColor = new Color(0.88f, 0.36f, 0.30f, 1f);
        [SerializeField] private Color initiatorColor = new Color(0.86f, 0.70f, 0.26f, 1f);
        [SerializeField] private Color controllerColor = new Color(0.36f, 0.56f, 0.86f, 1f);
        [SerializeField] private Color sentinelColor = new Color(0.36f, 0.76f, 0.46f, 1f);

        /// <summary>One card in the grid.</summary>
        private class Card
        {
            public RectTransform Rect;
            public Image Background;
            public Text Name;
            public Text Keys;
            public int Index;
        }

        private readonly List<Card> _cards = new List<Card>();

        // The roster split four ways, rebuilt whenever the roster itself changes.
        private readonly List<int> _byRole = new List<int>();

        private GameObject _canvasObject;
        private RectTransform _backdrop;
        private Text _title;
        private Text _timer;

        private Text _localChipName;
        private Text _localChipState;
        private Text _remoteChipName;
        private Text _remoteChipState;

        private RectTransform _detailPlate;
        private Text _detailName;
        private Text _detailRole;
        private Text _detailTraits;
        private readonly Text[] _detailAbilities = new Text[4];

        private RectTransform _lockRect;
        private Image _lockBackground;
        private Text _lockCaption;
        private bool _lockInteractable;

        private Text _status;

        private Font _font;

        private bool _isOpen;
        private int _focusedIndex = -1;

        // The last pick we told the other machine about, so a sweep across the grid costs one
        // packet per card rather than one per frame.
        private int _lastSentPick = -2;

        private CursorLockMode _previousLockMode;
        private bool _previousCursorVisible;

        private ProjectLEA.Manuel.PlayerController _playerController;

        private float _uiScale = 1f;
        private int _builtWidth;
        private int _builtHeight;

        /// <summary>
        /// Whether the class select screen is up anywhere. Ability casts stand down while it is,
        /// the same way they do for the shop.
        /// </summary>
        public static bool IsOpenNow { get; private set; }

        private float S(float value) => value * _uiScale;
        private int F(int value) => Mathf.Max(1, Mathf.RoundToInt(value * _uiScale));

        private void Awake()
        {
            _font = GetBuiltinFont();
            ComputeScale();
            Build();
            SetOpen(false, instant: true);
        }

        private void OnDisable()
        {
            if (_isOpen) SetOpen(false);
        }

        private void Update()
        {
            if (Screen.width != _builtWidth || Screen.height != _builtHeight) Rebuild();

            SyncWithPhase();
            if (!_isOpen) return;

            HandlePointer();
            Refresh();
        }

        // ------------------------------------------------------------------
        // Open / close
        // ------------------------------------------------------------------
        private void SyncWithPhase()
        {
            bool want = GameManager.Exists && GameManager.Instance.State == MatchState.ClassSelect;
            if (want == _isOpen) return;

            SetOpen(want);
        }

        private void SetOpen(bool open, bool instant = false)
        {
            if (_isOpen == open && !instant) return;

            _isOpen = open;
            IsOpenNow = open;

            if (_canvasObject != null) _canvasObject.SetActive(open);

            if (open)
            {
                _previousLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // Freeze the match while the roster is up. The phase clock uses unscaled time,
                // so the select countdown keeps running underneath this.
                MatchUiPause.Push();

                SetPlayerInput(false);

                // A returning player should not still be pointing at the class they had last
                // match, and the opponent should not hear about a stale pick.
                _focusedIndex = -1;
                _lastSentPick = -2;
            }
            else if (!instant)
            {
                Cursor.lockState = _previousLockMode;
                Cursor.visible = _previousCursorVisible;

                MatchUiPause.Pop();

                SetPlayerInput(true);
            }
        }

        private void SetPlayerInput(bool enabled)
        {
            if (_playerController == null)
            {
                var player = GameObject.Find("Player");
                if (player != null) _playerController = player.GetComponent<ProjectLEA.Manuel.PlayerController>();
            }

            if (_playerController != null) _playerController.InputEnabled = enabled;
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------
        private void HandlePointer()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pointer = mouse.position.ReadValue();
            bool pressed = mouse.leftButton.wasPressedThisFrame;

            bool localLocked = LocalLocked;

            // Hovering previews a card in the detail plate. A locked player has committed, so
            // the preview follows their pick instead of wherever the cursor is now.
            if (localLocked)
            {
                _focusedIndex = LocalPick;
            }
            else
            {
                _focusedIndex = -1;

                foreach (var card in _cards)
                {
                    if (card.Rect == null) continue;
                    if (Hit(card.Rect, pointer)) _focusedIndex = card.Index;
                }
            }

            if (pressed)
            {
                if (_lockRect != null && _lockInteractable && Hit(_lockRect, pointer))
                {
                    LockIn();
                    return;
                }

                if (!localLocked)
                {
                    foreach (var card in _cards)
                    {
                        if (card.Rect == null) continue;
                        if (Hit(card.Rect, pointer)) { PickClass(card.Index); return; }
                    }
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && !localLocked && LocalPick >= 0
                && (keyboard[Key.Enter].wasPressedThisFrame || keyboard[Key.NumpadEnter].wasPressedThisFrame))
            {
                LockIn();
            }
        }

        /// <summary>Points at a class without committing to it.</summary>
        private void PickClass(int index)
        {
            if (LocalLocked) return;

            _focusedIndex = index;

            // Recorded centrally, so a timer-out assigns the class the player was actually
            // looking at rather than one at random.
            if (GameManager.Exists) GameManager.Instance.HoverClass(PlayerSlot.One, index);

            if (LobbyNetwork.IsActive && index != _lastSentPick)
            {
                _lastSentPick = index;
                LobbyNetwork.Instance.SendClassLock(index, locked: false);
            }
        }

        /// <summary>Commits the current pick for the rest of the match.</summary>
        private void LockIn()
        {
            if (LocalLocked) return;

            int index = LocalPick;
            if (index < 0) return;

            if (GameManager.Exists) GameManager.Instance.LockClass(PlayerSlot.One, index);

            // The host's own advance is decided locally; a client sends its lock so the host
            // knows both sides are in and can open the buy phase.
            if (LobbyNetwork.IsActive) LobbyNetwork.Instance.SendClassLock(index, locked: true);
        }

        private int LocalPick => GameManager.Exists ? GameManager.Instance.GetClassPick(PlayerSlot.One) : -1;

        private bool LocalLocked => GameManager.Exists && GameManager.Instance.IsClassLocked(PlayerSlot.One);

        // ------------------------------------------------------------------
        // Construction
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

            _cards.Clear();

            ComputeScale();
            Build();

            // Restore visibility directly rather than through SetOpen: SetOpen would capture
            // the cursor and time-scale state again, and while the screen is up those are
            // already overridden - so closing would restore the overrides and freeze the game.
            if (_canvasObject != null) _canvasObject.SetActive(_isOpen);
        }

        /// <summary>Total width of the four columns, so the grid can be centred.</summary>
        private float GridWidth => 4f * S(columnWidth) + 3f * S(columnGap);

        private void Build()
        {
            _canvasObject = new GameObject("ClassSelectCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.transform.SetParent(transform, false);

            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD, below nothing - this is the whole screen while it is up.
            canvas.sortingOrder = 210;

            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            var root = (RectTransform)_canvasObject.transform;

            _backdrop = CreateRect("Backdrop", root);
            Stretch(_backdrop, 0f);
            var backdropImage = _backdrop.gameObject.AddComponent<Image>();
            backdropImage.sprite = RoundedSpriteFactory.Gradient;
            backdropImage.type = Image.Type.Simple;
            backdropImage.color = backdropColor;

            BuildHeader(root);
            BuildChips(root);
            BuildGrid(root);
            BuildDetail(root);
            BuildLock(root);
        }

        private void BuildHeader(RectTransform root)
        {
            _title = AddLabel(root, "SELECT YOUR CLASS", new Vector2(0f, -S(28f)),
                              new Vector2(S(900f), S(54f)), F(titleFontSize),
                              TextAnchor.MiddleCenter, accentColor);

            _timer = AddLabel(root, "", new Vector2(0f, -S(88f)),
                              new Vector2(S(900f), S(38f)), F(timerFontSize),
                              TextAnchor.MiddleCenter, textColor);
        }

        /// <summary>The two players' names and what each of them is on. Sits above the grid.</summary>
        private void BuildChips(RectTransform root)
        {
            float chipWidth = S(560f);

            var local = CreateRect("LocalChip", root);
            local.anchorMin = new Vector2(0f, 1f);
            local.anchorMax = new Vector2(0f, 1f);
            local.pivot = new Vector2(0f, 1f);
            local.sizeDelta = new Vector2(chipWidth, S(78f));
            local.anchoredPosition = new Vector2(S(sideMargin), -S(28f));
            local.gameObject.AddComponent<Image>().color = plateColor;

            _localChipName = AddLabel(local, "YOU", new Vector2(S(20f), -S(10f)),
                                      new Vector2(chipWidth - S(40f), S(30f)), F(chipNameFontSize),
                                      TextAnchor.UpperLeft, textColor);
            _localChipState = AddLabel(local, "BROWSING...", new Vector2(S(20f), -S(42f)),
                                       new Vector2(chipWidth - S(40f), S(26f)), F(chipStateFontSize),
                                       TextAnchor.UpperLeft, dimColor);

            var remote = CreateRect("RemoteChip", root);
            remote.anchorMin = new Vector2(1f, 1f);
            remote.anchorMax = new Vector2(1f, 1f);
            remote.pivot = new Vector2(1f, 1f);
            remote.sizeDelta = new Vector2(chipWidth, S(78f));
            remote.anchoredPosition = new Vector2(-S(sideMargin), -S(28f));
            remote.gameObject.AddComponent<Image>().color = plateColor;

            _remoteChipName = AddLabel(remote, "OPPONENT", new Vector2(-S(20f), -S(10f)),
                                       new Vector2(chipWidth - S(40f), S(30f)), F(chipNameFontSize),
                                       TextAnchor.UpperRight, textColor);
            _remoteChipState = AddLabel(remote, "BROWSING...", new Vector2(-S(20f), -S(42f)),
                                        new Vector2(chipWidth - S(40f), S(26f)), F(chipStateFontSize),
                                        TextAnchor.UpperRight, dimColor);
        }

        /// <summary>Four columns, one per role, each holding that role's classes as cards.</summary>
        private void BuildGrid(RectTransform root)
        {
            // The tallest column decides the card height for all of them, so the four stay the
            // same size. Without this, adding a class to the roster would push the longest
            // column straight through the detail plate and off the bottom of the screen.
            int tallest = 0;
            for (int role = 0; role < 4; role++)
            {
                tallest = Mathf.Max(tallest, IndicesForRole((ClassRole)role).Count);
            }

            float cardHeight = FittedCardHeight(tallest);

            float left = -GridWidth * 0.5f + S(columnWidth) * 0.5f;

            for (int role = 0; role < 4; role++)
            {
                float x = left + role * (S(columnWidth) + S(columnGap));

                AddLabel(root, RoleName((ClassRole)role), new Vector2(x, S(gridTop)),
                         new Vector2(S(columnWidth), S(34f)), F(headingFontSize),
                         TextAnchor.MiddleCenter, RoleColor((ClassRole)role));

                // A hairline under the heading, in the role's own colour, so a column reads as
                // a group at a glance rather than a list of names.
                var rule = CreateRect("RoleRule", root);
                rule.anchorMin = new Vector2(0.5f, 1f);
                rule.anchorMax = new Vector2(0.5f, 1f);
                rule.pivot = new Vector2(0.5f, 1f);
                rule.sizeDelta = new Vector2(S(columnWidth), S(2f));
                rule.anchoredPosition = new Vector2(x, S(gridTop) - S(36f));
                var ruleImage = rule.gameObject.AddComponent<Image>();
                ruleImage.sprite = RoundedSpriteFactory.Small;
                ruleImage.type = Image.Type.Sliced;
                ruleImage.color = new Color(RoleColor((ClassRole)role).r,
                                            RoleColor((ClassRole)role).g,
                                            RoleColor((ClassRole)role).b, 0.45f);

                // The cards hang from a hairline below the heading rather than from the heading
                // itself, so the two never overlap when the roster grows.
                float y = S(gridTop) - S(46f);

                foreach (var index in IndicesForRole((ClassRole)role))
                {
                    CreateCard(root, index, x, y, cardHeight);
                    y -= cardHeight + S(cardGap);
                }
            }
        }

        /// <summary>
        /// The card height that keeps the tallest column between its heading and the detail
        /// plate. The authored height is used while it fits; past that the cards shrink, so a
        /// growing roster compresses the grid instead of spilling off the bottom of the screen.
        /// </summary>
        private float FittedCardHeight(int cardsInTallestColumn)
        {
            float authored = S(cardHeight);

            if (cardsInTallestColumn <= 0) return authored;

            // Both positions are measured down from the top of the screen, so the space the
            // column has to live in is the gap between them.
            float columnTop = S(gridTop) - S(46f);
            float available = Mathf.Abs(S(detailTop) - columnTop) - S(cardGap);

            // N cards need N heights plus N-1 gaps between them.
            float fitted = (available - (cardsInTallestColumn - 1) * S(cardGap))
                           / cardsInTallestColumn;

            if (fitted >= authored) return authored;

            const float floor = 28f;
            if (fitted < S(floor))
            {
                Debug.LogWarning($"[ClassSelectUI] The tallest role column has " +
                                 $"{cardsInTallestColumn} classes, which does not fit even at " +
                                 "the minimum card height. The grid will scroll off the bottom.");
                return S(floor);
            }

            return fitted;
        }

        /// <summary>One class in the grid, with the keys that fire its kit down the right side.</summary>
        private void CreateCard(RectTransform parent, int index, float x, float y, float cardHeight)
        {
            var definition = ClassManager.Exists ? ClassManager.Instance.Get(index) : null;

            var rect = CreateRect($"Card_{index}", parent);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(S(columnWidth), cardHeight);
            rect.anchoredPosition = new Vector2(x, y);

            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = RoundedSpriteFactory.Small;
            background.type = Image.Type.Sliced;
            background.color = cardColor;

            // The text stays vertically centred whatever the fitted card height shrank to.
            float textY = -cardHeight * 0.5f + S(8f);

            var name = AddLabel(rect, definition != null ? definition.displayName : "?",
                                new Vector2(-S(columnWidth * 0.5f - 18f), textY - S(14f)),
                                new Vector2(S(columnWidth - 140f), S(28f)), F(cardNameFontSize),
                                TextAnchor.MiddleLeft, textColor);

            var keys = AddLabel(rect, AbilityKeySummary(definition),
                                new Vector2(S(columnWidth * 0.5f - 20f), textY - S(14f)),
                                new Vector2(S(120f), S(28f)), F(cardKeysFontSize),
                                TextAnchor.MiddleRight, dimColor);

            _cards.Add(new Card
            {
                Rect = rect,
                Background = background,
                Name = name,
                Keys = keys,
                Index = index
            });
        }

        /// <summary>The readout for the class under the cursor (or the one already picked).</summary>
        private void BuildDetail(RectTransform root)
        {
            float width = GridWidth;

            _detailPlate = CreateRect("Detail", root);
            _detailPlate.anchorMin = new Vector2(0.5f, 1f);
            _detailPlate.anchorMax = new Vector2(0.5f, 1f);
            _detailPlate.pivot = new Vector2(0.5f, 1f);
            _detailPlate.sizeDelta = new Vector2(width, S(detailHeight));
            _detailPlate.anchoredPosition = new Vector2(0f, S(detailTop));

            var plateImage = _detailPlate.gameObject.AddComponent<Image>();
            plateImage.sprite = RoundedSpriteFactory.Rounded;
            plateImage.type = Image.Type.Sliced;
            plateImage.color = plateColor;

            _detailName = AddLabel(_detailPlate, "", new Vector2(-width * 0.5f + S(24f), -S(18f)),
                                   new Vector2(S(700f), S(40f)), F(detailNameFontSize),
                                   TextAnchor.UpperLeft, textColor);

            _detailRole = AddLabel(_detailPlate, "", new Vector2(width * 0.5f - S(24f), -S(18f)),
                                   new Vector2(S(400f), S(40f)), F(detailNameFontSize),
                                   TextAnchor.UpperRight, accentColor);

            _detailTraits = AddLabel(_detailPlate, "", new Vector2(-width * 0.5f + S(24f), -S(64f)),
                                     new Vector2(width - S(48f), S(28f)), F(detailLineFontSize + 1),
                                     TextAnchor.UpperLeft, dimColor);

            float abilityY = -S(104f);
            for (int i = 0; i < _detailAbilities.Length; i++)
            {
                _detailAbilities[i] = AddLabel(_detailPlate, "",
                                               new Vector2(-width * 0.5f + S(24f), abilityY),
                                               new Vector2(width - S(48f), S(28f)),
                                               F(detailLineFontSize), TextAnchor.UpperLeft, textColor);
                abilityY -= S(32f);
            }

            // A rule above the abilities, separating the class's identity from its kit.
            var rule = CreateRect("DetailRule", _detailPlate);
            rule.anchorMin = new Vector2(0.5f, 1f);
            rule.anchorMax = new Vector2(0.5f, 1f);
            rule.pivot = new Vector2(0.5f, 1f);
            rule.sizeDelta = new Vector2(width - S(48f), S(2f));
            rule.anchoredPosition = new Vector2(0f, -S(96f));
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.sprite = RoundedSpriteFactory.Small;
            ruleImage.type = Image.Type.Sliced;
            ruleImage.color = ruleColor;
        }

        private void BuildLock(RectTransform root)
        {
            float width = GridWidth;

            _lockRect = CreateRect("LockIn", root);
            _lockRect.anchorMin = new Vector2(0.5f, 1f);
            _lockRect.anchorMax = new Vector2(0.5f, 1f);
            _lockRect.pivot = new Vector2(1f, 1f);
            _lockRect.sizeDelta = new Vector2(S(320f), S(66f));
            _lockRect.anchoredPosition = new Vector2(width * 0.5f, S(detailTop) - S(detailHeight) - S(18f));

            _lockBackground = _lockRect.gameObject.AddComponent<Image>();
            _lockBackground.sprite = RoundedSpriteFactory.Small;
            _lockBackground.type = Image.Type.Sliced;
            _lockBackground.color = accentDisabledColor;

            _lockCaption = AddLabel(_lockRect, "PICK A CLASS", new Vector2(0f, -S(10f)),
                                    new Vector2(S(320f), S(40f)), F(buttonFontSize),
                                    TextAnchor.MiddleCenter, dimColor);

            _status = AddLabel(root, "",
                               new Vector2(-width * 0.5f, S(detailTop) - S(detailHeight) - S(20f)),
                               new Vector2(width - S(320f) - S(24f), S(30f)), F(statusFontSize),
                               TextAnchor.MiddleLeft, hintColor);
        }

        // ------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------
        private void Refresh()
        {
            RefreshTimer();
            RefreshChips();
            RefreshCards();
            RefreshDetail();
            RefreshLock();
        }

        private void RefreshTimer()
        {
            if (_timer == null) return;
            if (!GameManager.Exists) { _timer.text = ""; return; }

            _timer.text = FormatTime(GameManager.Instance.TimeRemaining);
        }

        private void RefreshChips()
        {
            int localPick = LocalPick;
            bool localLocked = LocalLocked;

            if (_localChipName != null)
                _localChipName.text = DisplayName(PlayerSlot.One);

            if (_localChipState != null)
            {
                _localChipState.text = localLocked
                    ? ClassName(localPick) + "  [LOCKED]"
                    : (localPick >= 0 ? ClassName(localPick) : "BROWSING...");

                _localChipState.color = localLocked ? accentColor : dimColor;
            }

            int remotePick = GameManager.Exists ? GameManager.Instance.GetClassPick(PlayerSlot.Two) : -1;
            bool remoteLocked = GameManager.Exists && GameManager.Instance.IsClassLocked(PlayerSlot.Two);

            if (_remoteChipName != null)
                _remoteChipName.text = DisplayName(PlayerSlot.Two);

            if (_remoteChipState != null)
            {
                // A pick that is not locked yet is the other player thinking out loud; a locked
                // one is the class they are walking into the match with.
                _remoteChipState.text = remoteLocked
                    ? ClassName(remotePick) + "  [LOCKED]"
                    : (remotePick >= 0 ? ClassName(remotePick) : "BROWSING...");

                _remoteChipState.color = remoteLocked ? accentColor : dimColor;
            }
        }

        private void RefreshCards()
        {
            var mouse = Mouse.current;
            Vector2 pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

            int localPick = LocalPick;
            bool localLocked = LocalLocked;

            foreach (var card in _cards)
            {
                if (card.Rect == null) continue;

                bool picked = card.Index == localPick;
                bool hovered = !localLocked && Hit(card.Rect, pointer);

                if (picked) card.Background.color = cardPickedColor;
                else if (hovered) card.Background.color = cardHoverColor;
                else card.Background.color = cardColor;

                if (card.Name != null)
                {
                    card.Name.color = picked ? Color.white : (localLocked ? dimColor : textColor);
                }

                if (card.Keys != null) card.Keys.color = picked ? accentColor : dimColor;
            }
        }

        /// <summary>The detail plate shows the class being looked at, falling back to the pick.</summary>
        private void RefreshDetail()
        {
            int index = _focusedIndex >= 0 ? _focusedIndex : LocalPick;

            var definition = index >= 0 && ClassManager.Exists ? ClassManager.Instance.Get(index) : null;

            if (_detailName != null)
                _detailName.text = definition != null ? definition.displayName : "CHOOSE A CLASS";

            if (_detailRole != null)
                _detailRole.text = definition != null ? RoleName(definition.role).ToUpperInvariant() : "";

            if (_detailTraits != null)
            {
                _detailTraits.text = definition != null
                    ? $"+ {definition.passiveTrait}     - {definition.negativeTrait}"
                    : "Click a class to preview it. LOCK IN commits it for the whole match.";
            }

            for (int i = 0; i < _detailAbilities.Length; i++)
            {
                var label = _detailAbilities[i];
                if (label == null) continue;

                label.text = definition != null && definition.abilities != null && i < definition.abilities.Count
                    ? AbilityLine(definition, definition.abilities[i])
                    : "";
            }
        }

        /// <summary>One line of the kit: the key, the name, and what it does.</summary>
        private static string AbilityLine(ClassDefinition classDef, AbilityDefinition ability)
        {
            if (ability == null) return "";

            // The key the player has to press, which is their binding when they set one.
            string key = KeyLabel(KeyBindings.GetAbilityKey(classDef, ability.activationKey));
            string name = ability.displayName;
            string description = string.IsNullOrEmpty(ability.description)
                ? ""
                : " - " + Truncate(ability.description, 78);

            // The ultimate is the class's biggest play; marking it makes the X row findable.
            if (ability.isUltimate) name += " (ULT)";

            return $"{key}   {name}{description}";
        }

        private void RefreshLock()
        {
            int localPick = LocalPick;
            bool localLocked = LocalLocked;

            // Lockable the moment a class is pointed at. An already-locked player is done.
            _lockInteractable = localPick >= 0 && !localLocked;

            if (_lockBackground != null)
                _lockBackground.color = _lockInteractable ? accentColor : accentDisabledColor;

            if (_lockCaption != null)
            {
                _lockCaption.text = localLocked ? "LOCKED IN" : (localPick >= 0 ? "LOCK IN" : "PICK A CLASS");
                _lockCaption.color = localLocked ? accentColor : (_lockInteractable ? Color.white : dimColor);
            }

            if (_status != null)
            {
                _status.text = localLocked ? WaitingForOpponent() : "Click a class, then LOCK IN. Enter works too.";
            }
        }

        /// <summary>Says who the screen is still waiting on, rather than just "waiting".</summary>
        private static string WaitingForOpponent()
        {
            bool remoteLocked = GameManager.Exists && GameManager.Instance.IsClassLocked(PlayerSlot.Two);

            if (remoteLocked) return "Both classes locked - the buy phase is opening.";

            // Offline the other slot is the dummy, which locks the instant the phase starts, so
            // reaching here means the match is about to move on its own.
            return LobbyNetwork.IsActive ? "Waiting for the other player to lock in..." : "Starting the match...";
        }

        // ------------------------------------------------------------------
        // Roster helpers
        // ------------------------------------------------------------------
        /// <summary>Every index of the given role, in roster order.</summary>
        private List<int> IndicesForRole(ClassRole role)
        {
            _byRole.Clear();

            if (!ClassManager.Exists) return _byRole;

            var roster = ClassManager.Instance.Classes;
            for (int i = 0; i < roster.Count; i++)
            {
                var definition = roster[i];
                if (definition != null && definition.role == role) _byRole.Add(i);
            }

            return _byRole;
        }

        private static string ClassName(int index)
        {
            if (!ClassManager.Exists) return "Class";
            var definition = ClassManager.Instance.Get(index);
            return definition != null ? definition.displayName.ToUpperInvariant() : "CLASS";
        }

        /// <summary>The four letters of a class's kit, so a card says what it plays like.</summary>
        private static string AbilityKeySummary(ClassDefinition definition)
        {
            if (definition == null || definition.abilities == null || definition.abilities.Count == 0)
                return "";

            var builder = new System.Text.StringBuilder();

            foreach (var ability in definition.abilities)
            {
                if (ability == null) continue;

                if (builder.Length > 0) builder.Append("  ");
                builder.Append(KeyLabel(KeyBindings.GetAbilityKey(definition, ability.activationKey)));
            }

            return builder.ToString();
        }

        private static string RoleName(ClassRole role)
        {
            switch (role)
            {
                case ClassRole.Duelist: return "DUELIST";
                case ClassRole.Initiator: return "INITIATOR";
                case ClassRole.Controller: return "CONTROLLER";
                case ClassRole.Sentinel: return "SENTINEL";
                default: return "";
            }
        }

        private Color RoleColor(ClassRole role)
        {
            switch (role)
            {
                case ClassRole.Duelist: return duelistColor;
                case ClassRole.Initiator: return initiatorColor;
                case ClassRole.Controller: return controllerColor;
                case ClassRole.Sentinel: return sentinelColor;
                default: return dimColor;
            }
        }

        /// <summary>
        /// Slot One is always this machine's player, so the chips read as names rather than
        /// numbers. Offline the other slot is the dummy, and there is nobody to name it after.
        /// </summary>
        private static string DisplayName(PlayerSlot slot)
        {
            bool online = LobbyNetwork.IsActive;

            if (slot == PlayerSlot.One)
            {
                string local = online ? LobbyNetwork.Instance.LocalPlayerName : null;
                return !string.IsNullOrEmpty(local) ? local.ToUpperInvariant() : "YOU";
            }

            string remote = online ? LobbyNetwork.Instance.RemotePlayerName : null;
            return !string.IsNullOrEmpty(remote) ? remote.ToUpperInvariant() : "OPPONENT";
        }

        /// <summary>Reads a key as the letter or symbol printed on it, for the kit lines.</summary>
        private static string KeyLabel(Key key)
        {
            int value = (int)key;

            // A sits at 15 in the engine's enum, so A..Z is 15..40.
            if (value >= 15 && value <= 40)
                return ((char)('A' + value - 15)).ToString();

            return key.ToString().ToUpperInvariant();
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "...";
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{total / 60:0}:{total % 60:00}";
        }

        // ------------------------------------------------------------------
        // uGUI helpers
        // ------------------------------------------------------------------
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

        private static bool Hit(RectTransform rect, Vector2 pointer)
        {
            return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null);
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

        /// <summary>Unity 6 renamed Arial.ttf to LegacyRuntime.ttf, so try both.</summary>
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
