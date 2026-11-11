using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>
    /// First-person controller that never assumes "down" is world down.
    ///
    /// The player has a gravity direction of their own. Everything (ground check, WASD,
    /// jumping, falling, camera) is computed relative to that direction, so walking on
    /// a wall or ceiling uses exactly the same code as walking on the floor.
    ///
    /// Orientation lives in <see cref="bodyRotation"/> (yaw included, pitch separate).
    /// When gravity changes, the body rotates smoothly toward the new "up" while mouse
    /// look keeps working, so the transition is a quick roll/tilt instead of a snap.
    ///
    /// Online, only the owning client runs this simulation. Everyone else gets a
    /// kinematic copy positioned by <see cref="NetworkGravityPlayer"/>.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public class GravityPlayerController : PortalTraveller
    {
        [Header("View")]
        public Camera viewCamera;
        [Tooltip("Eye position above the capsule's center.")]
        public float eyeHeight = 0.65f;
        public float mouseSensitivity = 2f;
        public float baseFov = 80f;
        [Tooltip("Pitches with the view so other players can see where you aim.")]
        public Transform headPivot;

        [Header("Movement")]
        public float moveSpeed = 7f;
        public float sprintSpeed = 11f;
        public float groundAcceleration = 70f;
        public float airAcceleration = 20f;
        [Tooltip("Bunny hopping: jumping again on the frame you land skips ground friction, so speed carries on. " +
                 "Strafing in the air builds speed up to this (m/s); faster momentum from portals or the grapple is kept, just not added to.")]
        public float bhopSpeedCap = 20f;
        public float jumpSpeed = 9f;
        public float maxSlopeAngle = 50f;
        public float maxFallSpeed = 45f;

        [Header("Gravity")]
        public float gravityStrength = 22f;
        [Tooltip("How quickly the camera eases upright after a portal turns your view (higher = snappier).")]
        public float cameraEaseRate = 6f;
        [Tooltip("Slowest the camera eases (degrees/second), so the last few degrees don't drag on.")]
        public float cameraEaseMinSpeed = 150f;

        [Header("Crouch and slide")]
        [Tooltip("Capsule height while crouched (standing is the capsule's own height).")]
        public float crouchHeight = 1.1f;
        [Tooltip("Eye height above the crouched capsule's centre.")]
        public float crouchEyeHeight = 0.4f;
        public float crouchSpeed = 5f;
        [Tooltip("Crouching on the ground at this speed or faster starts a slide (m/s).")]
        public float slideStartSpeed = 8f;
        [Tooltip("Speed added when a slide starts (m/s), up to slideMaxBoostedSpeed.")]
        public float slideBoost = 3f;
        public float slideMaxBoostedSpeed = 15f;
        [Tooltip("How quickly a slide slows down on flat ground (m/s²). Downhill speeds you up.")]
        public float slideFriction = 4.5f;
        [Tooltip("A slide turns into a crouch-walk below this speed (m/s).")]
        public float slideEndSpeed = 4.5f;
        [Tooltip("Jumping out of a slide launches you where you're looking at this speed (m/s), like a bullet jump.")]
        public float bulletJumpSpeed = 15f;
        [Tooltip("Minimum upward speed of a bullet jump (m/s): at least a full jump's height on top of the forward launch, more when you aim up.")]
        public float bulletJumpLift = 9.5f;

        [Header("Grappling hook")]
        [Tooltip("How far the hook reaches (m).")]
        public float grappleRange = 45f;
        [Tooltip("Instant speed toward the anchor when the hook bites (m/s).")]
        public float grappleYankSpeed = 7f;
        [Tooltip("How hard the rope hauls you toward the anchor while you hold on (m/s²). Gravity is 22.")]
        public float grapplePullAcceleration = 48f;
        [Tooltip("The haul stops adding speed once you're closing on the anchor this fast (m/s).")]
        public float grapplePullSpeed = 26f;
        [Tooltip("The pull eases off this close to the anchor, so you hang there (m).")]
        public float grappleMinLength = 3f;
        [Tooltip("How hard WASD pumps your swing while hooked (m/s²).")]
        public float grappleSwingAcceleration = 12f;
        [Tooltip("Top speed while hooked (m/s); stays under the server's movement check.")]
        public float grappleMaxSpeed = 38f;

        [Header("Recoil")]
        [Tooltip("How fast the view settles back after recoil.")]
        public float recoilRecovery = 9f;

        /// <summary>Raised on the simulating side when this player passes through a portal.</summary>
        public event Action<Portal, Portal> PortalTravelled;

        CapsuleCollider capsule;
        Quaternion bodyRotation = Quaternion.identity;
        float pitch;
        Vector3 gravityDirection = Vector3.down;
        bool grounded;
        Vector3 groundNormal = Vector3.up;
        float noGroundUntil;
        float jumpBufferedUntil = -1f;
        bool jumpHeld; // holding Space jumps again the moment you land (bunny hopping)

        // Crouch / slide
        bool crouchHeld, crouching, sliding;
        float standHeight;            // the capsule's normal height
        float eyeOffset;              // camera height above the capsule centre (eased)
        float nextBulletJumpTime;
        Transform bodyMesh;
        float bodyMeshHeight;

        public bool IsCrouching => crouching;
        public bool IsSliding => sliding;
        Vector2 moveInput;
        bool sprinting;

        Vector3 spawnPosition;
        Quaternion spawnRotation;

        float fovKick;
        Color flashColor;
        float flashAlpha;
        float recoilPitch, recoilYaw;
        bool alive = true;

        // Feel
        float lastGroundedTime = -10f;
        bool jumpedSinceGrounded;
        bool wasGrounded;
        float lastFallSpeed;
        Collider groundCollider;
        StairSteps stairs;          // set while standing on a staircase
        float stairTarget, stairOffset;
        int lastStairStep = int.MinValue;
        float landDip;
        float stepDistance;
        [Tooltip("Seconds after walking off an edge during which you can still jump.")]
        public float coyoteTime = 0.12f;

        readonly RaycastHit[] hits = new RaycastHit[16];

        // Grappling hook
        bool grappling;
        Vector3 grappleAnchor;
        float ropeLength;
        float grappleReadyTime;

        /// <summary>Set by the weapon: &gt; 0 zooms the camera to this field of view (scoped).</summary>
        public float ScopeFov { get; set; }
        /// <summary>Set by the weapon: right mouse scopes, so the grapple is on Q only.</summary>
        public bool RightMouseScopes { get; set; }

        /// <summary>True while the hook is attached (on every machine, for drawing the rope).</summary>
        public bool IsGrappling => grappling;
        public Vector3 GrappleAnchor => grappleAnchor;
        /// <summary>Where the rope leaves the player: the right hand for you, the chest for others.</summary>
        public Vector3 RopeStart => IsLocallyControlled && viewCamera != null
            ? viewCamera.transform.TransformPoint(new Vector3(0.22f, -0.22f, 0.45f))
            : transform.position + (bodyRotation * Vector3.up) * 0.3f;

        public bool IsLocallyControlled { get; private set; } = true;
        public bool IsAlive => alive;
        /// <summary>Where shots come from: the eye.</summary>
        public Vector3 EyePosition => transform.position + (bodyRotation * Vector3.up) * (crouching ? crouchEyeHeight : eyeHeight);
        /// <summary>Where shots go: the view direction including recoil.</summary>
        public Quaternion AimRotation => bodyRotation * Quaternion.Euler(pitch - recoilPitch, recoilYaw, 0f);
        public float CapsuleHalfHeight => capsule.height * 0.5f;
        public float CapsuleRadius => capsule.radius;
        public override bool SimulatesLocally => IsLocallyControlled;
        public Vector3 GravityDirection => gravityDirection;
        public Vector3 Up => bodyRotation * Vector3.up;
        public bool IsGrounded => grounded;
        public Quaternion BodyRotation => bodyRotation;
        public float Pitch => pitch;

        protected override void Awake()
        {
            base.Awake();
            capsule = GetComponent<CapsuleCollider>();
            capsule.direction = 1;
            standHeight = capsule.height;
            eyeOffset = eyeHeight;
            bodyMesh = transform.Find("BodyMesh");
            if (bodyMesh != null) bodyMeshHeight = bodyMesh.localScale.y;
            capsule.sharedMaterial = new PhysicsMaterial("PlayerFrictionless")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

            Body.freezeRotation = true; // rotation is driven by bodyRotation, never by physics
            Body.linearDamping = 0f;

            bodyRotation = transform.rotation;
            gravityDirection = -(transform.up);
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;

            if (viewCamera == null) viewCamera = Camera.main;
            mouseSensitivity = PlayerPrefs.GetFloat(MainMenuUI.SensitivityKey, mouseSensitivity); // from Settings
            SetLocalControl(true, viewCamera);
            if (!Application.isBatchMode) gameObject.AddComponent<GrappleRope>();
        }

        /// <summary>Local: full simulation, input and camera. Remote: kinematic puppet.</summary>
        public void SetLocalControl(bool local, Camera cam)
        {
            IsLocallyControlled = local;
            Body.isKinematic = !local;
            // The local player smooths its own camera (see RenderPosition) so the smoothing can
            // carry through portals; Unity's built-in smoothing can't, and would blip.
            Body.interpolation = local ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            simPrevious = simCurrent = Body.position;
            Body.collisionDetectionMode = local
                ? CollisionDetectionMode.ContinuousDynamic
                : CollisionDetectionMode.ContinuousSpeculative;
            viewCamera = local ? (cam != null ? cam : Camera.main) : null;

            // Your own body is hidden from your camera (it would block the view) but
            // portal cameras still draw it, so you can see yourself through a portal.
            int localLayer = PortalView.LayerLocalPlayer;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!local)
                {
                    r.gameObject.layer = 0;
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                else if (localLayer >= 0)
                {
                    r.gameObject.layer = localLayer;
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                else
                {
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
            if (local && viewCamera != null && localLayer >= 0)
                viewCamera.cullingMask &= ~(1 << localLayer);
        }

        /// <summary>The player this machine controls (online: the owned one).</summary>
        public static GravityPlayerController Local { get; set; }

        void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        // ------------------------------------------------------------------ input + look

        void Update()
        {
            if (!IsLocallyControlled) return;

            HandleCursor();
            bool looking = Cursor.lockState == CursorLockMode.Locked;

            if (looking)
            {
                float zoom = ScopeFov > 0f ? ScopeFov / baseFov : 1f; // finer aim while scoped
                float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity * zoom;
                float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity * zoom;
                // Yaw turns around the player's own up, not the world's.
                bodyRotation = Quaternion.AngleAxis(mx, bodyRotation * Vector3.up) * bodyRotation;
                pitch = Mathf.Clamp(pitch - my, -89f, 89f);
            }

            AlignToGravity(Time.deltaTime);

            moveInput = alive ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) : Vector2.zero;
            sprinting = Input.GetKey(KeyCode.LeftShift);
            if (alive && Input.GetButtonDown("Jump")) jumpBufferedUntil = Time.time + 0.15f;
            jumpHeld = alive && Input.GetButton("Jump");
            crouchHeld = alive && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C));

            // Grappling hook: right mouse fires it, hold to hang on, let go to fly off.
            // Grappling hook: Q (always) or right mouse (unless the weapon in hand uses it to scope).
            bool grappleDown = Input.GetKeyDown(KeyCode.Q) || (!RightMouseScopes && Input.GetMouseButtonDown(1));
            bool grappleHold = Input.GetKey(KeyCode.Q) || (!RightMouseScopes && Input.GetMouseButton(1));
            if (alive && looking && grappleDown) TryGrapple();
            if (grappling && (!alive || !grappleHold)) ReleaseGrapple();

            float settle = 1f - Mathf.Exp(-recoilRecovery * Time.deltaTime);
            recoilPitch = Mathf.Lerp(recoilPitch, 0f, settle);
            recoilYaw = Mathf.Lerp(recoilYaw, 0f, settle);

            landDip = Mathf.Lerp(landDip, 0f, 1f - Mathf.Exp(-9f * Time.deltaTime));
            UpdateGravityWarning();
            fovKick = Mathf.Lerp(fovKick, 0f, 1f - Mathf.Exp(-6f * Time.deltaTime));
            flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.deltaTime * 1.2f);
        }

        void HandleCursor()
        {
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)
                && GUIUtility.hotControl == 0)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                Input.ResetInputAxes(); // don't fire on the click that grabbed the mouse
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>
        /// Eases the body's up toward the gravity "up" after a portal changes it.
        /// The direction you're looking stays fixed in the world the whole time; only the
        /// horizon rolls around it until you're upright. So the view you had through the
        /// portal never swings away, it just straightens out.
        /// </summary>
        void AlignToGravity(float dt)
        {
            Vector3 currentUp = bodyRotation * Vector3.up;
            Vector3 targetUp = -gravityDirection;
            float angle = Vector3.Angle(currentUp, targetUp);
            if (angle < 0.01f) return;

            Vector3 look = bodyRotation * Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;

            float step = Mathf.Max(angle * (1f - Mathf.Exp(-cameraEaseRate * dt)), cameraEaseMinSpeed * dt);
            Vector3 newUp;
            if (step >= angle)
            {
                newUp = targetUp;
            }
            else
            {
                Vector3 axis = Vector3.Cross(currentUp, targetUp);
                if (axis.sqrMagnitude < 1e-6f)
                {
                    // Exactly upside down: roll around the horizontal part of the view, so the
                    // up direction never swings through the look direction.
                    axis = Vector3.ProjectOnPlane(look, currentUp);
                    if (axis.sqrMagnitude < 1e-6f) axis = bodyRotation * Vector3.forward;
                }
                newUp = Quaternion.AngleAxis(step, axis.normalized) * currentUp;
            }

            // Rebuild yaw and pitch around the new up so the look direction is unchanged.
            Vector3 flat = Vector3.ProjectOnPlane(look, newUp);
            Vector3 forward = flat.sqrMagnitude > 1e-6f
                ? flat.normalized
                : Vector3.ProjectOnPlane(bodyRotation * Vector3.forward, newUp).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(bodyRotation * Vector3.up, newUp).normalized;
            bodyRotation = Quaternion.LookRotation(forward, newUp);
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(Vector3.Dot(look, newUp), -1f, 1f)) * Mathf.Rad2Deg, -89f, 89f);
        }

        // Camera smoothing: the physics position at the start of the latest physics step and
        // the one after it, blended by how far we are into that step. On a portal pass both are
        // carried through the portal, so the blend continues seamlessly on the other side.
        Vector3 simPrevious, simCurrent;

        /// <summary>Where the player is drawn this frame (smoothed between physics steps).</summary>
        public Vector3 RenderPosition
        {
            get
            {
                if (!IsLocallyControlled || Body.isKinematic) return transform.position;
                float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                return Vector3.Lerp(simPrevious, simCurrent, t);
            }
        }

        void LateUpdate()
        {
            if (headPivot != null) headPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (!IsLocallyControlled || viewCamera == null) return;

            simCurrent = Body.position; // result of the latest physics step
            Vector3 up = bodyRotation * Vector3.up;
            // On stairs the camera rides on the tread under you: it lags the smooth ramp, then
            // catches up quickly at each step edge, so climbing feels like steps.
            stairOffset = Mathf.Lerp(stairOffset, stairTarget, 1f - Mathf.Exp(-22f * Time.deltaTime));
            eyeOffset = Mathf.Lerp(eyeOffset, crouching ? crouchEyeHeight : eyeHeight, 1f - Mathf.Exp(-24f * Time.deltaTime));
            Vector3 eye = RenderPosition + up * (eyeOffset - landDip + stairOffset);
            Quaternion look = AimRotation;
            SeeThroughPortal(ref eye, ref look);
            viewCamera.transform.SetPositionAndRotation(eye, look);
            viewCamera.fieldOfView = ScopeFov > 0f ? ScopeFov : baseFov + fovKick;
        }

        /// <summary>The body goes through a portal when its center crosses, but the camera
        /// should cross when the eye does. Dropping into a floor portal the eye is still above
        /// the entrance when the center crosses, so right after the pass it sits behind the
        /// exit, inside the wall it's mounted on. While the eye is behind a portal you're
        /// passing through (inside its opening), draw it at the matching spot on the other
        /// side instead: looking through that portal, exactly as the eye would see it.</summary>
        void SeeThroughPortal(ref Vector3 eye, ref Quaternion look)
        {
            foreach (var p in PortalsPassing)
            {
                if (p == null || p.linkedPortal == null) continue;
                Vector3 e = p.transform.InverseTransformPoint(eye);
                if (e.z >= 0f || e.z < -1.5f) continue;
                if (Mathf.Abs(e.x) > p.size.x * 0.5f || Mathf.Abs(e.y) > p.size.y * 0.5f) continue;
                eye = Portal.TransferMatrix(p, p.linkedPortal).MultiplyPoint3x4(eye);
                look = Portal.TransferRotation(p, p.linkedPortal) * look;
                return;
            }
        }

        // ------------------------------------------------------------------ physics

        void FixedUpdate()
        {
            if (!IsLocallyControlled || Body.isKinematic) return;

            simPrevious = Body.position; // where this physics step starts (for camera smoothing)
            float dt = Time.fixedDeltaTime;
            Body.rotation = bodyRotation;

            Vector3 gravityUp = -gravityDirection;
            UpdateGround(bodyRotation * Vector3.up, gravityUp);

            if (grounded)
            {
                // Landing: dip the camera and thud, scaled by how hard we came down.
                if (!wasGrounded && lastFallSpeed > 4f)
                {
                    landDip = Mathf.Min(0.25f, lastFallSpeed * 0.012f);
                    Sfx.Play2D(SoundBank.Land, Mathf.Clamp01(lastFallSpeed / 18f) * 0.7f, UnityEngine.Random.Range(0.9f, 1.1f));
                }
                lastGroundedTime = Time.time;
                jumpedSinceGrounded = false;
            }
            bool justLanded = grounded && !wasGrounded;
            wasGrounded = grounded;
            bool wantsJump = Time.time <= jumpBufferedUntil || jumpHeld;
            // Bunny hop: jumping straight off a landing never touches ground friction.
            bool hop = justLanded && wantsJump;

            Vector3 velocity = Body.linearVelocity;
            Vector3 refUp = grounded ? groundNormal : gravityUp;
            Vector3 vertical = Vector3.Project(velocity, refUp);
            Vector3 lateral = velocity - vertical;

            // Crouch and slide. Crouching while moving fast on the ground (or landing fast with
            // crouch held) starts a slide; letting go stands you up once there's room.
            if (crouchHeld && !crouching)
            {
                if (SetCrouch(true) && grounded && lateral.magnitude >= slideStartSpeed) lateral = StartSlide(lateral);
            }
            else if (!crouchHeld && crouching)
            {
                SetCrouch(false); // stays crouched under a low ceiling
            }
            else if (crouching && justLanded && !sliding && lateral.magnitude >= slideStartSpeed)
            {
                lateral = StartSlide(lateral);
            }
            if (!grounded || !crouching) sliding = false;

            // WASD is relative to the body, flattened onto whatever we're standing on.
            Vector3 forward = Vector3.ProjectOnPlane(bodyRotation * Vector3.forward, refUp).normalized;
            Vector3 right = Vector3.ProjectOnPlane(bodyRotation * Vector3.right, refUp).normalized;
            Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
            float speed = crouching && grounded ? crouchSpeed : sprinting ? sprintSpeed : moveSpeed;
            Vector3 wish = (forward * input.y + right * input.x) * speed;

            if (sliding && !hop)
            {
                // Low friction, downhill speeds you up, and you can steer a little.
                stairs = null;
                stairTarget = 0f;
                lateral += Vector3.ProjectOnPlane(gravityDirection, groundNormal) * (gravityStrength * dt);
                lateral = Vector3.MoveTowards(lateral, Vector3.zero, slideFriction * dt);
                if (wish.sqrMagnitude > 0.01f)
                    lateral = Vector3.RotateTowards(lateral, wish.normalized * lateral.magnitude, 1.2f * dt, 0f);
                if (lateral.magnitude < slideEndSpeed) sliding = false; // now just crouch-walking
            }
            else if (grounded && !hop)
            {
                lateral = Vector3.MoveTowards(lateral, wish, groundAcceleration * dt);
                if (UpdateStairs(gravityUp))
                {
                    stepDistance = 0f; // on stairs, footsteps come from each step instead
                }
                else
                {
                    stepDistance += lateral.magnitude * dt;
                    if (stepDistance > (sprinting ? 2.6f : 2.1f))
                    {
                        stepDistance = 0f;
                        Sfx.Play2D(SoundBank.Footstep, 0.22f, UnityEngine.Random.Range(0.85f, 1.15f));
                    }
                }
            }
            else
            {
                stairs = null;
                stairTarget = 0f;
                if (wish.sqrMagnitude > 0.01f)
                {
                    // Air control only adds speed up to the move speed along the direction you
                    // press, and never brakes, so momentum carried out of a portal is preserved.
                    // Pressing sideways to your motion (strafing while turning) still adds a
                    // little each step, which is what builds speed when bunny hopping.
                    Vector3 wishDir = wish.normalized;
                    float current = Vector3.Dot(lateral, wishDir);
                    float before = lateral.magnitude;
                    lateral += wishDir * Mathf.Clamp(speed - current, 0f, airAcceleration * dt);
                    float after = lateral.magnitude;
                    if (after > before && after > bhopSpeedCap)
                        lateral *= Mathf.Max(before, bhopSpeedCap) / after; // gains stop at the cap
                }
            }

            // Stay planted: on the ground, never drift away from the surface (no bouncing off
            // step edges or slopes). Jumping below replaces this.
            if (grounded && Vector3.Dot(vertical, groundNormal) > 0f) vertical = Vector3.zero;

            bool canJump = (grounded || Time.time - lastGroundedTime < coyoteTime) && !jumpedSinceGrounded;
            if (canJump && wantsJump && sliding && Time.time >= nextBulletJumpTime)
            {
                // Bullet jump: out of a slide, launch where you're looking (keeping any extra
                // speed you already had that way), always with some lift off the ground.
                Vector3 aim = AimRotation * Vector3.forward;
                Vector3 launch = aim * Mathf.Max(bulletJumpSpeed, Vector3.Dot(lateral, aim));
                float lift = Vector3.Dot(launch, gravityUp);
                if (lift < bulletJumpLift) launch += gravityUp * (bulletJumpLift - lift);
                vertical = Vector3.Project(launch, refUp);
                lateral = launch - vertical;
                sliding = false;
                grounded = false;
                wasGrounded = false;
                jumpedSinceGrounded = true;
                noGroundUntil = Time.time + 0.2f;
                jumpBufferedUntil = -1f;
                nextBulletJumpTime = Time.time + 0.5f;
                fovKick += 8f;
                Sfx.Play2D(SoundBank.Jump, 0.55f, 0.7f);
                Sfx.Play2D(SoundBank.PortalWhoosh, 0.25f, 1.6f);
            }
            else if (canJump && wantsJump)
            {
                vertical = gravityUp * jumpSpeed;
                grounded = false;
                wasGrounded = false;
                jumpedSinceGrounded = true;
                noGroundUntil = Time.time + 0.15f;
                jumpBufferedUntil = -1f;
                Sfx.Play2D(SoundBank.Jump, 0.3f, UnityEngine.Random.Range(0.9f, 1.1f));
            }

            // On walkable ground, gravity presses into the surface so you don't slide down slopes.
            Vector3 gravity = (grounded ? -groundNormal : gravityDirection) * gravityStrength;
            velocity = lateral + vertical + gravity * dt;
            if (grappling) velocity = ApplyGrapple(velocity, wish, dt);

            float fallSpeed = Vector3.Dot(velocity, gravityDirection);
            if (fallSpeed > maxFallSpeed) velocity -= gravityDirection * (fallSpeed - maxFallSpeed);
            lastFallSpeed = grounded ? 0f : Mathf.Max(0f, fallSpeed);

            Body.linearVelocity = velocity;
        }

        // ------------------------------------------------------------------ crouch and slide

        Vector3 StartSlide(Vector3 lateral)
        {
            sliding = true;
            float s = lateral.magnitude;
            if (s < slideMaxBoostedSpeed) lateral *= Mathf.Min(s + slideBoost, slideMaxBoostedSpeed) / Mathf.Max(s, 0.01f);
            fovKick += 4f;
            Sfx.Play2D(SoundBank.Land, 0.35f, 0.6f);
            return lateral;
        }

        /// <summary>Shrinks or restores the capsule. On the ground your feet stay put; in the
        /// air crouching tucks your legs up (your head stays put). Standing up needs room above
        /// (or below, in the air) and returns false if there isn't any. The camera doesn't jump:
        /// it eases to the new eye height.</summary>
        bool SetCrouch(bool on)
        {
            if (on == crouching) return true;
            Vector3 up = bodyRotation * Vector3.up;
            float drop = (standHeight - crouchHeight) * 0.5f;
            Vector3 shift;
            if (on)
            {
                shift = grounded ? -up * drop : up * drop;
            }
            else
            {
                Vector3 dir = grounded ? up : -up;
                if (!HasRoom(dir, drop * 2f))
                {
                    if (grounded || !HasRoom(up, drop * 2f)) return false;
                    dir = up; // in the air with the ground right below: stand up into the space above
                }
                shift = dir * drop;
            }

            crouching = on;
            capsule.height = on ? crouchHeight : standHeight;
            if (!on) sliding = false;
            Body.position += shift;
            transform.position = Body.position;
            simPrevious += shift;
            simCurrent += shift;
            eyeOffset -= Vector3.Dot(shift, up); // keep the camera where it was; it eases from here
            ApplyCrouchVisuals();
            return true;
        }

        /// <summary>Is there room to grow the capsule by <paramref name="distance"/> along <paramref name="dir"/>?</summary>
        bool HasRoom(Vector3 dir, float distance)
        {
            float radius = capsule.radius * 0.9f;
            Vector3 origin = Body.position + dir * Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            int count = Physics.SphereCastNonAlloc(origin, radius, dir, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = hits[i].collider;
                if (IsOwnCollider(c) || IsHostIgnored(c)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue; // crates get pushed
                return false;
            }
            return true;
        }

        /// <summary>Shorter body and lower head for anyone looking (others, or you in a portal).</summary>
        void ApplyCrouchVisuals()
        {
            float scale = capsule.height / standHeight;
            if (bodyMesh != null)
            {
                var s = bodyMesh.localScale;
                s.y = bodyMeshHeight * scale;
                bodyMesh.localScale = s;
            }
            if (headPivot != null)
            {
                var p = headPivot.localPosition;
                p.y = crouching ? crouchEyeHeight : eyeHeight;
                headPivot.localPosition = p;
            }
        }

        /// <summary>Remote players: crouch the collider (the server's hit checks use it) and the
        /// body. Their position already comes from the owner, so nothing moves.</summary>
        public void SetRemoteCrouch(bool on)
        {
            if (IsLocallyControlled || on == crouching) return;
            crouching = on;
            capsule.height = on ? crouchHeight : standHeight;
            ApplyCrouchVisuals();
        }

        /// <summary>Back to standing at once (respawn, death).</summary>
        void ResetCrouch()
        {
            crouching = false;
            sliding = false;
            capsule.height = standHeight;
            eyeOffset = eyeHeight;
            ApplyCrouchVisuals();
        }

        // ------------------------------------------------------------------ grappling hook

        void TryGrapple()
        {
            if (Time.time < grappleReadyTime || Body.isKinematic) return;
            Vector3 origin = viewCamera != null ? viewCamera.transform.position : EyePosition;
            Vector3 dir = AimRotation * Vector3.forward;

            // The nearest solid level surface: not players, crates or bolts (anything with a
            // rigidbody), and not a wall you're passing through a portal in.
            int count = Physics.RaycastNonAlloc(origin, dir, hits, grappleRange, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.distance >= best || h.collider.attachedRigidbody != null || IsHostIgnored(h.collider)) continue;
                best = h.distance;
                point = h.point;
            }
            if (best == float.MaxValue)
            {
                Sfx.Play2D(SoundBank.Empty, 0.35f, 0.7f);
                return;
            }

            // The rope can't go through a portal: aiming into one (rather than at the wall
            // around it) is a miss.
            foreach (var p in Portal.All)
            {
                if (p == null || p.worldWrap) continue;
                float facing = Vector3.Dot(dir, p.Normal);
                if (facing >= -1e-4f) continue;
                float t = Vector3.Dot(p.transform.position - origin, p.Normal) / facing;
                if (t <= 0f || t >= best) continue;
                Vector3 local = p.transform.InverseTransformPoint(origin + dir * t);
                if (Mathf.Abs(local.x) <= p.size.x * 0.5f && Mathf.Abs(local.y) <= p.size.y * 0.5f)
                {
                    Sfx.Play2D(SoundBank.Empty, 0.35f, 0.7f);
                    return;
                }
            }

            grappling = true;
            grappleAnchor = point;
            ropeLength = Mathf.Max(grappleMinLength, Vector3.Distance(Body.position, point));
            // The hook bites: an instant yank toward it before the steady pull takes over.
            AddVelocity((point - Body.position).normalized * grappleYankSpeed);
            fovKick += 3f;
            Sfx.Play2D(SoundBank.Click, 0.7f, 0.5f);
            Sfx.Play2D(SoundBank.Land, 0.4f, 1.4f);
        }

        void ReleaseGrapple()
        {
            if (!grappling) return;
            grappling = false;
            grappleReadyTime = Time.time + 0.15f;
        }

        /// <summary>Remote players: just enough to draw their rope.</summary>
        public void SetRemoteGrapple(bool on, Vector3 anchor)
        {
            if (IsLocallyControlled) return;
            grappling = on;
            grappleAnchor = anchor;
        }

        /// <summary>The rope: reels in slowly and never lets you get further from the anchor than
        /// its length, so falling turns into a swing (a pendulum). WASD pumps the swing.</summary>
        Vector3 ApplyGrapple(Vector3 velocity, Vector3 wish, float dt)
        {
            Vector3 toAnchor = grappleAnchor - Body.position;
            float dist = toAnchor.magnitude;
            if (dist > grappleRange * 1.5f) { ReleaseGrapple(); return velocity; } // e.g. carried off by a knockback
            if (dist < 0.05f) return velocity;
            Vector3 dir = toAnchor / dist;

            // The rope only ever gets shorter: you can swing in closer, never drift back out.
            ropeLength = Mathf.Max(grappleMinLength, Mathf.Min(ropeLength, dist));

            // A strong, steady haul toward the anchor (well past gravity), up to the pull speed.
            // It eases off over the last few metres so you hang at the anchor instead of
            // bouncing on it. Sideways speed is left alone, so you arc round as you're pulled in.
            float ease = Mathf.Clamp01((dist - grappleMinLength) / 3f);
            float inward = Vector3.Dot(velocity, dir);
            if (inward < grapplePullSpeed)
                velocity += dir * Mathf.Min(grapplePullAcceleration * ease * dt, grapplePullSpeed - inward);

            if (!grounded && wish.sqrMagnitude > 0.01f)
                velocity += Vector3.ProjectOnPlane(wish.normalized, dir) * (grappleSwingAcceleration * dt);

            // Taut rope: never moving away from the anchor. This is what turns falling into a swing.
            inward = Vector3.Dot(velocity, dir);
            if (dist >= ropeLength - 0.05f && inward < 0f) velocity -= dir * inward;

            if (grounded && Vector3.Dot(dir, groundNormal) > 0.2f)
            {
                grounded = false; // hauled off the floor
                noGroundUntil = Time.time + 0.1f;
            }

            return Vector3.ClampMagnitude(velocity, grappleMaxSpeed);
        }

        /// <summary>On a staircase: work out the tread under you for the camera, and play a
        /// footstep each time you move onto a new step. Returns true while on stairs.</summary>
        bool UpdateStairs(Vector3 gravityUp)
        {
            if (groundCollider == null || !groundCollider.TryGetComponent(out StairSteps s))
            {
                stairs = null;
                stairTarget = 0f;
                return false;
            }

            // The ramp surface right under you (along your up), not where the capsule touches it.
            if (!groundCollider.Raycast(new Ray(Body.position, -gravityUp), out RaycastHit hit, capsule.height))
                return stairs != null;

            if (stairs != s) lastStairStep = int.MinValue;
            stairs = s;
            stairTarget = s.TreadOffset(hit.point, gravityUp);
            int step = s.StepIndex(hit.point, gravityUp);
            if (lastStairStep != int.MinValue && step != lastStairStep)
                Sfx.Play2D(SoundBank.Footstep, 0.2f, UnityEngine.Random.Range(0.9f, 1.15f));
            lastStairStep = step;
            return true;
        }

        void UpdateGround(Vector3 bodyUp, Vector3 gravityUp)
        {
            // "Moving away from the ground" is judged against the surface we were on, not
            // against gravity: walking up a 45-degree staircase moves you upward fast, but
            // you're still on it.
            Vector3 previousNormal = wasGrounded ? groundNormal : gravityUp;
            Collider previousCollider = groundCollider;
            grounded = false;
            groundCollider = null;
            groundNormal = gravityUp;
            if (Time.time < noGroundUntil) return;
            if (Vector3.Dot(Body.linearVelocity, previousNormal) > 2f) return;

            float radius = capsule.radius * 0.9f;
            float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            int count = Physics.SphereCastNonAlloc(Body.position, radius, -bodyUp, hits,
                halfSegment + 0.45f, ~0, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.distance <= 0f && h.point == Vector3.zero) continue; // started overlapping
                if (IsOwnCollider(h.collider) || IsHostIgnored(h.collider)) continue; // passing through it
                // Already on the ground: reach further, so going down stairs or over a step edge
                // keeps you planted. In the air: only count ground right below you (landing).
                if (!wasGrounded && h.distance > halfSegment + 0.12f) continue;

                Vector3 normal = h.normal;
                if (Vector3.Angle(normal, gravityUp) > maxSlopeAngle)
                {
                    // Near edges and corners a sweep reports a blended edge normal (e.g. the side
                    // of the hidden stair ramp). Ask the surface itself what's under that point.
                    if (!h.collider.Raycast(new Ray(h.point + gravityUp * 0.3f, -gravityUp), out RaycastHit surface, 0.6f))
                        continue;
                    normal = surface.normal;
                    if (Vector3.Angle(normal, gravityUp) > maxSlopeAngle) continue;
                }
                if (h.distance < best)
                {
                    best = h.distance;
                    groundNormal = normal;
                    groundCollider = h.collider;
                    grounded = true;
                }
            }

            if (grounded)
            {
                lastRealGroundTime = Time.time;
            }
            else if (wasGrounded && Time.time - lastRealGroundTime < 0.1f)
            {
                // A single missed probe at a seam or edge doesn't make you airborne.
                grounded = true;
                groundNormal = previousNormal;
                groundCollider = previousCollider;
            }
        }

        float lastRealGroundTime = -10f;

        // ------------------------------------------------------------------ portals

        public override float ExtentAlong(Vector3 direction, Quaternion rotation)
        {
            Vector3 axis = rotation * Vector3.up;
            float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            return Mathf.Abs(Vector3.Dot(axis, direction)) * halfSegment + capsule.radius;
        }

        protected override Quaternion TravelRotation => bodyRotation;

        protected override void OnTeleported(Portal from, Portal to, Quaternion newRotation)
        {
            // The view is carried through exactly (what you saw through the portal is what you
            // see now), then AlignToGravity eases the camera back upright. Seamless: no flash.
            ReleaseGrapple(); // the rope can't follow you through a portal
            bodyRotation = newRotation;
            Vector3 oldGravity = gravityDirection;
            // Your falling direction carries through the portal.
            gravityDirection = Portal.GravityAfterPass(from, to, oldGravity);
            if (Vector3.Angle(oldGravity, gravityDirection) > 1f)
                Sfx.Play2D(SoundBank.GravityShift, 0.4f, UnityEngine.Random.Range(0.95f, 1.05f));

            if (wasGrounded)
            {
                // Walked through (a doorway): stay on your feet the whole way, as if it were
                // an ordinary door. The ground you were on carries through with you.
                groundNormal = Portal.TransferRotation(from, to) * groundNormal;
            }
            else
            {
                // Fell or flew through: airborne on the other side.
                grounded = false;
                lastGroundedTime = -10f; // no coyote jump out of a portal
                noGroundUntil = Time.time + 0.1f;
            }

            // Carry the camera smoothing through the portal too, so the camera keeps gliding
            // along exactly as if the two portals were one opening (no jump, no in-between frame).
            Matrix4x4 transfer = Portal.TransferMatrix(from, to);
            simPrevious = transfer.MultiplyPoint3x4(simPrevious);
            simCurrent = transfer.MultiplyPoint3x4(simCurrent);
            PortalTravelled?.Invoke(from, to);
        }

        // ------------------------------------------------------------------ state from outside

        /// <summary>Hard-sets the simulation state (respawn, server correction).</summary>
        public void ForceState(Vector3 position, Quaternion rotation, Vector3 gravity, Vector3 velocity, float newPitch)
        {
            ClearPortalIgnores();
            ArrivalPortal = null;
            grappling = false; // a respawn or correction drops the rope
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            simPrevious = simCurrent = position; // a respawn is a deliberate cut, not a glide
            if (!Body.isKinematic) Body.linearVelocity = velocity;
            bodyRotation = rotation;
            gravityDirection = gravity.sqrMagnitude > 0f ? gravity.normalized : Vector3.down;
            pitch = newPitch;
            grounded = false;
        }

        /// <summary>Positions a remote player. <paramref name="snap"/> skips smoothing (teleports, respawns).</summary>
        public void ApplyRemoteState(Vector3 position, Quaternion rotation, float remotePitch, Vector3 gravity, bool snap)
        {
            if (snap)
            {
                Body.position = position;
                Body.rotation = rotation;
                transform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                Body.MovePosition(position);
                Body.MoveRotation(rotation);
            }
            bodyRotation = rotation;
            pitch = remotePitch;
            gravityDirection = gravity;
        }

        public void Respawn()
        {
            ForceState(spawnPosition, spawnRotation, -(spawnRotation * Vector3.up), Vector3.zero, 0f);
        }

        // ------------------------------------------------------------------ combat hooks

        /// <summary>Kicks the view (degrees). It settles back on its own.</summary>
        public void AddRecoil(float up, float side)
        {
            recoilPitch += up;
            recoilYaw += side;
        }

        public void AddVelocity(Vector3 change)
        {
            if (!IsLocallyControlled || Body.isKinematic) return;
            Body.linearVelocity += change;
            grounded = false;
            noGroundUntil = Time.time + 0.1f;
        }

        /// <summary>Dead players stop moving and can't be hit or collided with.</summary>
        public void SetAlive(bool value)
        {
            alive = value;
            if (!value) ResetCrouch(); // you respawn standing
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = value;
            if (IsLocallyControlled)
            {
                if (!value) Body.linearVelocity = Vector3.zero;
                Body.isKinematic = !value;
            }
            else
            {
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = value;
            }
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (!IsLocallyControlled) return;

            var white = Texture2D.whiteTexture;
            if (flashAlpha > 0f)
            {
                GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAlpha);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
            }

            GUI.color = Color.white;
            string gravity = DescribeGravity(gravityDirection);
            GUI.Label(new Rect(12, 10, 520, 22),
                $"Gravity: {gravity}   Speed: {Body.linearVelocity.magnitude:0.0}   {(grounded ? "Grounded" : "Airborne")}");
            if (Cursor.lockState != CursorLockMode.Locked)
                GUI.Label(new Rect(12, 32, 1600, 22), "Click to play. WASD move, Shift sprint, Space jump, Mouse fire, Right mouse or Q grapple (hold), Ctrl crouch/slide (jump while sliding to launch), R reload, 1/2/3 or wheel switch weapon (3 = sniper, right mouse scopes), Tab scores, K respawn, Esc release mouse.");
            if (TestMap.Active)
                GUI.Label(new Rect(12, 54, 400, 22), "Test map: H = back to the start");

            DrawGravityWarning();
        }

        // ------------------------------------------------------------------ gravity warning

        [Header("Gravity warning")]
        [Tooltip("How far in front of a portal the 'gravity is about to change' warning starts (m).")]
        public float warningDistance = 4f;

        Portal warningPortal;
        Vector3 warningGravity;
        float warningStrength;      // 0..1, rises as you get close
        float warningShown;         // smoothed, for fading in and out

        /// <summary>Finds the portal you're about to pass through, if going through it would
        /// change your gravity.</summary>
        void UpdateGravityWarning()
        {
            warningPortal = null;
            warningStrength = 0f;
            if (!alive) return;

            Vector3 center = Body.position;
            Vector3 velocity = Body.linearVelocity;
            float best = float.MaxValue;
            foreach (var p in Portal.All)
            {
                if (p == null || p.linkedPortal == null) continue;
                Vector3 local = p.transform.InverseTransformPoint(center);
                if (local.z < -0.3f || local.z > warningDistance) continue;
                if (Mathf.Abs(local.x) > p.size.x * 0.5f + 0.6f || Mathf.Abs(local.y) > p.size.y * 0.5f + 0.6f) continue;
                // Close enough to be about to go in, or heading toward it.
                bool approaching = Vector3.Dot(velocity, p.Normal) < -0.5f;
                if (!approaching && local.z > 1.6f) continue;

                Vector3 after = Portal.GravityAfterPass(p, p.linkedPortal, gravityDirection);
                if (Vector3.Angle(after, gravityDirection) < 1f) continue; // no change: nothing to warn about
                if (local.z < best)
                {
                    best = local.z;
                    warningPortal = p;
                    warningGravity = after;
                    warningStrength = 1f - Mathf.Clamp01(local.z / warningDistance);
                }
            }
            warningShown = Mathf.MoveTowards(warningShown, warningPortal != null ? 1f : 0f, Time.deltaTime * 4f);
        }

        void DrawGravityWarning()
        {
            if (warningShown <= 0.01f || warningPortal == null || viewCamera == null) return;
            var white = Texture2D.whiteTexture;
            float w = Screen.width, h = Screen.height, cx = w * 0.5f, cy = h * 0.5f;
            float a = warningShown * Mathf.Lerp(0.45f, 1f, warningStrength);
            Color c = warningPortal.color;

            // New "down" as seen from your camera.
            Vector3 d = Quaternion.Inverse(AimRotation) * warningGravity;
            Vector2 screenDir = new Vector2(d.x, -d.y); // GUI y points down
            string where = d.z > 0.7f ? " (ahead)" : d.z < -0.7f ? " (behind you)" : "";

            // Soft glow on the screen edge, strongest on the side the new down points to.
            float edge = 6f + 18f * warningStrength;
            GUI.color = new Color(c.r, c.g, c.b, a * 0.25f);
            GUI.DrawTexture(new Rect(0, 0, w, edge), white);
            GUI.DrawTexture(new Rect(0, h - edge, w, edge), white);
            GUI.DrawTexture(new Rect(0, 0, edge, h), white);
            GUI.DrawTexture(new Rect(w - edge, 0, edge, h), white);
            if (screenDir.sqrMagnitude > 0.04f)
            {
                Vector2 n = screenDir.normalized;
                GUI.color = new Color(c.r, c.g, c.b, a * 0.35f);
                if (n.x > 0.5f) GUI.DrawTexture(new Rect(w - edge * 2.5f, 0, edge * 2.5f, h), white);
                if (n.x < -0.5f) GUI.DrawTexture(new Rect(0, 0, edge * 2.5f, h), white);
                if (n.y > 0.5f) GUI.DrawTexture(new Rect(0, h - edge * 2.5f, w, edge * 2.5f), white);
                if (n.y < -0.5f) GUI.DrawTexture(new Rect(0, 0, w, edge * 2.5f), white);

                // Arrow from the crosshair toward the new down.
                float angle = Mathf.Atan2(n.y, n.x) * Mathf.Rad2Deg;
                var saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, new Vector2(cx, cy));
                GUI.color = new Color(c.r, c.g, c.b, a);
                GUI.DrawTexture(new Rect(cx + 34, cy - 2, 46, 4), white);           // shaft
                GUIUtility.RotateAroundPivot(35f, new Vector2(cx + 80, cy));
                GUI.DrawTexture(new Rect(cx + 66, cy - 2, 16, 4), white);           // head
                GUI.matrix = saved;
                GUIUtility.RotateAroundPivot(angle, new Vector2(cx, cy));
                GUIUtility.RotateAroundPivot(-35f, new Vector2(cx + 80, cy));
                GUI.DrawTexture(new Rect(cx + 66, cy - 2, 16, 4), white);
                GUI.matrix = saved;
            }

            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
            GUI.color = new Color(0f, 0f, 0f, a * 0.6f);
            GUI.Label(new Rect(cx - 199, cy + 61, 400, 24), $"GRAVITY SHIFT  >  {DescribeGravity(warningGravity)}{where}", style);
            GUI.color = new Color(Mathf.Lerp(c.r, 1f, 0.4f), Mathf.Lerp(c.g, 1f, 0.4f), Mathf.Lerp(c.b, 1f, 0.4f), a);
            GUI.Label(new Rect(cx - 200, cy + 60, 400, 24), $"GRAVITY SHIFT  >  {DescribeGravity(warningGravity)}{where}", style);
            GUI.color = Color.white;
        }

        public static string DescribeGravity(Vector3 g)
        {
            Vector3 a = new Vector3(Mathf.Abs(g.x), Mathf.Abs(g.y), Mathf.Abs(g.z));
            if (a.y >= a.x && a.y >= a.z) return g.y < 0f ? "Floor" : "Ceiling";
            if (a.x >= a.z) return g.x < 0f ? "West wall" : "East wall";
            return g.z < 0f ? "South wall" : "North wall";
        }
    }
}
