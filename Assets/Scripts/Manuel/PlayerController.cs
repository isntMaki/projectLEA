using ProjectLEA.Manuel.Managers;
using ProjectLEA.Manuel.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectLEA.Manuel
{
    /// <summary>
    /// First-person movement controller tuned to Valorant-style gunplay movement.
    /// WASD move, mouse look, Space jump, Ctrl crouch - defaults, all of them rebindable
    /// through the settings menu, which is why none of them are read as literal keys here.
    /// There is no slide. Uses the new Input System (com.unity.inputsystem) reading devices
    /// directly, so no .inputactions binding setup is required.
    ///
    /// Movement feel: Valorant's movement is built around instant, repeatable peaks
    /// and near-instant stops, so the gunplay stays aimable. Walk speed is a brisk
    /// 5.4 m/s, crouch drops to about 55% of that, and deceleration is deliberately
    /// enormous so releasing the keys plants the player almost immediately. Air
    /// authority is partial: a jump can be curved, but not reversed.
    ///
    /// The camera-feel layer (head bob, strafe lean, landing dip, eased crouch eye
    /// height) is preserved on top of that, so the body still reads as planted.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Run speed in meters per second. Valorant's knife-out speed is about 5.4; guns slow you below it.")]
        [SerializeField] private float moveSpeed = 5.4f;

        [Tooltip("How fast the player reaches run speed from a standstill. Higher = snappier.")]
        [SerializeField] private float acceleration = 16f;

        [Tooltip("How fast the player sheds speed when the keys are released. Deliberately huge: " +
                 "Valorant stops are near-instant so the player can shoot immediately.")]
        [SerializeField] private float deceleration = 90f;

        [Tooltip("How fast the player can change direction while airborne (lower = more committed jumps).")]
        [SerializeField] private float airControl = 0.45f;

        [Tooltip("How quickly an external force (an ability pull) bleeds off, in m/s per second.")]
        [SerializeField] private float pullDecay = 12f;

        [Header("Jumping")]
        [Tooltip("How high the player can jump in meters. Valorant's jump clears a head-high crate, not a building.")]
        [SerializeField] private float jumpHeight = 1f;

        [Tooltip("Gravity applied to the player. Unity default is -9.81. More negative = less floaty.")]
        [SerializeField] private float gravity = -22f;

        [Tooltip("Extra downward force applied when falling, so descents feel heavy rather than floaty.")]
        [SerializeField] private float fallMultiplier = 1.6f;

        [Tooltip("Largest upward camera dip applied on landing, in meters.")]
        [SerializeField] private float landingDip = 0.12f;

        [Tooltip("How quickly the landing dip recovers. Higher = quicker spring back.")]
        [SerializeField] private float landingDipRecovery = 8f;

        [Header("Crouch (Ctrl)")]
        [Tooltip("Movement speed while crouching, in meters per second. Roughly 55% of run speed, as in Valorant.")]
        [SerializeField] private float crouchSpeed = 3f;

        [Tooltip("Character height while crouching. Must be smaller than the standing height.")]
        [SerializeField] private float crouchHeight = 1.1f;

        [Tooltip("How fast the controller shrinks/grows when crouching. Higher = snappier.")]
        [SerializeField] private float crouchTransitionSpeed = 14f;

        [Header("Mouse Look")]
        [Tooltip("Mouse sensitivity for looking around.")]
        [SerializeField] private float mouseSensitivity = 0.15f;

        [Tooltip("Maximum up/down look angle in degrees.")]
        [SerializeField] private float lookClamp = 85f;

        [Tooltip("Transform that pitches up/down. Defaults to the camera if left empty.")]
        [SerializeField] private Transform cameraPivot;

        [Header("Body Placeholder")]
        [Tooltip("Renderer of the visible placeholder body. Hidden from the local first-person camera so it does not block the view.")]
        [SerializeField] private Renderer bodyRenderer;

        [Header("Camera Feel (head bob / sway)")]
        [Tooltip("Vertical bob height in meters while walking.")]
        [SerializeField] private float bobAmplitude = 0.05f;

        [Tooltip("Horizontal sway in meters while walking.")]
        [SerializeField] private float swayAmplitude = 0.04f;

        [Tooltip("Bob cycles per second at run speed. Scales down as you move slower.")]
        [SerializeField] private float bobFrequency = 2.1f;

        [Tooltip("Sideways camera roll (degrees) when strafing. Keeps the body feeling planted.")]
        [SerializeField] private float strafeTilt = 1.2f;

        [Tooltip("How quickly the camera sway responds. Higher = snappier. Frame-rate independent.")]
        [SerializeField] private float swayDamping = 14f;

        [Tooltip("How much the bob shrinks while crouched (0 = none, 1 = fully suppressed).")]
        [Range(0f, 1f)]
        [SerializeField] private float crouchBobSuppression = 0.35f;

        [Header("Camera Feel (footsteps)")]
        [Tooltip("Horizontal distance between footstep events in meters.")]
        [SerializeField] private float footstepStrideLength = 2.1f;

        [Tooltip("Multiplier applied to stride spacing while crouched (shorter steps).")]
        [SerializeField] private float crouchStrideMultiplier = 0.6f;

        // --- Cached components / rig ---
        private CharacterController _controller;
        private Transform _pivot;
        private float _standingHeight;
        private Vector3 _standingCenter;
        private float _cameraStandingY;
        private float _cameraCrouchY;

        // --- Input state ---
        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private bool _crouchHeld;

        // --- Movement state ---
        private float _pitch;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private bool _isGrounded;
        private bool _wasGrounded;

        // --- Camera feel state ---
        private float _bobPhase;
        private float _swayX;
        private float _swayY;
        private float _lean;
        private float _landingOffset;
        private float _strideAccumulator;
        private float _cameraBaseY;

        /// <summary>True while the player is crouched.</summary>
        public bool IsCrouching { get; private set; }

        /// <summary>True while the character is on the ground (smoothed to avoid flicker on stairs).</summary>
        public bool IsGrounded => _isGrounded;

        /// <summary>Current horizontal speed in meters per second. Useful for animation and audio.</summary>
        public float CurrentSpeed => _horizontalVelocity.magnitude;

        /// <summary>Raised each time a footstep should play. Hook audio here later.</summary>
        public event System.Action OnFootstep;

        // --- External control surface (driven by PlayerCombat) -------------

        /// <summary>Raw movement input this frame (x = strafe, y = forward), before rotation to world space.</summary>
        public Vector2 MoveInput => _moveInput;

        /// <summary>
        /// Multiplier applied to horizontal movement speed. Blocking halves it and a stun
        /// slows it further. Other systems write this; the controller only reads it.
        /// </summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// Gravity after the low-gravity cheat. Everything vertical reads this rather than the
        /// raw field, so the cheat needs exactly two call sites: the jump impulse and the
        /// falling integration.
        /// </summary>
        private float EffectiveGravity =>
            MatchCheats.lowGravity ? gravity * MatchCheats.LowGravityScale : gravity;

        /// <summary>
        /// Launches the player vertically without taking horizontal control, so an updraft
        /// behaves like a jump rather than like a dash.
        /// </summary>
        public void ApplyExternalVertical(float upwardVelocity)
        {
            _verticalVelocity = upwardVelocity;
        }

        /// <summary>
        /// While true an external system (the dash burst) owns horizontal motion, so the
        /// normal input-driven movement is skipped. Gravity still applies, so dashing off a
        /// ledge behaves sensibly.
        /// </summary>
        public bool ExternalMotionLock { get; set; }

        /// <summary>
        /// Multiplier on horizontal speed applied by an external system - currently the slow
        /// from an ability zone. Written by <see cref="ProjectLEA.Manuel.Abilities.StatusHost"/>
        /// every frame; the controller only reads it, and it is separate from
        /// <see cref="SpeedMultiplier"/> so a class's own multiplier and a debuff never fight
        /// over the same field.
        /// </summary>
        public float ExternalSpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// Additive horizontal force applied this frame by an external system - currently the
        /// pull from an ability zone. The controller integrates and decays it, so an ability
        /// only has to add to it.
        /// </summary>
        public Vector3 ExternalForce { get; set; }

        /// <summary>
        /// Movement speed granted by a buff (an ability, not the class's own stat). Written by
        /// the buff archetypes, reset to 1 when they expire. Kept separate from
        /// <see cref="SpeedMultiplier"/> (the class) and <see cref="ExternalSpeedMultiplier"/>
        /// (a debuff) so the three never clobber one another.
        /// </summary>
        public float BuffSpeedMultiplier { get; set; } = 1f;

        /// <summary>Horizontal velocity applied while <see cref="ExternalMotionLock"/> is true.</summary>
        public Vector3 ExternalVelocity { get; set; }

        /// <summary>
        /// When false, look and movement input are ignored entirely.
        ///
        /// This matters for menus: setting Time.timeScale to 0 stops movement, but mouse
        /// delta is NOT scaled by timeScale, so the view would keep swinging around behind
        /// an open menu unless the input is suppressed here.
        /// </summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>Mouse look sensitivity. Exposed so the settings menu can change it.</summary>
        public float MouseSensitivity
        {
            get => mouseSensitivity;
            set => mouseSensitivity = Mathf.Clamp(value, 0.01f, 1f);
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _pivot = cameraPivot != null ? cameraPivot : (Camera.main != null ? Camera.main.transform : transform);

            // Remember the authored standing dimensions so we can restore them exactly.
            _standingHeight = _controller.height;
            _standingCenter = _controller.center;

            if (_pivot != null)
            {
                _cameraStandingY = _pivot.localPosition.y;
                // The camera sits this far below the top of the standing capsule.
                float eyeFromTop = (_standingHeight * 0.5f + _standingCenter.y) - _cameraStandingY;
                // Keep the same relationship to the top of the (shorter) crouched capsule,
                // so the eye always stays inside it regardless of authored heights.
                _cameraCrouchY = (crouchHeight * 0.5f) - eyeFromTop;
                _cameraBaseY = _cameraStandingY;
            }

            // Lock the cursor so mouse look works without leaving the window.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // The body is the player's own. Keep it out of the local first-person view but let
            // it cast a shadow, so the player still reads as a physical presence in the world
            // (and remote players would see it).
            //
            // The referenced renderer stands in for the whole body: the character is several
            // separate parts, and any part left at full shadow casting would fill the camera.
            // The held weapon is parented to the camera, not the body, so it is untouched.
            if (bodyRenderer != null)
            {
                foreach (var part in bodyRenderer.transform.GetComponentsInChildren<Renderer>())
                {
                    part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
            }
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            ReadInput();
            UpdateLook();
            UpdateCrouch();
            UpdateMovement();
            UpdateCameraFeel();
        }

        /// <summary>Reads WASD, jump, crouch and mouse deltas from the active devices.</summary>
        private void ReadInput()
        {
            _moveInput = Vector2.zero;
            _lookInput = Vector2.zero;
            _crouchHeld = false;

            // A menu owns the input while it is open - otherwise the view keeps turning,
            // because mouse delta ignores Time.timeScale.
            if (!InputEnabled) return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[KeyBindings.Get("Move Forward", Key.W)].isPressed) _moveInput.y += 1f;
                if (keyboard[KeyBindings.Get("Move Back", Key.S)].isPressed) _moveInput.y -= 1f;
                if (keyboard[KeyBindings.Get("Move Right", Key.D)].isPressed) _moveInput.x += 1f;
                if (keyboard[KeyBindings.Get("Move Left", Key.A)].isPressed) _moveInput.x -= 1f;

                var crouch = KeyBindings.Get("Crouch", Key.LeftCtrl);
                _crouchHeld = keyboard[crouch].isPressed || keyboard[MirrorCtrl(crouch)].isPressed;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                _lookInput = mouse.delta.ReadValue() * mouseSensitivity;
            }
        }

        /// <summary>
        /// Either Ctrl works for crouch, whichever one the binding names - a player who rebound
        /// it to left Ctrl should not have to also remember which hand to use.
        /// </summary>
        private static Key MirrorCtrl(Key key)
        {
            if (key == Key.LeftCtrl) return Key.RightCtrl;
            if (key == Key.RightCtrl) return Key.LeftCtrl;
            return key;
        }

        /// <summary>Applies yaw to the body and pitch to the camera pivot.</summary>
        private void UpdateLook()
        {
            // Yaw: rotate the whole player around world up.
            transform.Rotate(Vector3.up, _lookInput.x, Space.World);

            // Pitch: clamp the camera pivot so the player cannot flip over.
            _pitch = Mathf.Clamp(_pitch - _lookInput.y, -lookClamp, lookClamp);
        }

        /// <summary>
        /// Handles crouch (hold Ctrl). The capsule shrinks while crouched and refuses to
        /// grow back through a low ceiling.
        /// </summary>
        private void UpdateCrouch()
        {
            bool wantCrouch = _crouchHeld;

            // Do not stand up if there is something directly overhead.
            if (!wantCrouch && IsCrouching && !CanStandUp())
            {
                wantCrouch = true;
            }

            IsCrouching = wantCrouch;
            ApplyCrouchHeight();
        }

        /// <summary>
        /// Smoothly resizes the controller capsule for crouch.
        /// Camera positioning is owned entirely by UpdateCameraFeel, which composes the
        /// crouch height with the bob, sway and landing dip.
        /// </summary>
        private void ApplyCrouchHeight()
        {
            float targetHeight = IsCrouching ? crouchHeight : _standingHeight;
            float newHeight = Mathf.MoveTowards(_controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);

            if (!Mathf.Approximately(newHeight, _controller.height))
            {
                // Keep the capsule's feet planted on the ground while it shrinks.
                float feetY = _controller.center.y - (_controller.height * 0.5f);
                _controller.height = newHeight;
                _controller.center = new Vector3(_standingCenter.x, feetY + (newHeight * 0.5f), _standingCenter.z);
            }
        }

        /// <summary>Casts upward to check whether the player has room to stand.</summary>
        private bool CanStandUp()
        {
            float radius = _controller.radius * 0.95f;
            Vector3 origin = transform.position + _controller.center;
            // Allow for skin width so we do not detect the ground or our own capsule.
            float castDistance = _standingHeight - _controller.height;

            return !Physics.SphereCast(origin, radius, Vector3.up, out _, castDistance + _controller.skinWidth,
                ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Moves the character with gravity, jumping and crouch speed.</summary>
        private void UpdateMovement()
        {
            _wasGrounded = _isGrounded;
            _isGrounded = _controller.isGrounded;

            // Convert input (x = strafe, y = forward) into world-space direction.
            Vector3 wishDirection = (transform.right * _moveInput.x + transform.forward * _moveInput.y);
            if (wishDirection.sqrMagnitude > 1f) wishDirection.Normalize();

            if (ExternalMotionLock)
            {
                // An external system (the dodge burst) owns horizontal motion this frame.
                _horizontalVelocity = ExternalVelocity;
            }
            else
            {
                float speed = (IsCrouching ? crouchSpeed : moveSpeed)
                              * Mathf.Max(0f, SpeedMultiplier)
                              * Mathf.Max(0f, ExternalSpeedMultiplier)
                              * Mathf.Max(0f, BuffSpeedMultiplier);
                if (MatchCheats.superSpeed) speed *= MatchCheats.SuperSpeedScale;
                Vector3 targetVelocity = wishDirection * speed;

                // Grounded uses separate accel/decel so stops are immediate; the air uses
                // reduced authority so jumps stay committal but can still be curved.
                float rate;
                if (_isGrounded)
                {
                    bool wantsToMove = wishDirection.sqrMagnitude > 0.0001f;
                    rate = wantsToMove ? acceleration : deceleration;
                }
                else
                {
                    rate = acceleration * airControl;
                }

                _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, rate * Time.deltaTime);
            }

            if (_isGrounded)
            {
                // Small downward push keeps the controller glued to slopes/ground.
                if (_verticalVelocity < 0f) _verticalVelocity = -2f;

                var keyboard = Keyboard.current;
                // No jumping while crouched - Valorant keeps crouch and jump as separate
                // options. None while an external system owns the motion (e.g. mid-dodge),
                // and none while a menu owns the input.
                if (InputEnabled && !ExternalMotionLock && keyboard != null &&
                    keyboard[KeyBindings.Get("Jump", Key.Space)].wasPressedThisFrame && !IsCrouching)
                {
                    _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * EffectiveGravity);
                }
            }
            else
            {
                // Stronger pull when already falling removes the floaty apex hang-time.
                float g = _verticalVelocity < 0f ? EffectiveGravity * fallMultiplier : EffectiveGravity;
                _verticalVelocity += g * Time.deltaTime;
            }

            Vector3 motion = _horizontalVelocity + Vector3.up * _verticalVelocity;

            // A pull or knockback adds to the motion this frame, then bleeds off. Applied after
            // the input velocity so it can never be cancelled by a change of direction.
            if (ExternalForce.sqrMagnitude > 0.0001f)
            {
                motion += ExternalForce;
                ExternalForce = Vector3.MoveTowards(ExternalForce, Vector3.zero,
                                                    pullDecay * Time.deltaTime);
            }

            _controller.Move(motion * Time.deltaTime);

            // Detect the landing frame so the camera can dip.
            if (!_wasGrounded && _controller.isGrounded)
            {
                // Scale the dip by how hard we hit.
                float impact = Mathf.Clamp01(Mathf.Abs(_verticalVelocity) / 10f);
                _landingOffset = -landingDip * Mathf.Max(0.4f, impact);
            }
        }

        /// <summary>
        /// Applies the head-bob, strafe lean and landing dip directly to the camera pivot.
        /// This is the layer that makes the body read as planted on the ground.
        /// All smoothing is frame-rate independent (exponential decay, not raw Lerp).
        /// </summary>
        private void UpdateCameraFeel()
        {
            if (_pivot == null) return;

            float speed = _horizontalVelocity.magnitude;
            bool moving = speed > 0.15f && _isGrounded;

            // Normalised speed drives both the bob rate and how far it swings.
            float speedRatio = moveSpeed > 0.0001f ? Mathf.Clamp01(speed / moveSpeed) : 0f;

            // Suppress the bob while crouched so low movement stays stable.
            float bobScale = speedRatio * (IsCrouching ? (1f - crouchBobSuppression) : 1f);

            // Advance the bob phase. Scaled by speed so the step rhythm tracks the motion.
            if (moving) _bobPhase += Time.deltaTime * bobFrequency * Mathf.PI * 2f * speedRatio;

            // Two vertical bounces per stride, one sideways sway per stride.
            float targetSwayY = moving ? Mathf.Sin(_bobPhase * 2f) * bobAmplitude * bobScale : 0f;
            float targetSwayX = moving ? Mathf.Cos(_bobPhase) * swayAmplitude * speedRatio : 0f;

            // Frame-rate independent smoothing: 1 - exp(-k*dt) converges correctly at any fps.
            float smooth = 1f - Mathf.Exp(-swayDamping * Time.deltaTime);
            _swayY = Mathf.Lerp(_swayY, targetSwayY, smooth);
            _swayX = Mathf.Lerp(_swayX, targetSwayX, smooth);

            // Strafe lean: roll the camera slightly into the direction of travel.
            float targetLean = -_moveInput.x * strafeTilt * speedRatio;
            _lean = Mathf.Lerp(_lean, targetLean, smooth);

            // Landing dip springs back to zero.
            _landingOffset = Mathf.MoveTowards(_landingOffset, 0f, landingDipRecovery * Time.deltaTime);

            // Smoothly ease the eye height between standing and crouching while the capsule resizes.
            float targetBaseY = IsCrouching ? _cameraCrouchY : _cameraStandingY;
            _cameraBaseY = Mathf.MoveTowards(_cameraBaseY, targetBaseY, crouchTransitionSpeed * Time.deltaTime);

            // Compose: eased crouch/stand height + bob + landing dip.
            Vector3 p = _pivot.localPosition;
            p.x = _swayX;
            p.y = _cameraBaseY + _swayY + _landingOffset;
            _pivot.localPosition = p;

            // Pitch (from look) combined with the sway roll.
            _pivot.localRotation = Quaternion.Euler(_pitch, 0f, _lean);

            // Footstep pacing: fire an event every stride length of travel.
            if (moving)
            {
                float stride = footstepStrideLength * (IsCrouching ? crouchStrideMultiplier : 1f);
                _strideAccumulator += speed * Time.deltaTime;
                if (_strideAccumulator >= stride)
                {
                    _strideAccumulator -= stride;
                    OnFootstep?.Invoke();
                }
            }
            else
            {
                // Reset so the first step after stopping lands immediately.
                _strideAccumulator = footstepStrideLength * (IsCrouching ? crouchStrideMultiplier : 1f);
            }
        }
    }
}
