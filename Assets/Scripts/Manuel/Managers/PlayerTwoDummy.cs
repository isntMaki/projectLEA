using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Stands in for the second player until there is a real one.
    ///
    /// It is a damageable body registered as <see cref="PlayerSlot.Two"/>, so the round loop
    /// treats it exactly like a player: it respawns at its spawn point, its death scores a
    /// kill for player one, and it shows up in the round logic. Nothing here is dummy-specific
    /// except the fact that it never moves on its own.
    ///
    /// Press M to make it die, once there is a real second player this component goes away.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [DisallowMultipleComponent]
    public class PlayerTwoDummy : MonoBehaviour
    {
        [Header("Body")]
        [Tooltip("Build a visible capsule if this object has no renderer.")]
        [SerializeField] private bool buildVisual = true;

        [Tooltip("The character model to wear. Leave empty to fall back to the placeholder " +
                 "capsule. The baked bean prefab stands on its own origin, so it goes at the " +
                 "capsule's centre and the capsule stays where the hit zone was.")]
        [SerializeField] private GameObject bodyPrefab;

        [Tooltip("Recolour the body to bodyColor. Off for the beans: they ship with their own " +
                 "materials (skin, eyes, pupils) and a single flat tint would paint the eyes " +
                 "too. Team colours will tint the skin material only, when TDM needs them.")]
        [SerializeField] private bool tintBody = false;

        [SerializeField] private Color bodyColor = new Color(0.78f, 0.32f, 0.30f);

        private Health _health;
        private bool _registered;

        private void Awake()
        {
            _health = GetComponent<Health>();

            if (buildVisual && GetComponentInChildren<Renderer>() == null) BuildBody();
        }

        private void Start()
        {
            Register();
        }

        private void Update()
        {
            // The managers may not have existed yet when Start ran, so keep trying until it
            // sticks rather than silently ending up with an unregistered opponent.
            if (!_registered) Register();
        }

        private void Register()
        {
            if (!RoundManager.Exists) return;

            RoundManager.Instance.RegisterPlayer(PlayerSlot.Two, transform);
            _registered = true;

            Debug.Log("[PlayerTwoDummy] Registered as player two.");
        }

        /// <summary>
        /// The body is visual only: the player capsule already has a CharacterController and
        /// a second collider on the dummy is what weapons should hit, so it stays.
        ///
        /// The collider is added here rather than baked into the prefab because the LOCAL
        /// player wears the same prefab and must stay collider-free - a second capsule next
        /// to its CharacterController is what flings a player across the arena.
        /// </summary>
        private void BuildBody()
        {
            if (bodyPrefab != null)
            {
                var body = Instantiate(bodyPrefab, transform, false);
                body.name = "Body";

                // Weapons raycast and read GetComponentInParent<Health>, and the hit zone
                // multiplier reads the collider's bounds, so this is what makes the other
                // player damageable at all.
                //
                // The bean stands on its own origin while this object's origin is the capsule
                // centre, so the collider is centred on the body's middle and the body is
                // dropped by exactly that much - otherwise the bean floats a metre up or the
                // hit zone sits a metre below it.
                var capsule = body.GetComponent<CapsuleCollider>();
                if (capsule == null) capsule = body.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.9f, 0f);
                capsule.height = 1.8f;
                capsule.radius = 0.4f;
                body.transform.localPosition = new Vector3(0f, -capsule.center.y, 0f);

                if (tintBody) TintBody(body);
                return;
            }

            var body2 = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body2.name = "Body";
            body2.transform.SetParent(transform, false);
            body2.transform.localPosition = Vector3.zero;

            // CreatePrimitive's built-in material renders pink under URP, so swap in a Lit one.
            TintBody(body2);
        }

        /// <summary>
        /// Recolours every renderer under the body to <see cref="bodyColor"/>, on cloned
        /// materials so the shared prefab the local player wears is untouched.
        /// </summary>
        private void TintBody(GameObject body)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = new Material(shader) { color = bodyColor };
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = bodyColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 1f);
        }
    }
}
