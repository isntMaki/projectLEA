using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// A damageable dummy to test weapons against.
    ///
    /// Builds its own body and floating damage readout at runtime, so it can be dropped onto
    /// an empty GameObject and will just work. The readout accumulates damage dealt and
    /// flashes on each hit, which is the quickest way to see whether a weapon's numbers
    /// actually feel right.
    ///
    /// Not a player, so its death does not score - it simply resets.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TrainingTarget : MonoBehaviour
    {
        [Header("Body")]
        [Tooltip("Build a visible capsule at runtime if this object has no renderer.")]
        [SerializeField] private bool buildVisual = true;

        [Header("Damage readout")]
        [SerializeField] private bool showDamageNumber = true;

        [Tooltip("Height above the base of the target for the readout.")]
        [SerializeField] private float displayHeight = 2.2f;

        [Header("Testing")]
        [Tooltip("Press this key to clear the accumulated damage.")]
        [SerializeField] private Key resetKey = Key.T;

        [Tooltip("Seconds after death before the target stands back up.")]
        [SerializeField] private float respawnDelay = 3f;

        private Health _health;
        private TextMesh _readout;
        private Transform _readoutTransform;
        private MeshRenderer _bodyRenderer;
        private MaterialPropertyBlock _block;
        private float _accumulated;
        private float _flashUntil;
        private float _respawnAt;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _health = GetComponent<Health>();
            _block = new MaterialPropertyBlock();

            if (buildVisual && GetComponentInChildren<Renderer>() == null) BuildBody();
            _bodyRenderer = GetComponentInChildren<MeshRenderer>();

            if (showDamageNumber) BuildReadout();
        }

        private void OnEnable()
        {
            _health.OnDamaged += HandleDamaged;
            _health.OnDied += HandleDied;
        }

        private void OnDisable()
        {
            _health.OnDamaged -= HandleDamaged;
            _health.OnDied -= HandleDied;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[resetKey].wasPressedThisFrame) ResetDamage();

            if (_respawnAt > 0f && Time.time >= _respawnAt)
            {
                _respawnAt = 0f;
                _health.ResetToFull();
                ResetDamage();
            }
        }

        private void LateUpdate()
        {
            if (_readoutTransform == null) return;

            // Billboard: always face the active camera.
            var camera = Camera.main;
            if (camera == null) return;

            Vector3 direction = camera.transform.position - _readoutTransform.position;
            if (direction.sqrMagnitude > 0.0001f)
                _readoutTransform.rotation = Quaternion.LookRotation(-direction.normalized, Vector3.up);

            UpdateFlash();
        }

        // ------------------------------------------------------------------
        // Readout
        // ------------------------------------------------------------------
        private void BuildReadout()
        {
            var go = new GameObject("DamageReadout");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, displayHeight, 0f);

            _readout = go.AddComponent<TextMesh>();
            _readout.anchor = TextAnchor.MiddleCenter;
            _readout.alignment = TextAlignment.Center;
            _readout.characterSize = 0.16f;
            _readout.fontSize = 64;
            _readout.color = Color.white;
            _readout.text = "0";

            var font = GetBuiltinFont();
            if (font != null) _readout.font = font;

            _readoutTransform = go.transform;
        }

        private void HandleDamaged(DamageInfo info, float applied)
        {
            _accumulated += applied;
            if (_readout != null) _readout.text = _accumulated.ToString("0.#");

            _flashUntil = Time.time + 0.12f;
        }

        private void HandleDied(Health health)
        {
            if (_readout != null) _readout.text = $"{_accumulated:0.#}\nDOWN";

            _respawnAt = Time.time + respawnDelay;
        }

        private void ResetDamage()
        {
            _accumulated = 0f;
            if (_readout != null) _readout.text = "0";
        }

        private void UpdateFlash()
        {
            if (_bodyRenderer == null) return;

            if (Time.time < _flashUntil)
            {
                _bodyRenderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, Color.white);
                _block.SetColor(ColorId, Color.white);
                _bodyRenderer.SetPropertyBlock(_block);
            }
            else
            {
                _bodyRenderer.SetPropertyBlock(null);
            }
        }

        // ------------------------------------------------------------------
        // Body
        // ------------------------------------------------------------------
        private void BuildBody()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.zero;

            // CreatePrimitive's built-in material renders pink under URP, so swap in a Lit one.
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                var material = new Material(shader) { color = new Color(0.72f, 0.30f, 0.28f) };
                var renderer = body.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = material;
            }
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
