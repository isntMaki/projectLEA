using System.Collections.Generic;
using ProjectLEA.Manuel.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// The main menu: PLAY, SETTINGS, QUIT.
    ///
    /// Built entirely in code so the MainMenu scene only needs a single GameObject. It also
    /// makes sure a Camera, a SettingsMenu and a LobbyUI exist, so the same settings panel
    /// and the same lobby are reused here rather than being duplicated.
    ///
    /// Clicks are hit-tested by hand, the same as the shop and the settings panel, so no
    /// EventSystem is needed anywhere in the project.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1200f);

        [SerializeField] private int titleFontSize = 72;
        [SerializeField] private int subtitleFontSize = 22;
        [SerializeField] private int buttonFontSize = 30;
        [SerializeField] private int hintFontSize = 18;

        [SerializeField] private float buttonWidth = 420f;
        [SerializeField] private float buttonHeight = 72f;
        [SerializeField] private float buttonSpacing = 18f;

        [SerializeField] private Color backdropColor = new Color(0.04f, 0.05f, 0.07f, 1f);
        [SerializeField] private Color buttonColor = new Color(1f, 1f, 1f, 0.07f);
        [SerializeField] private Color buttonHoverColor = new Color(0.20f, 0.78f, 0.62f, 0.85f);
        [SerializeField] private Color textColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        [SerializeField] private Color dimColor = new Color(0.60f, 0.64f, 0.70f, 1f);
        [SerializeField] private Color accentColor = new Color(0.20f, 0.78f, 0.62f, 1f);

        private class MenuButton
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public System.Action OnClick;
        }

        private readonly List<MenuButton> _buttons = new List<MenuButton>();

        private GameObject _canvasObject;
        private Font _font;
        private SettingsMenu _settings;
        private LobbyUI _lobby;

        private float _uiScale = 1f;
        private int _builtWidth;
        private int _builtHeight;

        private float S(float v) => v * _uiScale;
        private int F(int v) => Mathf.Max(1, Mathf.RoundToInt(v * _uiScale));

        private void Awake()
        {
            _font = GetBuiltinFont();

            EnsureCamera();
            EnsureSettings();
            EnsureLobby();

            ComputeScale();
            Build();
        }

        private void EnsureLobby()
        {
            _lobby = GetComponent<LobbyUI>();
            if (_lobby == null) _lobby = gameObject.AddComponent<LobbyUI>();
        }

        private void Update()
        {
            if (Screen.width != _builtWidth || Screen.height != _builtHeight) Rebuild();

            // The settings panel and the lobby each own the pointer while they are open.
            if (_settings != null && _settings.IsOpen) return;
            if (_lobby != null && _lobby.IsOpen) return;

            HandlePointer();
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------
        private void EnsureCamera()
        {
            if (Camera.main != null) return;

            var go = new GameObject("Main Camera", typeof(Camera));
            go.tag = "MainCamera";

            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = backdropColor;
        }

        private void EnsureSettings()
        {
            _settings = GetComponent<SettingsMenu>();
            if (_settings == null) _settings = gameObject.AddComponent<SettingsMenu>();
        }

        private void ComputeScale()
        {
            _uiScale = Mathf.Max(0.2f, Mathf.Min(Screen.width / referenceResolution.x,
                                                Screen.height / referenceResolution.y));
            _builtWidth = Screen.width;
            _builtHeight = Screen.height;
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------
        private void HandlePointer()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pointer = mouse.position.ReadValue();

            foreach (var button in _buttons)
            {
                bool hovered = Hit(button.Rect, pointer);
                button.Background.color = hovered ? buttonHoverColor : buttonColor;
                button.Label.color = hovered ? backdropColor : textColor;

                if (hovered && mouse.leftButton.wasPressedThisFrame) button.OnClick?.Invoke();
            }
        }

        private static bool Hit(RectTransform rect, Vector2 pointer)
        {
            return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null);
        }

        /// <summary>
        /// PLAY no longer drops you into a match. It opens the lobby, where you create a
        /// session or join one on your network - the match only starts once two players
        /// are in and the host says go.
        /// </summary>
        private void Play()
        {
            if (_lobby != null) _lobby.Open();
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------
        private void Rebuild()
        {
            if (_canvasObject != null) Destroy(_canvasObject);
            _buttons.Clear();

            ComputeScale();
            Build();
        }

        private void Build()
        {
            _canvasObject = new GameObject("MainMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.transform.SetParent(transform, false);

            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            var root = (RectTransform)_canvasObject.transform;

            var backdrop = CreateRect("Backdrop", root);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;
            backdrop.gameObject.AddComponent<Image>().color = backdropColor;

            AddLabel(root, "SHOOT A BEAN", S(0f), S(300f), S(1200f), S(90f),
                     F(titleFontSize), accentColor, TextAnchor.MiddleCenter);

            AddLabel(root, "arena combat", S(0f), S(228f), S(1200f), S(32f),
                     F(subtitleFontSize), dimColor, TextAnchor.MiddleCenter);

            float y = S(60f);

            AddButton(root, "PLAY", y, Play); y -= S(buttonHeight + buttonSpacing);
            AddButton(root, "SETTINGS", y, OpenSettings); y -= S(buttonHeight + buttonSpacing);
            AddButton(root, "QUIT", y, Quit);

            AddLabel(root, "ESC opens settings in game - the Controls tab rebinds every key",
                     S(0f), -S(520f), S(1200f), S(28f),
                     F(hintFontSize), dimColor, TextAnchor.MiddleCenter);
        }

        private void OpenSettings()
        {
            if (_settings != null) _settings.SetOpen(true);
        }

        private void AddButton(RectTransform parent, string caption, float y, System.Action onClick)
        {
            var rect = CreateRect(caption, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(S(buttonWidth), S(buttonHeight));
            rect.anchoredPosition = new Vector2(0f, y);

            var background = rect.gameObject.AddComponent<Image>();
            background.color = buttonColor;

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
            text.color = textColor;
            text.raycastTarget = false;

            _buttons.Add(new MenuButton
            {
                Rect = rect,
                Background = background,
                Label = text,
                OnClick = onClick
            });
        }

        private void AddLabel(RectTransform parent, string content, float x, float y,
                              float width, float height, int fontSize, Color color, TextAnchor anchor)
        {
            var rect = CreateRect("Label", parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);

            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
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
