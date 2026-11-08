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
        public float jumpSpeed = 7.5f;
        public float maxSlopeAngle = 50f;
        public float maxFallSpeed = 45f;

        [Header("Gravity")]
        public float gravityStrength = 22f;
        [Tooltip("How quickly the body turns to a new gravity direction.")]
        public float alignSharpness = 9f;
        [Tooltip("Minimum turn speed (degrees/second) so long flips still finish quickly.")]
        public float minAlignSpeed = 240f;

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
        float landDip;
        float stepDistance;
        [Tooltip("Seconds after walking off an edge during which you can still jump.")]
        public float coyoteTime = 0.12f;

        readonly RaycastHit[] hits = new RaycastHit[16];

        public bool IsLocallyControlled { get; private set; } = true;
        public bool IsAlive => alive;
        /// <summary>Where shots come from: the eye.</summary>
        public Vector3 EyePosition => transform.position + (bodyRotation * Vector3.up) * eyeHeight;
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
            SetLocalControl(true, viewCamera);
        }

        /// <summary>Local: full simulation, input and camera. Remote: kinematic puppet.</summary>
        public void SetLocalControl(bool local, Camera cam)
        {
            IsLocallyControlled = local;
            Body.isKinematic = !local;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
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
                float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
                // Yaw turns around the player's own up, not the world's.
                bodyRotation = Quaternion.AngleAxis(mx, bodyRotation * Vector3.up) * bodyRotation;
                pitch = Mathf.Clamp(pitch - my, -89f, 89f);
            }

            AlignToGravity(Time.deltaTime);

            moveInput = alive ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) : Vector2.zero;
            sprinting = Input.GetKey(KeyCode.LeftShift);
            if (alive && Input.GetButtonDown("Jump")) jumpBufferedUntil = Time.time + 0.15f;

            float settle = 1f - Mathf.Exp(-recoilRecovery * Time.deltaTime);
            recoilPitch = Mathf.Lerp(recoilPitch, 0f, settle);
            recoilYaw = Mathf.Lerp(recoilYaw, 0f, settle);

            landDip = Mathf.Lerp(landDip, 0f, 1f - Mathf.Exp(-9f * Time.deltaTime));
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

        /// <summary>Rotates the body's up toward the gravity "up" with a smooth, fast ease.
        /// A full 180 (floor to ceiling) rolls around the view direction, so where you
        /// were looking stays where you are looking.</summary>
        void AlignToGravity(float dt)
        {
            Vector3 currentUp = bodyRotation * Vector3.up;
            Vector3 targetUp = -gravityDirection;
            float angle = Vector3.Angle(currentUp, targetUp);
            if (angle < 0.01f) return;

            float step = Mathf.Max(angle * (1f - Mathf.Exp(-alignSharpness * dt)), minAlignSpeed * dt);
            if (step >= angle)
            {
                bodyRotation = Quaternion.FromToRotation(currentUp, targetUp) * bodyRotation;
            }
            else
            {
                Vector3 axis = Vector3.Cross(currentUp, targetUp);
                if (axis.sqrMagnitude < 1e-6f) axis = bodyRotation * Vector3.forward;
                bodyRotation = Quaternion.AngleAxis(step, axis.normalized) * bodyRotation;
            }
            bodyRotation = Quaternion.Normalize(bodyRotation);
        }

        void LateUpdate()
        {
            if (headPivot != null) headPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (!IsLocallyControlled || viewCamera == null) return;

            Vector3 up = bodyRotation * Vector3.up;
            viewCamera.transform.SetPositionAndRotation(transform.position + up * (eyeHeight - landDip), AimRotation);
            viewCamera.fieldOfView = baseFov + fovKick;
        }

        // ------------------------------------------------------------------ physics

        void FixedUpdate()
        {
            if (!IsLocallyControlled || Body.isKinematic) return;

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
            wasGrounded = grounded;

            Vector3 velocity = Body.linearVelocity;
            Vector3 refUp = grounded ? groundNormal : gravityUp;
            Vector3 vertical = Vector3.Project(velocity, refUp);
            Vector3 lateral = velocity - vertical;

            // WASD is relative to the body, flattened onto whatever we're standing on.
            Vector3 forward = Vector3.ProjectOnPlane(bodyRotation * Vector3.forward, refUp).normalized;
            Vector3 right = Vector3.ProjectOnPlane(bodyRotation * Vector3.right, refUp).normalized;
            Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
            float speed = sprinting ? sprintSpeed : moveSpeed;
            Vector3 wish = (forward * input.y + right * input.x) * speed;

            if (grounded)
            {
                lateral = Vector3.MoveTowards(lateral, wish, groundAcceleration * dt);
                stepDistance += lateral.magnitude * dt;
                if (stepDistance > (sprinting ? 2.6f : 2.1f))
                {
                    stepDistance = 0f;
                    Sfx.Play2D(SoundBank.Footstep, 0.22f, UnityEngine.Random.Range(0.85f, 1.15f));
                }
            }
            else if (wish.sqrMagnitude > 0.01f)
            {
                // Air control only adds speed up to the move speed and never brakes,
                // so momentum carried out of a portal is preserved.
                Vector3 wishDir = wish.normalized;
                float current = Vector3.Dot(lateral, wishDir);
                lateral += wishDir * Mathf.Clamp(speed - current, 0f, airAcceleration * dt);
            }

            bool canJump = (grounded || Time.time - lastGroundedTime < coyoteTime) && !jumpedSinceGrounded;
            if (canJump && Time.time <= jumpBufferedUntil)
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

            float fallSpeed = Vector3.Dot(velocity, gravityDirection);
            if (fallSpeed > maxFallSpeed) velocity -= gravityDirection * (fallSpeed - maxFallSpeed);
            lastFallSpeed = grounded ? 0f : Mathf.Max(0f, fallSpeed);

            Body.linearVelocity = velocity;
        }

        void UpdateGround(Vector3 bodyUp, Vector3 gravityUp)
        {
            grounded = false;
            groundNormal = gravityUp;
            if (Time.time < noGroundUntil) return;
            if (Vector3.Dot(Body.linearVelocity, gravityUp) > 2f) return; // moving away from the ground

            float radius = capsule.radius * 0.95f;
            float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            int count = Physics.SphereCastNonAlloc(Body.position, radius, -bodyUp, hits,
                halfSegment + 0.12f, ~0, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.distance <= 0f && h.point == Vector3.zero) continue; // started overlapping
                if (IsOwnCollider(h.collider) || IsHostIgnored(h.collider)) continue; // passing through it
                if (Vector3.Angle(h.normal, gravityUp) > maxSlopeAngle) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    groundNormal = h.normal;
                    grounded = true;
                }
            }
        }

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
            bodyRotation = newRotation;
            Vector3 oldGravity = gravityDirection;
            gravityDirection = -to.Normal; // gravity now points into the exit portal's surface
            if (Vector3.Angle(oldGravity, gravityDirection) > 1f)
                Sfx.Play2D(SoundBank.GravityShift, 0.4f, UnityEngine.Random.Range(0.95f, 1.05f));
            grounded = false;
            wasGrounded = false;
            lastGroundedTime = -10f; // no coyote jump out of a portal
            noGroundUntil = Time.time + 0.1f;

            fovKick = 14f;
            flashColor = to.color;
            flashAlpha = 0.35f;
            PortalTravelled?.Invoke(from, to);
        }

        // ------------------------------------------------------------------ state from outside

        /// <summary>Hard-sets the simulation state (respawn, server correction).</summary>
        public void ForceState(Vector3 position, Quaternion rotation, Vector3 gravity, Vector3 velocity, float newPitch)
        {
            ClearPortalIgnores();
            ArrivalPortal = null;
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
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
                GUI.Label(new Rect(12, 32, 900, 22), "Click to play. WASD move, Shift sprint, Space jump, Mouse fire, R reload, 1/2 or wheel switch weapon, Tab scores, K respawn, Esc release mouse.");
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
