using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Base class for anything that can pass through portals: players, props, projectiles.
    /// Owns the two things every traveller needs: temporarily ignoring the surface a portal
    /// sits on (so you can actually pass "into" the floor/wall), and the spatial transfer
    /// of position, rotation and momentum from one portal to the other.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public abstract class PortalTraveller : MonoBehaviour
    {
        readonly HashSet<Portal> ignoringPortals = new HashSet<Portal>();
        readonly List<Portal> scratch = new List<Portal>();
        Collider[] ownColliders;

        public Rigidbody Body { get; private set; }

        /// <summary>The portal this traveller just came out of. That portal stays solid
        /// for it until it leaves the portal's zone, which prevents teleport loops.</summary>
        public Portal ArrivalPortal { get; set; }

        public float LastTeleportTime { get; private set; } = float.NegativeInfinity;
        public Vector3 Center => Body.position;
        public Vector3 Velocity => Body.linearVelocity;

        /// <summary>False for objects whose physics runs somewhere else (a remote
        /// player, or a prop on a client that the server simulates). Portals leave
        /// those alone; their position arrives over the network.</summary>
        public virtual bool SimulatesLocally => true;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.useGravity = false; // all gravity in this game is custom and per-object
            ownColliders = GetComponentsInChildren<Collider>();
        }

        /// <summary>How far this traveller extends from its center along a direction,
        /// if it had the given rotation. Used to place it clear of the exit surface.</summary>
        public abstract float ExtentAlong(Vector3 direction, Quaternion rotation);

        /// <summary>The orientation that gets carried through the portal.</summary>
        protected virtual Quaternion TravelRotation => Body.rotation;

        /// <summary>Called after the transfer; subclasses switch gravity here.</summary>
        protected virtual void OnTeleported(Portal from, Portal to, Quaternion newRotation) { }

        public bool IsOwnCollider(Collider c)
        {
            for (int i = 0; i < ownColliders.Length; i++)
                if (ownColliders[i] == c) return true;
            return false;
        }

        public bool IsIgnoring(Portal portal) => ignoringPortals.Contains(portal);

        /// <summary>True if this traveller is currently passing through the given surface.
        /// Physics.IgnoreCollision does not affect queries, so ground checks and hitscan
        /// must check this themselves.</summary>
        public bool IsHostIgnored(Collider host)
        {
            foreach (var p in ignoringPortals)
                if (p != null && p.HasHost(host)) return true;
            return false;
        }

        public void SetPortalIgnore(Portal portal, bool ignore)
        {
            bool changed = ignore ? ignoringPortals.Add(portal) : ignoringPortals.Remove(portal);
            if (!changed || portal == null) return;

            // Several portals can share one host (e.g. two portals on the same floor),
            // so recompute per host instead of blindly toggling.
            foreach (var host in portal.Hosts)
            {
                if (host == null) continue;
                bool shouldIgnore = IsHostIgnored(host);
                foreach (var own in ownColliders)
                    if (own != null && !own.isTrigger)
                        Physics.IgnoreCollision(own, host, shouldIgnore);
            }
        }

        public void ClearPortalIgnores()
        {
            scratch.Clear();
            scratch.AddRange(ignoringPortals);
            foreach (var p in scratch) SetPortalIgnore(p, false);
        }

        /// <summary>Moves this traveller from one portal to the other as if the two
        /// portals were one continuous opening: position, orientation and velocity are
        /// all carried across.</summary>
        public void Teleport(Portal from, Portal to)
        {
            Matrix4x4 transfer = Portal.TransferMatrix(from, to);
            Quaternion rotationDelta = Portal.TransferRotation(from, to);

            Quaternion newRotation = rotationDelta * TravelRotation;
            Vector3 newPosition = transfer.MultiplyPoint3x4(Body.position);
            newPosition = to.ClampExitPosition(newPosition, ExtentAlong(to.Normal, newRotation));
            Vector3 newVelocity = transfer.MultiplyVector(Body.linearVelocity);
            Vector3 newAngular = transfer.MultiplyVector(Body.angularVelocity);

            SetPortalIgnore(from, false);

            Body.position = newPosition;
            Body.rotation = newRotation;
            transform.SetPositionAndRotation(newPosition, newRotation);
            Body.linearVelocity = newVelocity;
            if (!Body.freezeRotation) Body.angularVelocity = newAngular;
            Physics.SyncTransforms();

            ArrivalPortal = to;
            LastTeleportTime = Time.time;
            OnTeleported(from, to, newRotation);
            Portal.RaiseTeleported(this, from, to);
        }
    }
}
