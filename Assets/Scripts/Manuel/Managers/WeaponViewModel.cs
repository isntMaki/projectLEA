using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// The visible half of <see cref="WeaponUser"/>: it puts the equipped weapon's model in the
    /// player's hands.
    ///
    /// <see cref="WeaponUser"/> is deliberately all timing and damage - it never touches a
    /// mesh. This component is the mirror image: it listens to the same
    /// <see cref="WeaponManager.OnWeaponEquipped"/> event and does nothing but swap the
    /// instantiated viewmodel, so the two concerns stay separate and either can change without
    /// the other noticing.
    ///
    /// The model is parented to a socket on the first-person camera, so it rides the look
    /// rotation and the camera feel (bob, sway, landing dip) for free. It never carries a
    /// collider of its own: the raycast for a shot still comes out of <see cref="WeaponUser"/>'s
    /// attack origin, so a huge sniper model can never block its own bullet.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponViewModel : MonoBehaviour
    {
        [Tooltip("Where the gun is held. A child of the first-person camera; the match's camera " +
                 "feel drives it.")]
        [SerializeField] private Transform viewModelSocket;

        [Tooltip("Lower the gun while reloading, so the reload reads as a real action rather " +
                 "than the model freezing mid-air.")]
        [SerializeField] private float reloadDip = 0.12f;

        [Tooltip("How fast the reload dip moves. Frame-rate independent.")]
        [SerializeField] private float dipSpeed = 9f;

        private GameObject _instance;
        private Vector3 _baseLocalPosition;
        private bool _subscribed;

        /// <summary>The socket this component swaps models under. Falls back to the main camera,
        /// then to the player's own camera child - the same resolution order as WeaponUser's
        /// attack origin, so a missing MainCamera tag cannot leave the gun parented to the
        /// player's feet pointing at a fixed heading.</summary>
        public Transform Socket
        {
            get
            {
                if (viewModelSocket != null) return viewModelSocket;
                if (Camera.main != null) return Camera.main.transform;
                var own = GetComponentInChildren<Camera>();
                return own != null ? own.transform : transform;
            }
        }

        private void Start()
        {
            Subscribe();
            Equip(WeaponManager.Exists ? WeaponManager.Instance.Equipped : null);

            if (viewModelSocket != null) _baseLocalPosition = viewModelSocket.localPosition;
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed || !WeaponManager.Exists) return;
            WeaponManager.Instance.OnWeaponEquipped += HandleWeaponEquipped;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || !WeaponManager.Exists) return;
            WeaponManager.Instance.OnWeaponEquipped -= HandleWeaponEquipped;
            _subscribed = false;
        }

        private void HandleWeaponEquipped(int index, WeaponDefinition weapon)
        {
            Equip(weapon);
        }

        /// <summary>Replaces the held model. Null clears the hands.</summary>
        private void Equip(WeaponDefinition weapon)
        {
            if (_instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }

            var prefab = weapon != null ? weapon.viewmodelPrefab : null;
            if (prefab == null) return;

            var socket = Socket;
            _instance = Instantiate(prefab, socket.position, socket.rotation, socket);

            // Bake the hand position per weapon into the prefab instead of here, so one socket
            // serves the whole arsenal and a new gun is placed by its own bake, not by a tune.
            var definition = weapon;
            _instance.transform.localPosition = definition != null
                ? definition.viewmodelOffset
                : Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;

            // A freshly spawned model inherits nothing from the socket's scale, and a baked
            // prefab is already in world units, so leave it at one.
            _instance.transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (viewModelSocket == null) return;

            // The reload dip. WeaponUser owns the reload timer, so this just reads it and
            // moves the socket - no duplicate state, and no way for the two to disagree about
            // whether a reload is happening.
            var weaponUser = GetComponent<WeaponUser>();
            bool reloading = weaponUser != null && weaponUser.IsReloading;

            float target = reloading ? _baseLocalPosition.y - reloadDip : _baseLocalPosition.y;
            float t = 1f - Mathf.Exp(-dipSpeed * Time.deltaTime);

            var p = viewModelSocket.localPosition;
            p.y = Mathf.Lerp(p.y, target, t);
            p.x = _baseLocalPosition.x;
            p.z = _baseLocalPosition.z;
            viewModelSocket.localPosition = p;
        }
    }
}
