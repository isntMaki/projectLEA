using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// The loadout shop, opened with B.
    ///
    /// Weapons are listed down the left, classes down the right. Left-clicking a weapon buys
    /// it and equips it, selling whatever was carried to help pay for it. Right-clicking a
    /// weapon sells it. CONFIRM closes the shop and commits the loadout.
    ///
    /// TEXT SHARPNESS - why this does not use CanvasScaler.ScaleWithScreenSize:
    /// Legacy UI text rasterises glyphs at fontSize, and the canvas scale factor then scales
    /// the mesh, so any scale other than 1 resamples the text and it goes soft. So the canvas
    /// runs at scaleFactor 1 and this class scales every size and font size itself, meaning
    /// text is rasterised at exactly the pixel size it is drawn at. The layout is rebuilt if
    /// the screen size changes.
    ///
    /// Clicks are hit-tested by hand against each element's rect rather than going through an
    /// EventSystem, which avoids needing an InputSystemUIInputModule at runtime.
    /// </summary>
    public class LoadoutUI : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Key that opens and closes the shop. Overridden by the player's key binding when one is set.")]
        [SerializeField] private Key toggleKey = Key.B;

        [Tooltip("Open automatically whenever the player has no class picked, which is what " +
                 "enforces the mandatory re-pick after each point.")]
        [SerializeField] private bool openWhenNoClassPicked = true;

        [Header("Layout - authored at the reference resolution below")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1200f);

        [SerializeField] private float columnWidth = 720f;
        [SerializeField] private float rowHeight = 80f;
        [SerializeField] private float rowSpacing = 10f;
        [SerializeField] private float sideMargin = 80f;
        [SerializeField] private float headerHeight = 100f;
        [SerializeField] private float footerHeight = 150f;

        [Header("Font sizes - in reference pixels")]
        [SerializeField] private int titleFontSize = 34;
        [SerializeField] private int headingFontSize = 24;
        [SerializeField] private int rowTitleFontSize = 24;
        [SerializeField] private int rowDetailFontSize = 18;
        [SerializeField] private int moneyFontSize = 32;
        [SerializeField] private int statusFontSize = 22;
        [SerializeField] private int buttonFontSize = 24;

        [Header("Colours")]
        [SerializeField] private Color panelColor = new Color(0.03f, 0.04f, 0.06f, 0.96f);
        [SerializeField] private Color rowColor = new Color(1f, 1f, 1f, 0.06f);
        [SerializeField] private Color rowHoverColor = new Color(1f, 1f, 1f, 0.16f);
        [SerializeField] private Color rowSelectedColor = new Color(0.20f, 0.55f, 0.90f, 0.55f);
        [SerializeField] private Color rowOwnedColor = new Color(0.22f, 0.62f, 0.36f, 0.28f);
        [SerializeField] private Color rowLockedColor = new Color(0.55f, 0.18f, 0.18f, 0.28f);
        [SerializeField] private Color textColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        [SerializeField] private Color dimTextColor = new Color(0.66f, 0.70f, 0.75f, 1f);
        [SerializeField] private Color accentColor = new Color(0.20f, 0.78f, 0.62f, 0.90f);
        [SerializeField] private Color accentDisabledColor = new Color(0.35f, 0.38f, 0.42f, 0.55f);

        /// <summary>Holds whichever text component was available, so nothing else has to care.</summary>
        private class Label
        {
            public TextMeshProUGUI Tmp;
            public Text Legacy;

            public void Set(string value)
            {
                if (Tmp != null) Tmp.text = value;
                else if (Legacy != null) Legacy.text = value;
            }

            public void SetColor(Color color)
            {
                if (Tmp != null) Tmp.color = color;
                else if (Legacy != null) Legacy.color = color;
            }
        }

        private class Row
        {
            public RectTransform Rect;
            public Image Background;
            public Label Title;
            public Label Detail;
            public Label Price;
            public int Index;
        }

        private class ShopButton
        {
            public RectTransform Rect;
            public Image Background;
            public Label Caption;
            public System.Action OnClick;
            public bool Interactable = true;
        }

        private readonly List<Row> _weaponRows = new List<Row>();
        private readonly List<Row> _classRows = new List<Row>();

        private GameObject _canvasObject;
        private RectTransform _panel;
        private RectTransform _weaponContent;
        private RectTransform _classContent;
        private Label _moneyText;
        private Label _statusText;
        private ShopButton _confirmButton;
        private Font _legacyFont;

        private bool _isOpen;
        private float _weaponScroll;
        private float _classScroll;
        private string _status = "";
        private MatchState _lastPhase = MatchState.Idle;

        private float _uiScale = 1f;
        private int _builtWidth;
        private int _builtHeight;

        private CursorLockMode _previousLockMode;
        private bool _previousCursorVisible;

        private ProjectLEA.Manuel.PlayerController _playerController;

        private static TMP_FontAsset _tmpFont;
        private static bool _tmpFontResolved;

        public bool IsOpen => _isOpen;

        /// <summary>
        /// Whether a loadout shop is open anywhere, so other systems can stand down without
        /// having to find the instance. There is only ever one shop, so a static flag is enough.
        /// </summary>
        public static bool IsOpenNow { get; private set; }

        /// <summary>
        /// The one shop in the scene, if any. Set on Awake so the HUD's hint can read the key
        /// without searching for the component every frame.
        /// </summary>
        public static LoadoutUI Instance { get; private set; }

        /// <summary>True once a shop exists in the scene and is listening.</summary>
        public static bool ExistsOnScene => Instance != null;

        /// <summary>Printable name of the key that opens the shop, for the HUD hint.</summary>
        public string ToggleKeyName => EffectiveToggleKey.ToString();

        /// <summary>
        /// The key that opens the shop: the player's binding, or the authored default when they
        /// never set one. Read every press rather than cached, so a rebind takes effect at once.
        /// </summary>
        private Key EffectiveToggleKey => KeyBindings.Get("Shop", toggleKey);

        private float S(float value) => value * _uiScale;
        private int F(int value) => Mathf.Max(1, Mathf.RoundToInt(value * _uiScale));

        private void Awake()
        {
            Instance = this;
            _legacyFont = GetBuiltinFont();
            ComputeScale();
            Build();
            SetOpen(false, instant: true);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            if (_isOpen) SetOpen(false);
        }

        private void ComputeScale()
        {
            if (referenceResolution.x < 1f || referenceResolution.y < 1f)
            {
                _uiScale = 1f;
                return;
            }

            // Match the smaller axis so the whole layout always fits on screen.
            _uiScale = Mathf.Min(Screen.width / referenceResolution.x,
                                 Screen.height / referenceResolution.y);
            _uiScale = Mathf.Max(0.2f, _uiScale);

            _builtWidth = Screen.width;
            _builtHeight = Screen.height;
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------
        private void Update()
        {
            // Font sizes are baked in at build time, so a resolution change needs a rebuild.
            if (Screen.width != _builtWidth || Screen.height != _builtHeight) Rebuild();

            SyncWithPhase();

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[EffectiveToggleKey].wasPressedThisFrame && CanOpen)
            {
                SetOpen(!_isOpen);
            }

            if (!_isOpen) return;

            HandlePointer();
            RefreshRows();
        }

        /// <summary>The shop is only usable during buy and prep phases, never mid-round.</summary>
        public bool CanOpen => !GameManager.Exists || GameManager.Instance.IsBuyPhase;

        /// <summary>Opens when a buy phase starts, closes when the round begins.</summary>
        private void SyncWithPhase()
        {
            if (!GameManager.Exists || !openWhenNoClassPicked) return;

            var game = GameManager.Instance;
            if (game.State == _lastPhase) return;

            _lastPhase = game.State;

            if (game.IsBuyPhase) SetOpen(true);
            else if (_isOpen) SetOpen(false);
        }

        private void HandlePointer()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pointer = mouse.position.ReadValue();

            // Confirm is checked first so it always wins over anything underneath it.
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_confirmButton != null && _confirmButton.Interactable && Hit(_confirmButton.Rect, pointer))
                {
                    _confirmButton.OnClick?.Invoke();
                    return;
                }

                if (TryClick(_weaponRows, pointer, HandleWeaponClicked)) return;
                if (TryClick(_classRows, pointer, HandleClassClicked)) return;
            }

            // Right-click sells the weapon under the cursor.
            if (mouse.rightButton.wasPressedThisFrame)
            {
                TryClick(_weaponRows, pointer, HandleWeaponRightClicked);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _weaponScroll = Mathf.Clamp(_weaponScroll + scroll * 0.25f, 0f, MaxScroll(_weaponRows, _weaponContent));
                _classScroll = Mathf.Clamp(_classScroll + scroll * 0.25f, 0f, MaxScroll(_classRows, _classContent));
            }
        }

        private static bool Hit(RectTransform rect, Vector2 pointer)
        {
            return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null);
        }

        private bool TryClick(List<Row> rows, Vector2 pointer, System.Action<int> onHit)
        {
            foreach (var row in rows)
            {
                if (!Hit(row.Rect, pointer)) continue;
                onHit(row.Index);
                return true;
            }
            return false;
        }

        private void HandleWeaponClicked(int index)
        {
            if (!WeaponManager.Exists) return;
            var manager = WeaponManager.Instance;

            var weapon = manager.Get(index);
            if (weapon == null) return;

            if (!manager.IsAllowedByClass(index))
            {
                _status = $"'{weapon.displayName}' is not available to the current class.";
                return;
            }

            if (manager.IsEquipped(index))
            {
                _status = $"{weapon.displayName} is already equipped.";
                return;
            }

            if (manager.IsOwned(index))
            {
                manager.TryEquip(index);
                _status = $"Equipped {weapon.displayName}.";
                return;
            }

            // Buying replaces what you carry: the old weapon is sold to help pay for the new one.
            int balanceAfter = manager.ProjectedBalanceAfterBuying(index);
            if (balanceAfter < 0)
            {
                _status = $"Not enough credits for {weapon.displayName}. " +
                          $"You need {Mathf.Abs(balanceAfter)} more.";
                return;
            }

            if (manager.TrySelect(index))
            {
                _status = $"Bought and equipped {weapon.displayName}. {balanceAfter} credits left.";
            }
        }

        private void HandleWeaponRightClicked(int index)
        {
            if (!WeaponManager.Exists) return;
            var manager = WeaponManager.Instance;

            var weapon = manager.Get(index);
            if (weapon == null) return;

            if (!manager.IsOwned(index))
            {
                _status = $"You do not own {weapon.displayName}.";
                return;
            }

            int refund = manager.GetSellValue(index);
            if (manager.TrySell(index))
                _status = $"Sold {weapon.displayName} for {refund} credits.";
        }

        private void HandleClassClicked(int index)
        {
            if (!ClassManager.Exists) return;

            var manager = ClassManager.Instance;

            var definition = manager.Get(index);
            if (definition == null) return;

            // A class is chosen on the select screen and locked in for the match afterwards.
            // The shop still offers one when no class was picked at all - the select screen
            // being skipped should not leave the match classless.
            if (manager.HasSelection && GameManager.Exists && !GameManager.Instance.ClassChangeAllowed)
            {
                _status = "Your class is locked in for the match. It changes at the next match.";
                return;
            }

            manager.SelectClass(index);
            _status = $"Picked {definition.displayName}. Press CONFIRM to continue.";
        }

        private void HandleConfirmClicked()
        {
            if (ClassManager.Exists && !ClassManager.Instance.HasSelection)
            {
                _status = "Pick a class first.";
                return;
            }

            // Ready up. The round starts once both players are ready, or when the timer runs out.
            if (GameManager.Exists && GameManager.Instance.IsBuyPhase)
                GameManager.Instance.ConfirmReady(PlayerSlot.One);

            SetOpen(false);
        }

        // ------------------------------------------------------------------
        // Open / close
        // ------------------------------------------------------------------
        private void SetOpen(bool open, bool instant = false)
        {
            if (_isOpen == open && !instant) return;
            _isOpen = open;
            IsOpenNow = open;

            if (_panel != null) _panel.gameObject.SetActive(open);

            if (open)
            {
                _previousLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // The class select screen and the shop hand this freeze to each other at a
                // phase change - the shared counter is what keeps the nesting honest.
                MatchUiPause.Push();

                SetPlayerInput(false);

                _status = "";
                RefreshRows();
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
        // Construction
        // ------------------------------------------------------------------
        private void Rebuild()
        {
            if (_canvasObject != null) Destroy(_canvasObject);

            _weaponRows.Clear();
            _classRows.Clear();

            ComputeScale();
            Build();

            // Restore visibility directly rather than through SetOpen: SetOpen would capture
            // the cursor and time-scale state again, and while the panel is open those are
            // already overridden - so closing would restore the overrides and freeze the game.
            if (_panel != null) _panel.gameObject.SetActive(_isOpen);
            if (_isOpen) RefreshRows();
        }

        private void Build()
        {
            _canvasObject = new GameObject("LoadoutCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.transform.SetParent(transform, false);

            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            // Scale factor stays at 1: the layout does its own scaling, so text is rasterised
            // at the exact pixel size it is drawn at and never resampled.
            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            _panel = CreateRect("Panel", (RectTransform)_canvasObject.transform);
            Stretch(_panel, 0f);
            _panel.gameObject.AddComponent<Image>().color = panelColor;

            BuildHeader(_panel);
            BuildFooter(_panel);

            _weaponContent = BuildColumn(_panel, "WEAPONS", left: true);
            _classContent = BuildColumn(_panel, "CLASSES", left: false);

            BuildRows();
            BuildConfirmButton(_panel);

            Debug.Log($"[LoadoutUI] Shop built at {Screen.width}x{Screen.height}, ui scale {_uiScale:0.###}, " +
                      $"text: {(_tmpFont != null ? "TextMeshPro" : "legacy")}.");
        }

        private void BuildHeader(RectTransform parent)
        {
            var title = CreateRect("Title", parent);
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(0f, 1f);
            title.pivot = new Vector2(0f, 1f);
            title.sizeDelta = new Vector2(S(700f), S(48f));
            title.anchoredPosition = new Vector2(S(sideMargin), -S(36f));
            CreateLabel(title, "LOADOUT", F(titleFontSize), TextAnchor.MiddleLeft, textColor);

            var money = CreateRect("Credits", parent);
            money.anchorMin = new Vector2(1f, 1f);
            money.anchorMax = new Vector2(1f, 1f);
            money.pivot = new Vector2(1f, 1f);
            money.sizeDelta = new Vector2(S(700f), S(48f));
            money.anchoredPosition = new Vector2(-S(sideMargin), -S(36f));
            _moneyText = CreateLabel(money, "", F(moneyFontSize), TextAnchor.MiddleRight, accentColor);
        }

        private void BuildFooter(RectTransform parent)
        {
            var status = CreateRect("Status", parent);
            status.anchorMin = new Vector2(0.5f, 0f);
            status.anchorMax = new Vector2(0.5f, 0f);
            status.pivot = new Vector2(0.5f, 0f);
            status.sizeDelta = new Vector2(S(1500f), S(34f));
            status.anchoredPosition = new Vector2(0f, S(28f));
            _statusText = CreateLabel(status, "", F(statusFontSize), TextAnchor.MiddleCenter, dimTextColor);
        }

        private void BuildConfirmButton(RectTransform parent)
        {
            var rect = CreateRect("Confirm", parent);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(S(280f), S(60f));
            rect.anchoredPosition = new Vector2(-S(sideMargin), S(74f));

            var background = rect.gameObject.AddComponent<Image>();
            background.color = accentDisabledColor;

            var caption = CreateRect("Caption", rect);
            Stretch(caption, 0f);
            var label = CreateLabel(caption, "CONFIRM", F(buttonFontSize), TextAnchor.MiddleCenter, textColor);

            _confirmButton = new ShopButton
            {
                Rect = rect,
                Background = background,
                Caption = label,
                OnClick = HandleConfirmClicked
            };
        }

        /// <summary>Creates a titled column and returns its scrollable content rect.</summary>
        private RectTransform BuildColumn(RectTransform parent, string heading, bool left)
        {
            float anchorX = left ? 0f : 1f;
            float posX = left ? S(sideMargin) : -S(sideMargin);

            var column = CreateRect(heading, parent);
            column.anchorMin = new Vector2(anchorX, 0f);
            column.anchorMax = new Vector2(anchorX, 1f);
            column.pivot = new Vector2(anchorX, 0.5f);
            column.sizeDelta = new Vector2(S(columnWidth), -(S(headerHeight) + S(footerHeight)));
            column.anchoredPosition = new Vector2(posX, -(S(headerHeight) - S(footerHeight)) * 0.5f);

            var label = CreateRect("Heading", column);
            label.anchorMin = new Vector2(0f, 1f);
            label.anchorMax = new Vector2(1f, 1f);
            label.pivot = new Vector2(0f, 1f);
            label.sizeDelta = new Vector2(0f, S(40f));
            label.anchoredPosition = new Vector2(0f, S(4f));
            CreateLabel(label, heading, F(headingFontSize), TextAnchor.MiddleLeft, dimTextColor);

            var content = CreateRect("Content", column);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);
            content.anchoredPosition = Vector2.zero;

            return content;
        }

        private void BuildRows()
        {
            if (WeaponManager.Exists)
            {
                var catalog = WeaponManager.Instance.Catalog;
                for (int i = 0; i < catalog.Count; i++)
                    _weaponRows.Add(CreateRow(_weaponContent, i, "Weapon", withPrice: true));
            }

            if (ClassManager.Exists)
            {
                var roster = ClassManager.Instance.Classes;
                for (int i = 0; i < roster.Count; i++)
                    _classRows.Add(CreateRow(_classContent, i, "Class", withPrice: false));
            }
        }

        /// <summary>
        /// One shop card. The title and detail get their own fixed-height boxes at the top of
        /// the card rather than each taking half the row - sharing the row by halves made the
        /// two lines meet exactly at the midline and read as overlapping.
        /// </summary>
        private Row CreateRow(RectTransform parent, int index, string prefix, bool withPrice)
        {
            float y = -index * (S(rowHeight) + S(rowSpacing));

            var rect = CreateRect($"{prefix}_{index}", parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, S(rowHeight));
            rect.anchoredPosition = new Vector2(0f, y);

            var background = rect.gameObject.AddComponent<Image>();
            background.color = rowColor;

            // Title box: 32 tall, 6 from the top.
            var title = CreateRect("Title", rect);
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0f, 1f);
            title.sizeDelta = new Vector2(-S(32f), S(32f));
            title.anchoredPosition = new Vector2(S(16f), -S(6f));
            var titleLabel = CreateLabel(title, "", F(rowTitleFontSize), TextAnchor.UpperLeft, textColor);

            // Detail box: 22 tall, starting 42 from the top, so there is a clear gap.
            var detail = CreateRect("Detail", rect);
            detail.anchorMin = new Vector2(0f, 1f);
            detail.anchorMax = new Vector2(1f, 1f);
            detail.pivot = new Vector2(0f, 1f);
            detail.sizeDelta = new Vector2(-S(32f), S(22f));
            detail.anchoredPosition = new Vector2(S(16f), -S(42f));
            var detailLabel = CreateLabel(detail, "", F(rowDetailFontSize), TextAnchor.UpperLeft, dimTextColor);

            Label priceLabel = null;
            if (withPrice)
            {
                var price = CreateRect("Price", rect);
                price.anchorMin = new Vector2(1f, 1f);
                price.anchorMax = new Vector2(1f, 1f);
                price.pivot = new Vector2(1f, 1f);
                price.sizeDelta = new Vector2(S(260f), S(32f));
                price.anchoredPosition = new Vector2(-S(16f), -S(6f));
                priceLabel = CreateLabel(price, "", F(rowTitleFontSize), TextAnchor.UpperRight, accentColor);
            }

            return new Row
            {
                Rect = rect,
                Background = background,
                Title = titleLabel,
                Detail = detailLabel,
                Price = priceLabel,
                Index = index
            };
        }

        // ------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------
        private void RefreshRows()
        {
            var mouse = Mouse.current;
            Vector2 pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

            if (_moneyText != null)
            {
                int money = EconomyManager.Exists ? EconomyManager.Instance.Money : 0;
                _moneyText.Set($"{money} CREDITS");
            }

            if (_statusText != null) _statusText.Set(_status);

            RefreshWeaponRows(pointer);
            RefreshClassRows(pointer);
            RefreshConfirmButton(pointer);

            if (_weaponContent != null)
                _weaponContent.anchoredPosition = new Vector2(0f, _weaponScroll);
            if (_classContent != null)
                _classContent.anchoredPosition = new Vector2(0f, _classScroll);
        }

        private void RefreshWeaponRows(Vector2 pointer)
        {
            if (!WeaponManager.Exists) return;
            var manager = WeaponManager.Instance;

            foreach (var row in _weaponRows)
            {
                var weapon = manager.Get(row.Index);
                if (weapon == null) continue;

                bool owned = manager.IsOwned(row.Index);
                bool equipped = manager.IsEquipped(row.Index);
                bool allowed = manager.IsAllowedByClass(row.Index);
                bool affordable = manager.CanBuy(row.Index);

                if (row.Title != null) row.Title.Set(weapon.displayName);
                if (row.Detail != null) row.Detail.Set(weapon.Summary());

                if (row.Price != null)
                {
                    if (equipped)
                    {
                        row.Price.Set("EQUIPPED");
                        row.Price.SetColor(accentColor);
                    }
                    else if (owned)
                    {
                        row.Price.Set($"SELL {manager.GetSellValue(row.Index)}");
                        row.Price.SetColor(dimTextColor);
                    }
                    else
                    {
                        row.Price.Set($"{weapon.cost}");
                        row.Price.SetColor(affordable ? textColor : rowLockedColor);
                    }
                }

                if (!allowed) row.Background.color = rowLockedColor;
                else if (equipped) row.Background.color = rowSelectedColor;
                else if (owned) row.Background.color = rowOwnedColor;
                else if (!affordable) row.Background.color = rowLockedColor;
                else row.Background.color = Hit(row.Rect, pointer) ? rowHoverColor : rowColor;
            }
        }

        private void RefreshClassRows(Vector2 pointer)
        {
            if (!ClassManager.Exists) return;
            var manager = ClassManager.Instance;

            foreach (var row in _classRows)
            {
                var definition = manager.Get(row.Index);
                if (definition == null) continue;

                bool selected = manager.SelectedIndex == row.Index;

                // Once the match has started, the pick is settled; a class already chosen reads
                // as committed rather than as something the shop will let you swap.
                bool locked = manager.HasSelection && GameManager.Exists
                              && !GameManager.Instance.ClassChangeAllowed;

                if (row.Title != null)
                    row.Title.Set(selected ? $"{definition.displayName}   [LOCKED]" : definition.displayName);

                if (row.Detail != null)
                    row.Detail.Set(ClassDetail(definition));

                if (selected) row.Background.color = rowSelectedColor;
                else if (locked) row.Background.color = rowLockedColor;
                else row.Background.color = Hit(row.Rect, pointer) ? rowHoverColor : rowColor;
            }
        }

        /// <summary>
        /// One line that says what the class does: its role, then each ability with the key that
        /// fires it. With 29 classes the trait text alone is not enough to pick between them.
        /// Truncated at the width of the column so a long kit does not spill across the panel.
        /// </summary>
        private static string ClassDetail(ClassDefinition definition)
        {
            const int maxLength = 58;

            var builder = new System.Text.StringBuilder();
            builder.Append(definition.role.ToString());

            if (definition.abilities != null)
            {
                foreach (var ability in definition.abilities)
                {
                    if (ability == null) continue;

                    // The key shown is the one the player actually has to press - a rebind that
                    // is not reflected here would make every class description a lie.
                    var key = KeyBindings.GetAbilityKey(definition, ability.activationKey);
                    string keyText = KeyLabel(key);

                    string segment = $"   {keyText}: {ability.displayName}";

                    if (builder.Length + segment.Length > maxLength)
                    {
                        // Keep the keys visible even when the names have to go.
                        builder.Append($"   {keyText}…");
                        continue;
                    }

                    builder.Append(segment);
                }
            }

            return builder.ToString();
        }

        /// <summary>Reads a key as the letter or symbol printed on it, for the ability line.</summary>
        private static string KeyLabel(UnityEngine.InputSystem.Key key)
        {
            int value = (int)key;

            // A sits at 15 in the engine's enum, so A..Z is 15..40.
            if (value >= 15 && value <= 40)
                return ((char)('A' + value - 15)).ToString();

            return key.ToString();
        }

        private void RefreshConfirmButton(Vector2 pointer)
        {
            if (_confirmButton == null) return;

            bool classPicked = ClassManager.Exists && ClassManager.Instance.HasSelection;
            _confirmButton.Interactable = classPicked;

            bool hovered = Hit(_confirmButton.Rect, pointer);

            if (!classPicked) _confirmButton.Background.color = accentDisabledColor;
            else _confirmButton.Background.color = hovered
                ? new Color(accentColor.r, accentColor.g, accentColor.b, 1f)
                : accentColor;

            if (_confirmButton.Caption != null)
                _confirmButton.Caption.Set(classPicked ? "CONFIRM" : "PICK A CLASS");
        }

        private float MaxScroll(List<Row> rows, RectTransform content)
        {
            if (rows.Count == 0 || content == null) return 0f;

            float needed = rows.Count * (S(rowHeight) + S(rowSpacing));
            float available = content.parent is RectTransform parent ? parent.rect.height - S(50f) : needed;
            return Mathf.Max(0f, needed - available);
        }

        // ------------------------------------------------------------------
        // uGUI helpers
        // ------------------------------------------------------------------
        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private Label CreateLabel(RectTransform rect, string content, int size, TextAnchor anchor, Color color)
        {
            var label = new Label();
            var tmpFont = GetTmpFont();

            if (tmpFont != null)
            {
                var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
                tmp.font = tmpFont;
                tmp.fontSize = size;
                tmp.text = content;
                tmp.color = color;
                tmp.raycastTarget = false;
                tmp.alignment = ToTmpAlignment(anchor);
                tmp.overflowMode = TextOverflowModes.Overflow;
                label.Tmp = tmp;
                return label;
            }

            var legacy = rect.gameObject.AddComponent<Text>();
            legacy.font = _legacyFont;
            legacy.fontSize = size;
            legacy.text = content;
            legacy.color = color;
            legacy.raycastTarget = false;
            legacy.alignment = anchor;
            legacy.horizontalOverflow = HorizontalWrapMode.Overflow;
            legacy.verticalOverflow = VerticalWrapMode.Overflow;
            label.Legacy = legacy;
            return label;
        }

        /// <summary>
        /// Returns a usable TMP font asset, or null to use legacy text.
        ///
        /// Deliberately does NOT try TMP_FontAsset.CreateFontAsset on Unity's built-in font:
        /// that fails with "Unable to load font face for [LegacyRuntime]" because the built-in
        /// font ships without readable font data. Only the project's own TMP default is used,
        /// which appears once TMP Essential Resources have been imported.
        /// </summary>
        private static TMP_FontAsset GetTmpFont()
        {
            if (_tmpFontResolved) return _tmpFont;
            _tmpFontResolved = true;

            try
            {
                _tmpFont = TMP_Settings.defaultFontAsset;
            }
            catch (System.Exception)
            {
                _tmpFont = null;
            }

            return _tmpFont;
        }

        private static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
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
