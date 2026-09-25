using ProjectLEA.Manuel.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Turns the equipped <see cref="WeaponDefinition"/> into actual attacks.
    ///
    /// This is the consumer the weapon data was missing: it reads delay, windup, range,
    /// swing arc, ammo, fire mode, pellet spread, damage falloff and hit-zone multipliers
    /// off whatever is equipped and drives the whole attack cycle from them. Melee sweeps an
    /// arc, everything else casts a projectile line, and a launcher adds splash on impact.
    ///
    /// FIRE MODES: a Semi weapon fires once per click, an Auto weapon keeps firing while the
    /// button is held, and a Burst weapon spends several rounds per click on its own faster
    /// clock. Any of the three can also be the right-click alt fire.
    ///
    /// HIT ZONES: damage is scaled by where on the body the shot lands, which is read from
    /// the height of the hit point inside the target's collider bounds rather than from
    /// separate head and leg colliders. That keeps it working on any body, including the
    /// network proxy whose geometry is identical, without needing extra colliders on the
    /// model - and it means the shooter's machine computes the final amount before the hit
    /// is relayed, so both machines agree on exactly how much it was.
    ///
    /// Animation is deliberately not wired here - this is the timing and damage layer. A
    /// future animator should hook the same windup so the visuals match.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponUser : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Primary attack. Melee and guns both use this for now.")]
        [SerializeField] private bool useMouseForAttack = true;

        [Tooltip("Key that starts a reload. Overridden by the player's key binding when one is set.")]
        [SerializeField] private Key reloadKey = Key.R;

        [Header("Aiming")]
        [Tooltip("Where attacks originate. Defaults to the main camera.")]
        [SerializeField] private Transform attackOrigin;

        [Tooltip("Which layers attacks can hit.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Thickness of the projectile sweep. Keeps fast shots from tunnelling.")]
        [SerializeField] private float projectileRadius = 0.08f;

        [Header("Rules")]
        [Tooltip("Auto-reload when the magazine runs dry.")]
        [SerializeField] private bool autoReload = true;

        [Tooltip("Log every attack and hit.")]
        [SerializeField] private bool logAttacks;

        [Tooltip("Hits above this fraction of the target's body height count as headshots.")]
        [Range(0.5f, 0.95f)]
        [SerializeField] private float headshotFromHeight = 0.78f;

        [Tooltip("Hits below this fraction of the target's body height count as leg shots.")]
        [Range(0.1f, 0.6f)]
        [SerializeField] private float legshotBelowHeight = 0.45f;

        // --- Public state, for a future HUD -------------------------------
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public bool IsReloading => Time.time < _reloadEndsAt;
        public float ReloadProgress =>
            IsReloading ? 1f - Mathf.Clamp01((_reloadEndsAt - Time.time) / Mathf.Max(0.01f, CurrentWeapon?.reloadTime ?? 1f)) : 1f;

        /// <summary>Seconds until the next attack is allowed. 0 when ready.</summary>
        public float CooldownRemaining => Mathf.Max(0f, _nextAttackAt - Time.time);

        /// <summary>True during the windup, which is the opponent's window to react.</summary>
        public bool IsWindingUp => _attackPending;

        /// <summary>True while a burst is still spending its rounds.</summary>
        public bool IsBursting => _burstRemaining > 0;

        public WeaponDefinition CurrentWeapon =>
            WeaponManager.Exists ? WeaponManager.Instance.Equipped : null;

        /// <summary>
        /// Multiplier on the time between shots, written by buff abilities. 1 = the weapon's
        /// own rate; 1.5 = 50% faster. Reset to 1 when the buff expires.
        /// </summary>
        public float FireRateMultiplier { get; set; } = 1f;

        private float AttackDelay(WeaponDefinition weapon) =>
            Mathf.Max(0.01f, weapon.delay * Mathf.Max(0.05f, FireRateMultiplier));

        /// <summary>
        /// Where shots come from. The serialized field wins when it is set; otherwise the main
        /// camera is looked up, and finally the player's own camera child - which is what saves
        /// a match where nobody tagged the camera MainCamera, since the whole scene would
        /// otherwise fire silently forever (that was a real bug: every shot logged "no attack
        /// origin" because the camera was Untagged).
        ///
        /// Resolved on every shot rather than cached in Awake, because on a network client the
        /// body and its camera can be spawned well after this component's Awake ran.
        /// </summary>
        private Transform AttackOrigin
        {
            get
            {
                if (attackOrigin != null) return attackOrigin;

                var main = Camera.main;
                if (main != null) { attackOrigin = main.transform; return attackOrigin; }

                var own = GetComponentInChildren<Camera>();
                if (own != null) attackOrigin = own.transform;

                return attackOrigin;
            }
        }

        private float _nextAttackAt;
        private float _windupEndsAt;
        private float _reloadEndsAt;
        private bool _attackPending;
        private bool _pendingAlt;
        private int _burstRemaining;
        private float _burstNextAt;
        private bool _subscribed;
        private int _lastWeaponIndex = -1;

        private void Awake()
        {
            // Resolved eagerly so the inspector-assigned origin wins immediately, but the
            // property below re-resolves on use - a client's camera can appear after this
            // component wakes up, and a missing MainCamera tag must not permanently disarm the
            // gun. See AttackOrigin.
            _ = AttackOrigin;
        }

        private void Start()
        {
            Subscribe();
            RefreshAmmoForCurrentWeapon();
        }

        // Must override-free: this component has no base class, but it must still clean up
        // its subscription or the WeaponManager will hold a dead reference.
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
            _lastWeaponIndex = index;
            RefreshAmmoForCurrentWeapon();
            _nextAttackAt = 0f;
            _attackPending = false;
            _burstRemaining = 0;
            _reloadEndsAt = 0f;
        }

        private void RefreshAmmoForCurrentWeapon()
        {
            var weapon = CurrentWeapon;
            if (weapon == null || !weapon.UsesAmmo)
            {
                Magazine = 0;
                Reserve = 0;
                return;
            }

            Magazine = weapon.magazineSize;
            Reserve = weapon.reserveAmmo;
        }

        private void Update()
        {
            var weapon = CurrentWeapon;

            // Picking a different weapon is handled by the event, but this covers the case
            // where one was equipped before this component existed.
            if (WeaponManager.Exists && WeaponManager.Instance.EquippedIndex != _lastWeaponIndex)
            {
                _lastWeaponIndex = WeaponManager.Instance.EquippedIndex;
                RefreshAmmoForCurrentWeapon();
            }

            if (weapon == null) return;

            // --- Reload ------------------------------------------------
            if (IsReloading)
            {
                if (Time.time >= _reloadEndsAt) FinishReload(weapon);
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[KeyBindings.Get("Reload", reloadKey)].wasPressedThisFrame)
                BeginReload(weapon);

            // Clicks while the buy menu is up are menu clicks. The shop freezes match time, but
            // Update still runs, so without this every shop row you clicked would also fire the
            // gun you were trying to sell.
            if (LoadoutUI.IsOpenNow) return;

            // A burst in progress fires its own rounds on its own clock, which is faster
            // than the weapon's normal shot delay.
            if (_burstRemaining > 0)
            {
                if (Time.time >= _burstNextAt) FireBurstRound(weapon);
                return;
            }

            // --- Windup resolving into the hit --------------------------
            if (_attackPending)
            {
                if (Time.time >= _windupEndsAt) ResolveAttack(weapon);
                return;
            }

            // --- New attack ---------------------------------------------
            if (Time.time < _nextAttackAt) return;

            bool primary = AttackPressed();
            bool alt = !primary && AltPressed(weapon);
            if (!primary && !alt) return;
            if (!HasAmmoToFire(weapon)) return;

            BeginAttack(weapon, alt);
        }

        /// <summary>
        /// The primary trigger. Automatic weapons read the held state so they keep firing;
        /// everything else fires once per click.
        /// </summary>
        private bool AttackPressed()
        {
            if (!useMouseForAttack) return false;

            var mouse = Mouse.current;
            if (mouse == null) return false;

            if (CurrentWeapon != null && CurrentWeapon.IsAutomatic)
                return mouse.leftButton.isPressed;

            return mouse.leftButton.wasPressedThisFrame;
        }

        /// <summary>The alternate trigger, which is always one shot per click.</summary>
        private bool AltPressed(WeaponDefinition weapon)
        {
            if (!useMouseForAttack || weapon == null || !weapon.HasAltFire) return false;

            var mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
        }

        private bool HasAmmoToFire(WeaponDefinition weapon)
        {
            if (!weapon.UsesAmmo) return true;
            if (weapon.magazineSize <= 0) return true;   // single-use, no magazine to track

            if (Magazine > 0) return true;

            // Dry: try a reload rather than silently doing nothing.
            if (autoReload && Reserve > 0) BeginReload(weapon);
            else if (logAttacks) Debug.Log("[WeaponUser] Out of ammo.");

            return false;
        }

        private void BeginAttack(WeaponDefinition weapon, bool alt)
        {
            if (weapon.UsesAmmo && weapon.magazineSize > 0) Magazine--;

            _attackPending = true;
            _pendingAlt = alt;

            bool heavySwing = alt && weapon.altFire == AltFire.HeavySwing;
            _windupEndsAt = Time.time + (heavySwing ? weapon.altWindup : weapon.windup);

            if (logAttacks)
                Debug.Log($"[WeaponUser] {weapon.displayName}{(alt ? " alt" : "")} " +
                          $"winding up ({_windupEndsAt - Time.time:0.##}s).");
        }

        private void ResolveAttack(WeaponDefinition weapon)
        {
            _attackPending = false;
            _nextAttackAt = Time.time + AttackDelay(weapon);

            var origin = AttackOrigin;
            if (origin == null)
            {
                Debug.LogWarning("[WeaponUser] No attack origin, no main camera and no camera " +
                                 "child on the player; cannot resolve the hit.");
                return;
            }

            Vector3 originPos = origin.position;
            Vector3 forward = origin.forward;

            if (!weapon.IsRanged)
            {
                ResolveMelee(weapon, originPos, forward, _pendingAlt);
                return;
            }

            FireOneShot(weapon, originPos, forward, _pendingAlt);

            // A burst spends its remaining rounds after the first, on the burst clock.
            bool burst = weapon.fireMode == FireMode.Burst ||
                         (_pendingAlt && weapon.altFire == AltFire.Burst);
            if (burst)
            {
                int count = _pendingAlt ? weapon.altBurstCount : weapon.burstCount;
                _burstRemaining = Mathf.Max(0, count - 1);
                _burstNextAt = Time.time + BurstInterval(weapon, _pendingAlt);
            }
        }

        private static float BurstInterval(WeaponDefinition weapon, bool alt)
        {
            return alt ? weapon.altBurstInterval : weapon.burstInterval;
        }

        /// <summary>One round of a burst. Ammo is checked again, so a dry burst stops early.</summary>
        private void FireBurstRound(WeaponDefinition weapon)
        {
            var origin = AttackOrigin;
            if (origin == null) { _burstRemaining = 0; return; }

            if (weapon.UsesAmmo && weapon.magazineSize > 0 && Magazine <= 0)
            {
                _burstRemaining = 0;
                if (autoReload && Reserve > 0) BeginReload(weapon);
                return;
            }

            if (weapon.UsesAmmo && weapon.magazineSize > 0) Magazine--;

            FireOneShot(weapon, origin.position, origin.forward, _pendingAlt);

            _burstRemaining--;
            if (_burstRemaining > 0)
                _burstNextAt = Time.time + BurstInterval(weapon, _pendingAlt);
        }

        /// <summary>One trigger pull's worth of projectiles - one bullet, or a spread of pellets.</summary>
        private void FireOneShot(WeaponDefinition weapon, Vector3 origin, Vector3 forward, bool alt)
        {
            int pellets = Mathf.Max(1, weapon.pelletsPerShot);

            float spread = weapon.pelletSpread;
            if (alt && weapon.altFire == AltFire.TightSpread)
                spread *= weapon.altSpreadScale;

            for (int i = 0; i < pellets; i++)
            {
                Vector3 direction = spread > 0f ? ConeDirection(forward, spread) : forward;
                ResolveRanged(weapon, origin, direction, alt);
            }
        }

        /// <summary>
        /// A direction somewhere inside a cone of the given half-angle. The radius is square
        /// rooted so the pellets spread evenly across the disc instead of piling up at the
        /// centre, and the azimuth is uniform.
        /// </summary>
        private static Vector3 ConeDirection(Vector3 forward, float halfAngleDegrees)
        {
            float radians = halfAngleDegrees * Mathf.Deg2Rad;
            float radius = Mathf.Sqrt(Random.value);
            float cos = Mathf.Cos(radians * radius);
            float sin = Mathf.Sin(radians * radius);
            float azimuth = Random.value * Mathf.PI * 2f;

            // Need two axes perpendicular to the firing direction. Straight up and down is
            // degenerate, so fall back to a horizontal reference there.
            Vector3 reference = Mathf.Abs(forward.y) > 0.99f ? Vector3.right : Vector3.up;
            Vector3 tangent = Vector3.Cross(forward, reference).normalized;
            Vector3 bitangent = Vector3.Cross(forward, tangent).normalized;

            Vector3 offset = (tangent * Mathf.Cos(azimuth) + bitangent * Mathf.Sin(azimuth)) * sin;
            return (forward * cos + offset).normalized;
        }

        // ------------------------------------------------------------------
        // Hit detection
        // ------------------------------------------------------------------
        private void ResolveMelee(WeaponDefinition weapon, Vector3 origin, Vector3 forward, bool alt)
        {
            int hits = 0;
            float halfArc = weapon.swingArc * 0.5f;

            bool heavySwing = alt && weapon.altFire == AltFire.HeavySwing;
            float damage = heavySwing ? weapon.AltSwingDamage : weapon.damage;

            Vector3 flatForward = forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = transform.forward;

            var colliders = Physics.OverlapSphere(origin, weapon.range, hitMask, QueryTriggerInteraction.Ignore);

            foreach (var collider in colliders)
            {
                if (IsPartOfSelf(collider)) continue;

                var health = collider.GetComponentInParent<Health>();
                if (health == null) continue;

                Vector3 toTarget = health.transform.position - origin;
                toTarget.y = 0f;

                if (toTarget.sqrMagnitude > weapon.range * weapon.range) continue;
                if (weapon.swingArc < 360f && Vector3.Angle(flatForward, toTarget) > halfArc) continue;

                ApplyHit(health, weapon, origin, toTarget.normalized, damage);
                hits++;
            }

            if (logAttacks && hits > 0)
                Debug.Log($"[WeaponUser] {weapon.displayName} hit {hits} target(s).");
        }

        private int ResolveRanged(WeaponDefinition weapon, Vector3 origin, Vector3 forward, bool alt)
        {
            if (!Physics.SphereCast(origin, projectileRadius, forward, out var hit, weapon.range,
                                    hitMask, QueryTriggerInteraction.Ignore))
            {
                return 0;
            }

            if (IsPartOfSelf(hit.collider)) return 0;

            var health = hit.collider.GetComponentInParent<Health>();
            if (health == null) return 0;

            // Distance decides how much of the damage survives, then where the shot landed
            // decides the head or leg multiplier.
            float amount = weapon.damage
                         * weapon.DamageRatioAtDistance(hit.distance)
                         * ZoneMultiplier(weapon, hit.collider, hit.point);

            ApplyHit(health, weapon, hit.point, forward, amount);

            int hits = 1;

            // A launcher damages everything in the blast, including whoever it landed on.
            if (weapon.IsExplosive && weapon.splashRadius > 0f)
                hits += ApplySplash(weapon, hit.point, health);

            return hits;
        }

        /// <summary>
        /// The head or leg multiplier, from how far up the target's body the shot landed. The
        /// bands are fractions of the collider's bounds so they work on any body without
        /// needing extra colliders - when the real models arrive, swap this for hitbox
        /// colliders and this falls away.
        /// </summary>
        private float ZoneMultiplier(WeaponDefinition weapon, Collider collider, Vector3 point)
        {
            if (collider == null) return 1f;

            Bounds bounds = collider.bounds;
            float height = bounds.size.y;
            if (height < 0.1f) return 1f;

            float fraction = Mathf.InverseLerp(bounds.min.y, bounds.max.y, point.y);

            if (fraction >= headshotFromHeight) return weapon.headshotMultiplier;
            if (fraction <= legshotBelowHeight) return weapon.legshotMultiplier;
            return 1f;
        }

        /// <summary>Area damage around a blast. The direct target is excluded so it is not hit twice.</summary>
        private int ApplySplash(WeaponDefinition weapon, Vector3 centre, Health directTarget)
        {
            int hits = 0;

            var colliders = Physics.OverlapSphere(centre, weapon.splashRadius, hitMask, QueryTriggerInteraction.Ignore);

            foreach (var collider in colliders)
            {
                if (IsPartOfSelf(collider)) continue;

                var health = collider.GetComponentInParent<Health>();
                if (health == null || health == directTarget) continue;

                // Linear falloff from full damage at the centre to a quarter at the edge.
                float distance = Vector3.Distance(centre, health.transform.position);
                float falloff = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(distance / weapon.splashRadius));

                var info = DamageInfo.Explosion(weapon.damage * falloff, centre, gameObject, weapon, weapon.splashRadius);

                // A blast that catches the opponent's proxy relays just like a direct hit does.
                if (TryRelayRemoteHit(health, info)) continue;

                health.ApplyDamage(info);
                hits++;
            }

            return hits;
        }

        private void ApplyHit(Health health, WeaponDefinition weapon, Vector3 point, Vector3 direction, float amount)
        {
            var info = DamageInfo.Direct(amount, point, direction, gameObject, weapon);

            // The opponent's body is a proxy: its real Health lives on their machine, so
            // applying the hit here would be discarded the moment the next transform arrived.
            if (TryRelayRemoteHit(health, info)) return;

            float applied = health.ApplyDamage(info);

            if (logAttacks && applied > 0f)
                Debug.Log($"[WeaponUser] Dealt {applied:0.#} to {health.name} with {weapon.displayName}.");
        }

        /// <summary>
        /// Sends a hit to the machine that owns the body it landed on. Returns true when the
        /// damage was relayed and must NOT be applied locally - the local copy of that body is
        /// a network proxy whose Health is a stand-in, and the owner's copy is the real one.
        ///
        /// Deliberately shares one implementation with the ability archetypes, so a gunshot and
        /// an ability rocket follow the same rule about who applies the damage.
        /// </summary>
        private bool TryRelayRemoteHit(Health health, DamageInfo info)
        {
            bool relayed = ProjectLEA.Manuel.Abilities.AbilityAim.RelayIfProxy(health, info);

            if (relayed && logAttacks)
                Debug.Log($"[WeaponUser] Relayed {info.amount:0.#} to {health.name}'s owner over the network.");

            return relayed;
        }

        private bool IsPartOfSelf(Collider collider)
        {
            if (collider == null) return true;
            return collider.transform == transform || collider.transform.IsChildOf(transform);
        }

        // ------------------------------------------------------------------
        // Reloading
        // ------------------------------------------------------------------
        private void BeginReload(WeaponDefinition weapon)
        {
            if (!weapon.UsesAmmo || weapon.magazineSize <= 0) return;
            if (Reserve <= 0 || Magazine >= weapon.magazineSize) return;

            // A reload cancels anything mid-swing or mid-burst.
            _attackPending = false;
            _burstRemaining = 0;

            _reloadEndsAt = Time.time + weapon.reloadTime;
            if (logAttacks) Debug.Log($"[WeaponUser] Reloading {weapon.displayName} ({weapon.reloadTime:0.##}s).");
        }

        private void FinishReload(WeaponDefinition weapon)
        {
            _reloadEndsAt = 0f;

            int needed = weapon.magazineSize - Magazine;
            int taken = Mathf.Min(needed, Reserve);

            Magazine += taken;
            Reserve -= taken;

            if (logAttacks) Debug.Log($"[WeaponUser] Reloaded - {Magazine}/{Reserve}.");
        }
    }
}
