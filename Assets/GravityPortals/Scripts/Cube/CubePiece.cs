using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// One piece of the turning cube. Its object sits at the cube centre and its geometry is built at
    /// the piece's home place, so the object's rotation alone (round the cube centre) puts the piece
    /// wherever the turns have taken it. A kinematic Rigidbody, so turning it pushes and carries
    /// whatever touches it properly. Doors, spawns and the ring gear on a centre piece are children
    /// of it and turn with it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CubePiece : MonoBehaviour
    {
        [Tooltip("Grid index (CubeSchedule.Index): 0..26, 13 is the core.")]
        public int index;

        static readonly Dictionary<int, CubePiece> byIndex = new Dictionary<int, CubePiece>();
        static readonly Dictionary<Collider, CubePiece> byCollider = new Dictionary<Collider, CubePiece>();

        public Rigidbody Body { get; private set; }

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.isKinematic = true;
            Body.useGravity = false;
        }

        void OnEnable()
        {
            byIndex[index] = this;
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) byCollider[c] = this;
        }

        void OnDisable()
        {
            if (byIndex.TryGetValue(index, out var p) && p == this) byIndex.Remove(index);
            foreach (var c in GetComponentsInChildren<Collider>(true)) byCollider.Remove(c);
        }

        public static CubePiece Find(int index) => byIndex.TryGetValue(index, out var p) ? p : null;

        /// <summary>The piece a collider belongs to, or null (not part of the cube).</summary>
        public static CubePiece Of(Collider c) => c != null && byCollider.TryGetValue(c, out var p) ? p : null;
    }
}
