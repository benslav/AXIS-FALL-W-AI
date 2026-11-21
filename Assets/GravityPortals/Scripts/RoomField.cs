using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Gravity for walk-anywhere cube rooms (The Tesseract): each room is a cube whose twelve inner
    /// edges are rounded into quarter-pipes and whose eight corners are rounded into sphere caps, so
    /// you can walk off the floor, up the curve and onto any wall. Pure maths, so the client, the
    /// server and the map checks all use exactly the same rule.
    ///
    /// The rule, at a position inside a room:
    ///  - On a rounded edge or corner, down points straight into the curve under you (away from
    ///    the curve's centre line), so it turns smoothly as you walk round it.
    ///  - Near the middle of a face it points into that face; in the open middle of the room there
    ///    is no pull either way, so you keep the gravity you have.
    ///  - The field only takes over when it is close to the gravity you already have (within
    ///    <see cref="captureAngle"/>), or when you're mid-turn (gravity off the axes). So falling
    ///    or jumping along a wall never pulls you onto it; you only follow a curve you're on.
    ///  - Door corners are left sharp (the door stands flush against the floor): there your gravity
    ///    stays on the face you're on.
    /// </summary>
    [System.Serializable]
    public class RoomField
    {
        /// <summary>A straight (not rounded) stretch of a room edge, where a door stands.</summary>
        [System.Serializable]
        public struct SharpEdge
        {
            public int room;
            /// <summary>The two faces it joins, as world directions out of the room (axis-aligned).</summary>
            public Vector3 faceA, faceB;
            /// <summary>Half its length along the edge, centred on the middle of the edge.</summary>
            public float halfLength;
        }

        public Vector3[] centres = new Vector3[0];
        [Tooltip("Half the inside size of a room.")]
        public float half = 10f;
        [Tooltip("Radius of the rounded edges and corners.")]
        public float edgeRadius = 3f;
        [Tooltip("The field only takes over when it is within this many degrees of your gravity (degrees).")]
        public float captureAngle = 30f;
        public SharpEdge[] sharpEdges = new SharpEdge[0];

        public bool Active => centres != null && centres.Length > 0;

        /// <summary>The room a point is in (or within <paramref name="margin"/> of), else -1.</summary>
        public int RoomOf(Vector3 p, float margin)
        {
            float limit = half + margin;
            for (int i = 0; i < centres.Length; i++)
            {
                Vector3 l = p - centres[i];
                if (Mathf.Abs(l.x) <= limit && Mathf.Abs(l.y) <= limit && Mathf.Abs(l.z) <= limit) return i;
            }
            return -1;
        }

        /// <summary>Outside every room: fallen out of the map.</summary>
        public bool OutOfBounds(Vector3 p) => Active && RoomOf(p, 3f) < 0;

        /// <summary>Within half a degree of a world axis.</summary>
        public static bool AxisAligned(Vector3 g)
        {
            Vector3 a = new Vector3(Mathf.Abs(g.x), Mathf.Abs(g.y), Mathf.Abs(g.z));
            return Mathf.Max(a.x, Mathf.Max(a.y, a.z)) >= 0.99996f * g.magnitude;
        }

        /// <summary>The direction the rounded room surface lies in from <paramref name="p"/> (its
        /// "down"), or zero in the open middle of the room or on a sharp door corner.</summary>
        public Vector3 SurfaceDown(Vector3 p, out bool sharp)
        {
            sharp = false;
            int room = RoomOf(p, edgeRadius);
            if (room < 0) return Vector3.zero;
            Vector3 l = p - centres[room];
            float s = half - edgeRadius;
            Vector3 d = new Vector3(Beyond(l.x, s), Beyond(l.y, s), Beyond(l.z, s));
            if (d.sqrMagnitude < 1e-10f) return Vector3.zero;
            foreach (var e in sharpEdges)
            {
                if (e.room != room) continue;
                if (Vector3.Dot(l, e.faceA) <= s || Vector3.Dot(l, e.faceB) <= s) continue;
                Vector3 along = Vector3.Cross(e.faceA, e.faceB);
                if (Mathf.Abs(Vector3.Dot(l, along)) > e.halfLength) continue;
                sharp = true;
                return Vector3.zero;
            }
            return d.normalized;
        }

        static float Beyond(float x, float s) => x > s ? x - s : x < -s ? x + s : 0f;

        public Vector3 Resolve(Vector3 g, Vector3 p)
        {
            Vector3 down = SurfaceDown(p, out bool sharp);
            if (sharp)
            {
                // A door corner: stay on whichever face you're on (snap back onto it if mid-turn).
                return AxisAligned(g) ? g : NearestAxis(g);
            }
            if (down == Vector3.zero) return g;
            if (!AxisAligned(g) || Vector3.Angle(g, down) <= captureAngle) return down;
            return g;
        }

        public static Vector3 NearestAxis(Vector3 g)
        {
            Vector3 a = new Vector3(Mathf.Abs(g.x), Mathf.Abs(g.y), Mathf.Abs(g.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(g.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(g.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(g.z));
        }
    }
}
