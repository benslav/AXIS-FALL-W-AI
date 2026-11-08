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
            Body.AddForce(gravityDirection * gravityStrength, ForceMode.Acceleration);
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
            GravityDirection = Portal.GravityAfterPass(from, to, GravityDirection);

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
