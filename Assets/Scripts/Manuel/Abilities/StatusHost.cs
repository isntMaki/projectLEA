using ProjectLEA.Manuel.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// Owns the temporary status effects an ability can put on a player: slowed, suppressed,
    /// blinded, pulled. Lives on the player's body.
    ///
    /// THE MIRRORING RULE: a mirrored zone applies a status only to the body its OWN machine is
    /// authoritative for. So the slow, the flash and the suppress you suffer are always applied
    /// by your own machine, which is also the machine that owns your real movement and your real
    /// camera - there is no way for the two copies to disagree about whether you were flashed.
    /// The pull is the same idea expressed as a force on the local body.
    ///
    /// Everything here is a debuff; buffs are handled by the ability scripts themselves through
    /// the same public surface the movement controller already exposes.
    /// </summary>
    [DisallowMultipleComponent]
    public class StatusHost : MonoBehaviour
    {
        [Header("Flash")]
        [Tooltip("Peak opacity of the blind overlay, 0 to 1.")]
        [Range(0.1f, 1f)] [SerializeField] private float flashPeak = 0.92f;

        [Tooltip("How much of the flash duration the overlay stays near full before it fades.")]
        [Range(0.05f, 0.9f)] [SerializeField] private float flashHoldRatio = 0.35f;

        [Header("Pull")]
        [Tooltip("How quickly a pull force bleeds off, in m/s per second.")]
        [SerializeField] private float pullDecay = 12f;

        private PlayerController _controller;

        // --- Slow -------------------------------------------------------
        private float _slowFactor = 1f;
        private float _slowEndsAt = -1f;

        // --- Suppress ---------------------------------------------------
        private float _suppressEndsAt = -1f;

        // --- Flash ------------------------------------------------------
        private float _flashEndsAt = -1f;
        private float _flashDuration = 1f;
        private Image _flashOverlay;
        private Canvas _flashCanvas;

        /// <summary>Multiplier for the local player's walk speed while slowed. 1 = unaffected.</summary>
        public float SpeedMultiplier => Active(_slowEndsAt) ? Mathf.Clamp01(_slowFactor) : 1f;

        /// <summary>True while abilities cannot be cast.</summary>
        public bool Suppressed => Active(_suppressEndsAt);

        /// <summary>True while the player is blinded.</summary>
        public bool IsFlashing => Active(_flashEndsAt);

        private static bool Active(float endsAt) => endsAt > 0f && Time.time < endsAt;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            // The controller reads this every frame, so a slow that ends this frame is picked
            // up immediately without the ability having to clean it up.
            if (_controller != null)
                _controller.ExternalSpeedMultiplier = SpeedMultiplier;

            UpdateFlash();
        }

        // ------------------------------------------------------------------
        // Applied by zones
        // ------------------------------------------------------------------

        /// <summary>
        /// Slows the player. Re-applying refreshes the duration and takes the stronger effect,
        /// because stacking two slows should never make the player faster than one.
        /// </summary>
        /// <param name="factor">Speed multiplier for the duration. 0.5 = half speed.</param>
        public void ApplySlow(float factor, float duration)
        {
            if (duration <= 0f) return;

            _slowFactor = Mathf.Min(SpeedMultiplier, Mathf.Clamp01(factor));
            _slowEndsAt = Time.time + duration;
        }

        /// <summary>Blocks ability casts for the duration.</summary>
        public void ApplySuppress(float duration)
        {
            if (duration <= 0f) return;

            _suppressEndsAt = Mathf.Max(_suppressEndsAt, Time.time + duration);
        }

        /// <summary>Blinds the player. The overlay holds then fades over the duration.</summary>
        public void ApplyFlash(float duration)
        {
            if (duration <= 0f) return;

            _flashDuration = duration;
            _flashEndsAt = Time.time + duration;
            EnsureFlashOverlay();
        }

        /// <summary>Pushes the local body towards a point, as a pull would.</summary>
        public void ApplyPull(Vector3 towards, float speed)
        {
            if (_controller == null) return;
            if (speed <= 0f) return;

            Vector3 horizontal = towards - transform.position;
            horizontal.y = 0f;
            if (horizontal.sqrMagnitude < 0.01f) return;

            _controller.ExternalForce += horizontal.normalized * speed;
        }

        // ------------------------------------------------------------------
        // Flash overlay
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the blind overlay on demand. It is a screen-space canvas with a single white
        /// image stretched over it, built from Texture2D.whiteTexture so it never depends on a
        /// sprite asset being present.
        /// </summary>
        private void EnsureFlashOverlay()
        {
            if (_flashOverlay != null) return;

            var canvasObject = new GameObject("FlashOverlay", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);

            _flashCanvas = canvasObject.GetComponent<Canvas>();
            _flashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _flashCanvas.sortingOrder = 500;

            var rect = (RectTransform)canvasObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _flashOverlay = canvasObject.AddComponent<Image>();

            var sprite = Sprite.Create(Texture2D.whiteTexture,
                                       new Rect(0f, 0f, 4f, 4f), Vector2.zero, 100f);
            _flashOverlay.sprite = sprite;
            _flashOverlay.color = new Color(1f, 1f, 1f, 0f);
            _flashOverlay.raycastTarget = false;

            canvasObject.SetActive(false);
        }

        private void UpdateFlash()
        {
            if (_flashCanvas == null) return;

            if (!IsFlashing)
            {
                if (_flashCanvas.gameObject.activeSelf) _flashCanvas.gameObject.SetActive(false);
                return;
            }

            if (!_flashCanvas.gameObject.activeSelf) _flashCanvas.gameObject.SetActive(true);

            float elapsed = _flashDuration - (_flashEndsAt - Time.time);
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _flashDuration));

            // Hold near peak, then ease out - a flat fade from full reads as a glitch.
            float alpha;
            if (t < flashHoldRatio)
                alpha = Mathf.Lerp(flashPeak * 0.6f, flashPeak, t / Mathf.Max(0.01f, flashHoldRatio));
            else
                alpha = Mathf.Lerp(flashPeak, 0f,
                                   (t - flashHoldRatio) / Mathf.Max(0.01f, 1f - flashHoldRatio));

            _flashOverlay.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        private void OnDisable()
        {
            // Never leave a blind up if the component is torn down mid-flash.
            if (_flashCanvas != null) _flashCanvas.gameObject.SetActive(false);
        }
    }
}
