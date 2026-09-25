using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// The ESC settings panel, split into categories so the options are not all lying in one
    /// flat list.
    ///
    /// Deliberately does NOT pause: Time.timeScale is left alone, so the round timer keeps
    /// running and the match carries on around you. The cursor is unlocked and player input
    /// switched off so the panel can be used, which does mean you are a sitting duck in here.
    ///
    /// Sliders are dragged with the mouse. That is hand-rolled rather than using uGUI's
    /// Slider component, which would need an EventSystem and an InputSystemUIInputModule to
    /// exist at runtime - two more things that can silently fail.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        [SerializeField] private Key toggleKey = Key.Escape;
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1200f);

        [Header("Panel")]
        [SerializeField] private float panelWidth = 1040f;
        [SerializeField] private float panelHeight = 1120f;
        [SerializeField] private float rowHeight = 50f;
        [SerializeField] private float rowSpacing = 8f;

        [Header("Fonts")]
        [SerializeField] private int titleFontSize = 36;
        [SerializeField] private int rowFontSize = 24;
        [SerializeField] private int hintFontSize = 18;

        [Header("Colours")]
        [SerializeField] private Color backdropColor = new Color(0.02f, 0.03f, 0.04f, 0.82f);
        [SerializeField] private Color panelColor = new Color(0.07f, 0.08f, 0.11f, 0.98f);
        [SerializeField] private Color rowColor = new Color(1f, 1f, 1f, 0.05f);
        [SerializeField] private Color rowHoverColor = new Color(1f, 1f, 1f, 0.12f);
        [SerializeField] private Color trackColor = new Color(1f, 1f, 1f, 0.12f);
        [SerializeField] private Color fillColor = new Color(0.20f, 0.78f, 0.62f, 0.95f);
        [SerializeField] private Color textColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        [SerializeField] private Color dimColor = new Color(0.62f, 0.66f, 0.72f, 1f);
        [SerializeField] private Color accentColor = new Color(0.20f, 0.78f, 0.62f, 1f);
        [SerializeField] private Color selectedTabColor = new Color(0.20f, 0.78f, 0.62f, 0.30f);

        [Header("Sliders")]
        [SerializeField] private float touchPadding = 16f;

        /// <summary>
        /// The categories the panel is split into. Order is the order the tabs appear in, and
        /// it is also the order the settings are laid out inside the panel.
        /// </summary>
        private enum Category
        {
            Controls,
            Gameplay,
            Video,
            Audio
        }

        private class SliderSetting
        {
            public RectTransform Row;
            public Image Background;
            public RectTransform Touch;
            public RectTransform Track;
            public RectTransform Fill;
            public RectTransform Knob;
            public Text Value;
            public float Min;
            public float Max;
            public string Format = "0.00";
            public System.Func<float> Read;
            public System.Action<float> Write;
            public bool Dragging;
        }

        private class CycleSetting
        {
            public RectTransform Row;
            public Image Background;
            public Text Value;
            public System.Func<string> Read;
            public System.Action<int> Step;
        }

        /// <summary>
        /// A rebindable key row. Clicking the key box puts the panel into a listening state,
        /// and the next key pressed becomes the binding. The row remembers its action so the
        /// capture knows where to write.
        /// </summary>
        private class RebindSetting
        {
            public RectTransform Row;
            public Image Background;
            public RectTransform KeyBox;
            public Text KeyLabel;
            public string Action;
            public bool IsAbility;     // an ability key: falls back to the class asset
        }

        private class TabSetting
        {
            public RectTransform Row;
            public Image Background;
            public Text Label;
            public Category Category;
        }

        private readonly List<SliderSetting> _sliders = new List<SliderSetting>();
        private readonly List<CycleSetting> _cycles = new List<CycleSetting>();
        /// <summary>
        /// The rebind rows. The reset tile lives here too - it is the same kind of click target,
        /// and its action name is what tells the handler that a click clears everything rather
        /// than starting a listen.
        /// </summary>
        private readonly List<RebindSetting> _rebinds = new List<RebindSetting>();

        /// <summary>
        /// The "reset keys" tile on the tab strip. Built once with the tabs rather than with a
        /// category's rows, so switching category never tears it down or orphans its handler.
        /// </summary>
        private RebindSetting _resetTile;
        private readonly List<TabSetting> _tabs = new List<TabSetting>();

        private Category _category = Category.Controls;

        /// <summary>The row waiting for a key press, or null when the panel is not listening.</summary>
        private RebindSetting _listening;

        /// <summary>Curated so every aspect ratio the game might run at is reachable.</summary>
        private readonly List<Vector2Int> _resolutions = new List<Vector2Int>
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080),
            new Vector2Int(1920, 1200),
            new Vector2Int(2560, 1440),
            new Vector2Int(3440, 1440),
            new Vector2Int(3840, 2160),
            new Vector2Int(1024, 768)
        };

        private GameObject _canvasObject;
        private RectTransform _backdrop;
        private RectTransform _panel;
        private RectTransform _content;
        private Font _font;
        private bool _isOpen;

        private float _uiScale = 1f;
        private int _builtWidth;
        private int _builtHeight;

        private CursorLockMode _previousLock;
        private bool _previousCursorVisible;
        private ProjectLEA.Manuel.PlayerController _player;
        private Camera _camera;

        private float S(float v) => v * _uiScale;
        private int F(int v) => Mathf.Max(1, Mathf.RoundToInt(v * _uiScale));

        public bool IsOpen => _isOpen;

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

        private void ComputeScale()
        {
            _uiScale = Mathf.Max(0.2f, Mathf.Min(Screen.width / referenceResolution.x,
                                                Screen.height / referenceResolution.y));
            _builtWidth = Screen.width;
            _builtHeight = Screen.height;
        }

        private void Update()
        {
            if (Screen.width != _builtWidth || Screen.height != _builtHeight) Rebuild();

            // A rebind in progress eats the key press before anything else can react to it -
            // including this panel's own close key, which is what makes Escape cancel a
            // rebind instead of closing the menu underneath the player.
            if (_listening != null)
            {
                CaptureRebind();
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) SetOpen(!_isOpen);

            if (!_isOpen) return;

            HandlePointer();
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------
        private void HandlePointer()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pointer = mouse.position.ReadValue();
            bool held = mouse.leftButton.isPressed;
            bool pressed = mouse.leftButton.wasPressedThisFrame;

            HandleSliderDrag(pointer, held, pressed);

            if (pressed)
            {
                // Only one row may consume a click. Tabs come first, because switching category
                // tears the other rows down and a click that landed on a tab should not also
                // reach the row that was underneath it a frame ago.
                if (!HandleTabClick(pointer) && !HandleRebindClick(pointer)) HandleCycleClick(pointer);
            }

            RefreshHover(pointer);
        }

        private void HandleSliderDrag(Vector2 pointer, bool held, bool pressed)
        {
            foreach (var slider in _sliders)
            {
                // Only a press on the bar itself grabs it. The row is 920 wide and the bar
                // only 380, so starting the drag on the row made the label clickable.
                if (pressed && Hit(slider.Touch, pointer))
                {
                    slider.Dragging = true;
                    ApplySliderFromPointer(slider, pointer);
                    continue;
                }

                if (slider.Dragging && held) ApplySliderFromPointer(slider, pointer);
                else if (slider.Dragging) slider.Dragging = false;
            }
        }

        private void ApplySliderFromPointer(SliderSetting slider, Vector2 pointer)
        {
            if (slider.Track == null) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    slider.Track, pointer, null, out var local))
            {
                return;
            }

            Rect r = slider.Track.rect;
            if (r.width <= 0.01f) return;

            // rect.xMin is the left edge in local space regardless of the pivot, which is
            // (0, 0.5) here. Dividing straight by width had the value at half way when the
            // pointer was at the left edge and at maximum in the middle of the bar.
            float t = Mathf.Clamp01((local.x - r.xMin) / r.width);

            slider.Write(Mathf.Lerp(slider.Min, slider.Max, t));
            RefreshSlider(slider);
        }

        private bool HandleTabClick(Vector2 pointer)
        {
            foreach (var tab in _tabs)
            {
                if (!Hit(tab.Row, pointer)) continue;

                SelectCategory(tab.Category);
                return true;
            }

            return false;
        }

        private bool HandleRebindClick(Vector2 pointer)
        {
            // The reset tile is a button, not a rebind: it clears every override and refreshes,
            // which is a different thing from waiting on the next keypress.
            if (_resetTile != null && Hit(_resetTile.KeyBox, pointer))
            {
                KeyBindings.Reset();
                _listening = null;
                RefreshAll();
                return true;
            }

            foreach (var rebind in _rebinds)
            {
                if (!Hit(rebind.KeyBox, pointer)) continue;

                // The row reads "Press a key…" from this frame until the next press lands.
                _listening = rebind;
                RefreshRebind(rebind);
                return true;
            }

            return false;
        }

        private void HandleCycleClick(Vector2 pointer)
        {
            foreach (var cycle in _cycles)
            {
                if (!Hit(cycle.Row, pointer)) continue;

                cycle.Step(1);
                RefreshCycle(cycle);
                return;
            }
        }

        /// <summary>
        /// Takes the next pressed key and makes it the listening row's binding. Escape is the
        /// escape hatch: it cancels without changing anything, which is also why the panel's
        /// own close key has to be suppressed while a rebind is running.
        /// </summary>
        private void CaptureRebind()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var row = _listening;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                _listening = null;
                RefreshRebind(row);
                return;
            }

            // Scan the enum's own values rather than a range, because the Key enum is not
            // guaranteed to be contiguous and an out-of-range cast would index the keyboard's
            // control array past its end. Only keys do this - mouse buttons stay on the mouse,
            // where fire and aim live.
            foreach (var value in System.Enum.GetValues(typeof(Key)))
            {
                var key = (Key)value;
                if (key == Key.None) continue;
                if (!keyboard[key].wasPressedThisFrame) continue;

                KeyBindings.Set(row.Action, key);
                _listening = null;
                RefreshAll();
                return;
            }
        }

        private static bool Hit(RectTransform rect, Vector2 pointer)
        {
            return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null);
        }

        // ------------------------------------------------------------------
        // Open / close
        // ------------------------------------------------------------------
        /// <summary>Opens or closes the panel. Public so the main menu can open it.</summary>
        public void SetOpen(bool open, bool instant = false)
        {
            if (_isOpen == open && !instant) return;
            _isOpen = open;

            if (_backdrop != null) _backdrop.gameObject.SetActive(open);
            if (_panel != null) _panel.gameObject.SetActive(open);

            if (open)
            {
                _previousLock = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // No timeScale change: the match keeps running.
                SetPlayerInput(false);
                RefreshAll();
            }
            else if (!instant)
            {
                Cursor.lockState = _previousLock;
                Cursor.visible = _previousCursorVisible;
                SetPlayerInput(true);

                foreach (var slider in _sliders) slider.Dragging = false;

                // A rebind left mid-listen would otherwise swallow the first key the player
                // presses after the panel closed.
                _listening = null;
            }
        }

        private void SetPlayerInput(bool enabled)
        {
            if (ResolvePlayer() != null) _player.InputEnabled = enabled;
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------
        private void Rebuild()
        {
            if (_canvasObject != null) Destroy(_canvasObject);
            _sliders.Clear();
            _cycles.Clear();
            _rebinds.Clear();
            _tabs.Clear();
            _resetTile = null;
            _listening = null;

            ComputeScale();
            Build();

            if (_backdrop != null) _backdrop.gameObject.SetActive(_isOpen);
            if (_panel != null) _panel.gameObject.SetActive(_isOpen);
            if (_isOpen) RefreshAll();
        }

        private void Build()
        {
            _canvasObject = new GameObject("SettingsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.transform.SetParent(transform, false);

            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;

            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            var root = (RectTransform)_canvasObject.transform;

            // Full-screen dimmer behind the card.
            _backdrop = CreateRect("Backdrop", root);
            Stretch(_backdrop, 0f);
            _backdrop.gameObject.AddComponent<Image>().color = backdropColor;

            // The card itself, centred on screen.
            _panel = CreateRect("Panel", root);
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(S(panelWidth), S(panelHeight));
            _panel.anchoredPosition = Vector2.zero;
            _panel.gameObject.AddComponent<Image>().color = panelColor;

            AddLabel(_panel, "SETTINGS", new Vector2(S(40f), -S(30f)), new Vector2(S(600f), S(46f)),
                     F(titleFontSize), TextAnchor.MiddleLeft, accentColor);

            AddLabel(_panel, "The match keeps running while this is open.",
                     new Vector2(S(40f), -S(78f)), new Vector2(S(900f), S(28f)),
                     F(hintFontSize), TextAnchor.MiddleLeft, dimColor);

            BuildTabs();

            // Everything below the tab strip is replaced when the category changes, so it lives
            // in its own container rather than directly on the panel.
            _content = CreateRect("Content", _panel);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.anchoredPosition = new Vector2(0f, -S(tabStripBottom));
            _content.sizeDelta = new Vector2(0f, S(panelHeight - tabStripBottom - 40f));

            BuildCategory();
        }

        /// <summary>The tabs themselves, built once: switching category only replaces content.</summary>
        private void BuildTabs()
        {
            // The reset button rides the tab strip as a fifth tile, because it is a whole-panel
            // action rather than something that belongs to one category's rows.
            float tabWidth = (panelWidth - 80f - TabCount * tabSpacing) / (TabCount + 1f);

            for (int i = 0; i < TabCount; i++)
            {
                var category = (Category)i;
                float x = 40f + i * (tabWidth + tabSpacing);

                var tab = CreateRect($"Tab{category}", _panel);
                tab.anchorMin = new Vector2(0f, 1f);
                tab.anchorMax = new Vector2(0f, 1f);
                tab.pivot = new Vector2(0f, 1f);
                tab.sizeDelta = new Vector2(S(tabWidth), S(tabHeight));
                tab.anchoredPosition = new Vector2(S(x), -S(tabTop));

                var setting = new TabSetting
                {
                    Row = tab,
                    Background = tab.gameObject.AddComponent<Image>(),
                    Label = AddLabel(tab, category.ToString().ToUpperInvariant(),
                                     new Vector2(S(10f), -S(4f)), new Vector2(S(tabWidth - 20f), S(tabHeight - 8f)),
                                     F(rowFontSize), TextAnchor.MiddleCenter, textColor),
                    Category = category
                };

                setting.Background.color = rowColor;
                _tabs.Add(setting);
            }

            // The reset tile: same shape as a tab, but a click clears the key bindings rather
            // than switching page, so it lives in the rebind list where the click handler looks.
            float resetX = 40f + TabCount * (tabWidth + tabSpacing);

            var reset = CreateRect("ResetBindings", _panel);
            reset.anchorMin = new Vector2(0f, 1f);
            reset.anchorMax = new Vector2(0f, 1f);
            reset.pivot = new Vector2(0f, 1f);
            reset.sizeDelta = new Vector2(S(tabWidth), S(tabHeight));
            reset.anchoredPosition = new Vector2(S(resetX), -S(tabTop));
            reset.gameObject.AddComponent<Image>().color = rowColor;

            AddLabel(reset, "RESET KEYS", new Vector2(S(10f), -S(4f)),
                     new Vector2(S(tabWidth - 20f), S(tabHeight - 8f)),
                     F(rowFontSize), TextAnchor.MiddleCenter, dimColor);

            _resetTile = new RebindSetting
            {
                Row = reset,
                Background = reset.GetComponent<Image>(),
                KeyBox = reset,
                KeyLabel = null,
                Action = ResetAction,
                IsAbility = false
            };
        }

        private const float tabTop = 116f;
        private const float tabHeight = 46f;
        private const float tabSpacing = 8f;
        private const float tabStripBottom = tabTop + tabHeight + 14f;

        private static int TabCount => System.Enum.GetValues(typeof(Category)).Length;

        /// <summary>Tears the current category's rows down and builds the new one's.</summary>
        private void SelectCategory(Category category)
        {
            if (_category == category) return;

            _category = category;
            BuildCategory();
            RefreshAll();
        }

        /// <summary>
        /// Fills the content container with the current category's rows. Rows are rebuilt from
        /// scratch every time, so a category never inherits another one's stale widgets.
        /// </summary>
        private void BuildCategory()
        {
            ClearContent();

            float y = 0f;

            switch (_category)
            {
                case Category.Controls:
                    y = BuildControls(y);
                    break;

                case Category.Gameplay:
                    y = AddSlider("Mouse sensitivity", y, 0.02f, 0.60f, "0.00", ReadSensitivity, WriteSensitivity);
                    y = AddSlider("Field of view", y, 60f, 110f, "0", ReadFov, WriteFov);
                    break;

                case Category.Video:
                    y = AddCycle("Display mode", y, ReadDisplayMode, StepDisplayMode);
                    y = AddCycle("Resolution", y, ReadResolution, StepResolution);
                    y = AddCycle("V-Sync", y, ReadVSync, StepVSync);
                    y = AddCycle("MSAA", y, ReadAntiAliasing, StepAntiAliasing);
                    y = AddCycle("Anti-aliasing", y, ReadCameraAA, StepCameraAA);
                    y = AddCycle("FPS cap", y, ReadFpsCap, StepFpsCap);
                    y = AddCycle("Quality level", y, ReadQuality, StepQuality);
                    break;

                case Category.Audio:
                    y = AddSlider("Master volume", y, 0f, 1f, "0.00", ReadVolume, WriteVolume);
                    break;
            }

            string hint = _category switch
            {
                Category.Controls => "Click a key, then press the key you want. Escape cancels.",
                Category.Gameplay => "Drag a bar to set it.",
                Category.Video => "Click a row to cycle it. Right-click steps back.",
                _ => "Drag a bar to set it."
            };

            AddLabel(_content, hint, new Vector2(S(0f), y - S(6f)), new Vector2(S(panelWidth - 80f), S(28f)),
                     F(hintFontSize), TextAnchor.MiddleLeft, dimColor);
        }

        /// <summary>
        /// The Controls category: every rebindable action, then the current class's abilities,
        /// then the reset. Ability keys only appear once a class is equipped - before that there
        /// is no class for a binding to override, and the row would promise a rebind it cannot
        /// describe.
        /// </summary>
        private float BuildControls(float y)
        {
            y = AddHeading(y, "MOVEMENT & ACTIONS");

            foreach (var action in KeyBindings.Actions)
            {
                y = AddRebind(y, action, isAbility: false);
            }

            var currentClass = ClassManager.Exists ? ClassManager.Instance.Selected : null;

            if (currentClass != null && currentClass.abilities != null && currentClass.abilities.Count > 0)
            {
                y = AddHeading(y, $"{currentClass.displayName.ToUpperInvariant()} ABILITIES");

                foreach (var ability in currentClass.abilities)
                {
                    if (ability == null) continue;

                    // Stored under the class's own name, so a binding for one class's dash does
                    // not silently become the binding for every other class's dash.
                    y = AddRebind(y, KeyBindings.AbilityAction(currentClass), isAbility: true,
                                  label: $"{ability.displayName} key");
                }
            }

            return y;
        }

        /// <summary>The action name the reset tile reports, picked up by the rebind click handler.</summary>
        private const string ResetAction = "__reset__";

        /// <summary>A small caps heading between groups of rows.</summary>
        private float AddHeading(float y, string text)
        {
            AddLabel(_content, text, new Vector2(S(4f), y - S(2f)), new Vector2(S(panelWidth - 88f), S(28f)),
                     F(hintFontSize + 2), TextAnchor.MiddleLeft, accentColor);

            return y - S(34f);
        }

        private void ClearContent()
        {
            if (_content == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Destroy(_content.GetChild(i).gameObject);
            }

            _sliders.Clear();
            _cycles.Clear();
            _rebinds.Clear();
            _listening = null;
        }

        private float AddSlider(string name, float y, float min, float max, string format,
                                System.Func<float> read, System.Action<float> write)
        {
            var row = CreateRow(name, y);
            AddLabel(row, name, new Vector2(S(20f), -S(4f)), new Vector2(S(300f), S(38f)),
                     F(rowFontSize), TextAnchor.MiddleLeft, textColor);

            // Track, knob and value all sit inside the row with margin to spare. The old
            // offsets ran the value label right up to the panel edge, which clipped it.
            var track = CreateRect("Track", row);
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(0f, 0.5f);
            track.pivot = new Vector2(0f, 0.5f);
            track.sizeDelta = new Vector2(S(380f), S(24f));
            track.anchoredPosition = new Vector2(S(340f), 0f);
            track.gameObject.AddComponent<Image>().color = trackColor;

            // Invisible, no image, wider than the bar and as tall as the row: a generous
            // grab target that still stops at the bar's ends. This is what the pointer hits.
            var touch = CreateRect("Touch", row);
            touch.anchorMin = new Vector2(0f, 0f);
            touch.anchorMax = new Vector2(0f, 1f);
            touch.pivot = new Vector2(0f, 0.5f);
            touch.anchoredPosition = new Vector2(S(340f - touchPadding), 0f);
            touch.sizeDelta = new Vector2(S(380f + touchPadding * 2f), 0f);

            var fill = CreateRect("Fill", track);
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<Image>().color = fillColor;

            // A knob makes it read as draggable rather than as a progress bar.
            var knob = CreateRect("Knob", track);
            knob.anchorMin = new Vector2(0f, 0.5f);
            knob.anchorMax = new Vector2(0f, 0.5f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            knob.sizeDelta = new Vector2(S(22f), S(36f));
            knob.anchoredPosition = Vector2.zero;
            knob.gameObject.AddComponent<Image>().color = fillColor;

            var value = AddLabel(row, "", new Vector2(S(730f), -S(4f)), new Vector2(S(170f), S(38f)),
                                 F(rowFontSize), TextAnchor.MiddleRight, accentColor);

            var slider = new SliderSetting
            {
                Row = row,
                Background = row.GetComponent<Image>(),
                Touch = touch,
                Track = track,
                Fill = fill,
                Knob = knob,
                Value = value,
                Min = min,
                Max = max,
                Format = format,
                Read = read,
                Write = write
            };

            _sliders.Add(slider);
            RefreshSlider(slider);

            return y - S(rowHeight + rowSpacing);
        }

        private float AddCycle(string name, float y, System.Func<string> read, System.Action<int> step)
        {
            var row = CreateRow(name, y);
            AddLabel(row, name, new Vector2(S(20f), -S(4f)), new Vector2(S(360f), S(38f)),
                     F(rowFontSize), TextAnchor.MiddleLeft, textColor);

            var value = AddLabel(row, "", new Vector2(S(400f), -S(4f)), new Vector2(S(500f), S(38f)),
                                 F(rowFontSize), TextAnchor.MiddleRight, accentColor);

            var cycle = new CycleSetting
            {
                Row = row,
                Background = row.GetComponent<Image>(),
                Value = value,
                Read = read,
                Step = step
            };

            _cycles.Add(cycle);
            RefreshCycle(cycle);

            return y - S(rowHeight + rowSpacing);
        }

        /// <summary>
        /// A rebind row: the action's name on the left, the key it is bound to on the right
        /// inside a box. Clicking the box is what starts a rebind.
        /// </summary>
        private float AddRebind(float y, string action, bool isAbility, string label = null)
        {
            var row = CreateRow(action, y);

            AddLabel(row, label ?? action, new Vector2(S(20f), -S(4f)), new Vector2(S(520f), S(38f)),
                     F(rowFontSize), TextAnchor.MiddleLeft, textColor);

            var box = CreateRect("KeyBox", row);
            box.anchorMin = new Vector2(1f, 0.5f);
            box.anchorMax = new Vector2(1f, 0.5f);
            box.pivot = new Vector2(1f, 0.5f);
            box.sizeDelta = new Vector2(S(200f), S(38f));
            box.anchoredPosition = new Vector2(-S(20f), 0f);
            box.gameObject.AddComponent<Image>().color = trackColor;

            var keyLabel = AddLabel(box, "", new Vector2(S(0f), -S(4f)), new Vector2(S(200f), S(38f)),
                                    F(rowFontSize), TextAnchor.MiddleCenter, textColor);

            var rebind = new RebindSetting
            {
                Row = row,
                Background = row.GetComponent<Image>(),
                KeyBox = box,
                KeyLabel = keyLabel,
                Action = action,
                IsAbility = isAbility
            };

            _rebinds.Add(rebind);
            RefreshRebind(rebind);

            return y - S(rowHeight + rowSpacing);
        }

        private RectTransform CreateRow(string name, float y)
        {
            var row = CreateRect(name, _content);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(S(panelWidth - 80f), S(rowHeight));
            row.anchoredPosition = new Vector2(S(40f), y);

            row.gameObject.AddComponent<Image>().color = rowColor;
            return row;
        }

        // ------------------------------------------------------------------
        // Slider values
        // ------------------------------------------------------------------
        /// <summary>
        /// Stands in for the player's value when there is no PlayerController, which is the
        /// case in the main menu. Without it the slider would snap back to the default on
        /// every drag, because the write would go nowhere.
        /// </summary>
        private float _fallbackSensitivity = 0.15f;

        private float ReadSensitivity() =>
            ResolvePlayer() != null ? _player.MouseSensitivity : _fallbackSensitivity;

        private void WriteSensitivity(float value)
        {
            _fallbackSensitivity = value;
            if (ResolvePlayer() != null) _player.MouseSensitivity = value;
        }

        private float ReadFov() => ResolveCamera() != null ? _camera.fieldOfView : 60f;

        private void WriteFov(float value)
        {
            if (ResolveCamera() != null) _camera.fieldOfView = value;
        }

        private float ReadVolume() => AudioListener.volume;

        private void WriteVolume(float value) => AudioListener.volume = Mathf.Clamp01(value);

        private ProjectLEA.Manuel.PlayerController ResolvePlayer()
        {
            if (_player != null) return _player;

            var go = GameObject.Find("Player");
            if (go != null) _player = go.GetComponent<ProjectLEA.Manuel.PlayerController>();
            return _player;
        }

        private Camera ResolveCamera()
        {
            if (_camera != null) return _camera;

            if (ResolvePlayer() != null) _camera = _player.GetComponentInChildren<Camera>();
            if (_camera == null) _camera = Camera.main;
            return _camera;
        }

        // ------------------------------------------------------------------
        // Cycle values
        // ------------------------------------------------------------------
        private string ReadDisplayMode()
        {
            switch (Screen.fullScreenMode)
            {
                case FullScreenMode.ExclusiveFullScreen: return "Fullscreen";
                case FullScreenMode.FullScreenWindow: return "Borderless";
                case FullScreenMode.MaximizedWindow: return "Maximised";
                default: return "Windowed";
            }
        }

        private void StepDisplayMode(int direction)
        {
            var mode = Screen.fullScreenMode;

            if (mode == FullScreenMode.ExclusiveFullScreen) ApplyDisplayMode(FullScreenMode.FullScreenWindow);
            else if (mode == FullScreenMode.FullScreenWindow) ApplyDisplayMode(FullScreenMode.Windowed);
            else ApplyDisplayMode(FullScreenMode.ExclusiveFullScreen);
        }

        private static void ApplyDisplayMode(FullScreenMode mode)
        {
            Screen.SetResolution(Screen.width, Screen.height, mode);
        }

        private string ReadResolution()
        {
            var current = new Vector2Int(Screen.width, Screen.height);

            foreach (var size in _resolutions)
            {
                if (size == current) return Describe(size);
            }

            return $"{Screen.width} x {Screen.height}   current";
        }

        private void StepResolution(int direction)
        {
            var current = new Vector2Int(Screen.width, Screen.height);
            int index = _resolutions.IndexOf(current);

            // An unlisted resolution starts the cycle at the first entry rather than stalling.
            index = index < 0 ? 0 : (index + 1) % _resolutions.Count;

            var target = _resolutions[index];
            Screen.SetResolution(target.x, target.y, Screen.fullScreenMode);
        }

        private static string Describe(Vector2Int size)
        {
            float ratio = (float)size.x / size.y;

            string aspect = Mathf.Abs(ratio - 16f / 9f) < 0.02f ? "16:9"
                          : Mathf.Abs(ratio - 16f / 10f) < 0.02f ? "16:10"
                          : Mathf.Abs(ratio - 4f / 3f) < 0.02f ? "4:3"
                          : Mathf.Abs(ratio - 21f / 9f) < 0.05f ? "21:9"
                          : $"{ratio:0.##}:1";

            return $"{size.x} x {size.y}   {aspect}";
        }

        private static string ReadVSync()
        {
            switch (QualitySettings.vSyncCount)
            {
                case 1: return "On";
                case 2: return "Half";
                default: return "Off";
            }
        }

        private static void StepVSync(int direction)
        {
            QualitySettings.vSyncCount = (QualitySettings.vSyncCount + 1) % 3;
        }

        private string ReadAntiAliasing()
        {
            int samples = GetMsaa();
            return samples <= 1 ? "Off" : $"{samples}x MSAA";
        }

        private static void StepAntiAliasing(int direction)
        {
            int[] steps = { 1, 2, 4, 8 };
            int current = GetMsaa();

            int index = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] == current) { index = i; break; }
            }

            SetMsaa(steps[(index + 1) % steps.Length]);
        }

        /// <summary>URP owns MSAA, so read it from the pipeline asset when there is one.</summary>
        private static int GetMsaa()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return pipeline != null ? pipeline.msaaSampleCount : QualitySettings.antiAliasing;
        }

        private static void SetMsaa(int samples)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null) pipeline.msaaSampleCount = samples;

            // Kept in step as well, for anything reading the built-in value.
            QualitySettings.antiAliasing = samples;
        }

        /// <summary>
        /// Camera-level post-process anti-aliasing. In URP this is entirely separate from
        /// MSAA, and it is where TAA lives.
        /// </summary>
        private string ReadCameraAA()
        {
            var data = GetCameraData();
            if (data == null) return "n/a";

            switch (data.antialiasing)
            {
                case AntialiasingMode.FastApproximateAntialiasing: return "FXAA";
                case AntialiasingMode.SubpixelMorphologicalAntiAliasing: return "SMAA";
                case AntialiasingMode.TemporalAntiAliasing: return "TAA";
                default: return "Off";
            }
        }

        private void StepCameraAA(int direction)
        {
            var data = GetCameraData();
            if (data == null) return;

            switch (data.antialiasing)
            {
                case AntialiasingMode.None:
                    data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    break;

                case AntialiasingMode.FastApproximateAntialiasing:
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    break;

                case AntialiasingMode.SubpixelMorphologicalAntiAliasing:
                    data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
                    break;

                default:
                    data.antialiasing = AntialiasingMode.None;
                    break;
            }

            // SMAA and TAA are both applied by the post-process pass, so they do nothing
            // unless the camera has post-processing switched on.
            if (data.antialiasing == AntialiasingMode.SubpixelMorphologicalAntiAliasing ||
                data.antialiasing == AntialiasingMode.TemporalAntiAliasing)
            {
                data.renderPostProcessing = true;
            }
        }

        private UniversalAdditionalCameraData GetCameraData()
        {
            var camera = ResolveCamera();
            return camera != null ? camera.GetUniversalAdditionalCameraData() : null;
        }

        private static string ReadFpsCap()
        {
            int cap = Application.targetFrameRate;
            return cap <= 0 ? "Unlimited" : $"{cap} fps";
        }

        private static void StepFpsCap(int direction)
        {
            int[] steps = { 30, 60, 120, 144, 240, -1 };
            int current = Application.targetFrameRate;

            int index = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] == current) { index = i; break; }
            }

            Application.targetFrameRate = steps[(index + 1) % steps.Length];
        }

        private static string ReadQuality()
        {
            var names = QualitySettings.names;
            if (names == null || names.Length == 0) return "n/a";

            return names[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, names.Length - 1)];
        }

        private static void StepQuality(int direction)
        {
            int count = QualitySettings.names.Length;
            if (count == 0) return;

            QualitySettings.SetQualityLevel((QualitySettings.GetQualityLevel() + 1) % count);
        }

        // ------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------
        private void RefreshAll()
        {
            foreach (var tab in _tabs) RefreshTab(tab);

            foreach (var slider in _sliders) RefreshSlider(slider);
            foreach (var cycle in _cycles) RefreshCycle(cycle);
            foreach (var rebind in _rebinds) RefreshRebind(rebind);
        }

        private void RefreshTab(TabSetting tab)
        {
            if (tab.Background == null) return;

            // The active category is filled and tinted, the rest sit flush, so the split is
            // visible without the content having to say which page it is.
            tab.Background.color = tab.Category == _category ? selectedTabColor : rowColor;
            if (tab.Label != null)
            {
                tab.Label.color = tab.Category == _category ? accentColor : textColor;
            }
        }

        private void RefreshSlider(SliderSetting slider)
        {
            float value = Mathf.Clamp(slider.Read(), slider.Min, slider.Max);

            if (slider.Value != null)
            {
                string text = value.ToString(slider.Format);
                if (slider.Value.text != text) slider.Value.text = text;
            }

            if (slider.Fill == null) return;

            float t = Mathf.Approximately(slider.Max, slider.Min)
                ? 0f
                : Mathf.Clamp01((value - slider.Min) / (slider.Max - slider.Min));

            slider.Fill.anchorMax = new Vector2(t, 1f);
            slider.Fill.offsetMin = Vector2.zero;
            slider.Fill.offsetMax = Vector2.zero;

            if (slider.Knob != null)
            {
                slider.Knob.anchorMin = new Vector2(t, 0.5f);
                slider.Knob.anchorMax = new Vector2(t, 0.5f);
                slider.Knob.anchoredPosition = Vector2.zero;
            }
        }

        private void RefreshCycle(CycleSetting cycle)
        {
            if (cycle.Value == null) return;

            string text = cycle.Read();
            if (cycle.Value.text != text) cycle.Value.text = text;
        }

        private void RefreshRebind(RebindSetting rebind)
        {
            if (rebind.KeyLabel == null) return;

            // The reset row has no label to refresh; it is a button, not a readout.
            if (rebind.Action == ResetAction) return;

            // An ability key falls back to whatever the class asset says, so the box shows the
            // key the player actually presses rather than an empty slot for an unbound action.
            Key key = rebind.IsAbility
                ? KeyBindings.GetAbilityKey(ClassManager.Exists ? ClassManager.Instance.Selected : null, Key.None)
                : KeyBindings.Get(rebind.Action, KeyBindings.GetDefault(rebind.Action));

            rebind.KeyLabel.text = _listening == rebind ? "Press a key…" : PrettyKeyName(key);
            rebind.KeyLabel.color = _listening == rebind ? accentColor : textColor;
        }

        /// <summary>
        /// The Key enum's own names are already readable ("W", "LeftCtrl", "Space"), so the
        /// only tidying needed is the camelCase split the enum does not have.
        /// </summary>
        private static string PrettyKeyName(Key key)
        {
            if (key == Key.None) return "Unbound";

            string raw = key.ToString();

            var builder = new System.Text.StringBuilder(raw.Length + 4);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (i > 0 && char.IsUpper(c)) builder.Append(' ');
                builder.Append(c);
            }

            return builder.ToString();
        }

        private void RefreshHover(Vector2 pointer)
        {
            foreach (var slider in _sliders)
            {
                if (slider.Background != null)
                {
                    slider.Background.color = Hit(slider.Row, pointer) ? rowHoverColor : rowColor;
                }
            }

            foreach (var cycle in _cycles)
            {
                if (cycle.Background != null)
                {
                    cycle.Background.color = Hit(cycle.Row, pointer) ? rowHoverColor : rowColor;
                }
            }

            foreach (var rebind in _rebinds)
            {
                if (rebind.Background == null) continue;

                rebind.Background.color = Hit(rebind.Row, pointer) ? rowHoverColor : rowColor;
            }

            if (_resetTile != null && _resetTile.Background != null)
            {
                _resetTile.Background.color = Hit(_resetTile.Row, pointer) ? rowHoverColor : rowColor;
            }
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

        private Text AddLabel(RectTransform parent, string content, Vector2 position, Vector2 size,
                              int fontSize, TextAnchor anchor, Color color)
        {
            var rect = CreateRect("Label", parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
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
