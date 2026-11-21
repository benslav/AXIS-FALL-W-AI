using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// A physics object (crate, ball, projectile) with its own gravity direction.
    /// Passing through a portal points its gravity into the exit portal's surface,
    /// exactly like a player.
    ///
    /// Online, the server simulates it (server-authoritative) and clients just show
    /// the position NetworkTransform sends them.
    /// </summary>
    public class GravityBody : PortalTraveller
    {
        [SerializeField] Vector3 gravityDirection = Vector3.down;
        public float gravityStrength = 20f;
        [Tooltip("Destroy after this many seconds. 0 = live forever.")]
        public float lifetime;

        NetworkObject netObject;
        NetworkTransform netTransform;

        public Vector3 GravityDirection
        {
            get => gravityDirection;
            set => gravityDirection = value.sqrMagnitude > 0f ? value.normalized : Vector3.down;
        }

        // Networked bodies run on the server; local-only ones (like the shooter's preview bolt) run here.
        public override bool SimulatesLocally => netObject == null || !NetMode.IsClientOnly;

        protected override void Awake()
        {
            base.Awake();
            netObject = GetComponent<NetworkObject>();
            netTransform = GetComponent<NetworkTransform>();
        }

        void Start()
        {
            if (lifetime <= 0f) return;
            if (netObject != null && NetMode.IsOnline)
            {
                if (NetMode.IsServer) StartCoroutine(DespawnAfter(lifetime));
            }
            else
            {
                Destroy(gameObject, lifetime);
            }
        }

        IEnumerator DespawnAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (netObject != null && netObject.IsSpawned) netObject.Despawn();
        }

        void FixedUpdate()
        {
            if (!SimulatesLocally) return;
            if (RideCube(out Quaternion frame))
                gravityDirection = SpinGravity.ApplyRiding(gravityDirection, Body.position, frame);
            else
                gravityDirection = SpinGravity.Apply(gravityDirection, Body.position); // field maps only
            Body.AddForce(gravityDirection * gravityStrength, ForceMode.Acceleration);
        }

        // The turning cube: resting on a turning piece carries you round with it (like players).
        CubePiece restingOn;
        float restingTime = -10f;

        void OnCollisionStay(Collision collision)
        {
            var piece = CubePiece.Of(collision.collider);
            if (piece == null) return;
            restingOn = piece;
            restingTime = Time.fixedTime;
        }

        bool RideCube(out Quaternion frame)
        {
            frame = Quaternion.identity;
            var turns = CubeTurns.Current;
            if (turns == null || restingOn == null || Time.fixedTime - restingTime > Time.fixedDeltaTime * 2.5f) return false;
            var pose = turns.Pose;
            if (!pose.Turning || !pose.InMove[restingOn.index]) return false;
            frame = pose.TurnRotation;
            Quaternion step = turns.StepDelta(restingOn.index);
            Vector3 c = turns.Centre;
            Body.position = c + step * (Body.position - c);
            Body.rotation = step * Body.rotation;
            Body.linearVelocity = step * Body.linearVelocity;
            gravityDirection = step * gravityDirection;
            return true;
        }

        float cachedExtent = -1f;

        public override float ExtentAlong(Vector3 direction, Quaternion rotation)
        {
            // Bounding-sphere approximation: always enough clearance, whatever the rotation.
            // Rotation-independent, so it's worked out once (this runs every physics step near portals).
            if (cachedExtent < 0f)
            {
                cachedExtent = 0.1f;
                foreach (var c in GetComponentsInChildren<Collider>())
                    if (!c.isTrigger) cachedExtent = Mathf.Max(cachedExtent, c.bounds.extents.magnitude);
            }
            return cachedExtent;
        }

        protected override void OnTeleported(Portal from, Portal to, Quaternion newRotation)
        {
            // Same rule as players.
            GravityDirection = SpinGravity.Apply(Portal.GravityAfterPass(from, to, GravityDirection), Body.position);

            // Tell clients to jump straight to the exit instead of sliding across the map.
            if (netTransform != null && netObject != null && netObject.IsSpawned && NetMode.IsServer)
            {
                Vector3 velocity = Body.linearVelocity;
                netTransform.Teleport(transform.position, transform.rotation, transform.localScale);
                Body.linearVelocity = velocity;
            }
        }
    }
}
