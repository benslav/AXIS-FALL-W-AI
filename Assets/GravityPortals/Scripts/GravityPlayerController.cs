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
        [Tooltip("How quickly the camera eases upright after a portal turns your view (higher = snappier).")]
        public float cameraEaseRate = 6f;
        [Tooltip("Slowest the camera eases (degrees/second), so the last few degrees don't drag on.")]
        public float cameraEaseMinSpeed = 150f;

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
        Collider groundCollider;
        StairSteps stairs;          // set while standing on a staircase
        float stairTarget, stairOffset;
        int lastStairStep = int.MinValue;
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
            viewCamera.transform.SetPositionAndRotation(RenderPosition + up * (eyeHeight - landDip + stairOffset), AimRotation);
            viewCamera.fieldOfView = baseFov + fovKick;
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
                    // Air control only adds speed up to the move speed and never brakes,
                    // so momentum carried out of a portal is preserved.
                    Vector3 wishDir = wish.normalized;
                    float current = Vector3.Dot(lateral, wishDir);
                    lateral += wishDir * Mathf.Clamp(speed - current, 0f, airAcceleration * dt);
                }
            }

            // Stay planted: on the ground, never drift away from the surface (no bouncing off
            // step edges or slopes). Jumping below replaces this.
            if (grounded && Vector3.Dot(vertical, groundNormal) > 0f) vertical = Vector3.zero;

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
            Vector3 d = viewCamera.transform.InverseTransformDirection(warningGravity);
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
