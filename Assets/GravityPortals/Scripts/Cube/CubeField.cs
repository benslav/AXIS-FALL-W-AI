using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Gravity on the turning cube. Pure maths, so the client, the server and the map checks all
    /// use exactly the same rule.
    ///
    ///  - Outside: down points at the nearest point of the cube shrunk by its edge radius, so it is
    ///    straight into a face on a face and turns smoothly over the rounded edges and corners. You
    ///    can walk all the way round, and anything thrown or knocked away falls back (a tiny planet).
    ///  - Inside (the hollow middle): walk-anywhere, the same rule as a Tesseract room
    ///    (<see cref="RoomField"/>): down follows the rounded edge you are on and only takes over
    ///    close to the gravity you already have, so a jump across the middle never pulls you sideways.
    ///  - While a layer turns, the space in its slab turns with it: a point there gets the rest field
    ///    of where it came from, turned by the layer's rotation so far. Gravity on a turning layer
    ///    keeps you held to its surface. The cube looks the same after every quarter turn, so the
    ///    field matches the rest field exactly at both ends of a turn.
    /// </summary>
    [System.Serializable]
    public class CubeField
    {
        [Tooltip("Half the outer size of the cube, metres.")]
        public float outerHalf = 21f;
        [Tooltip("Radius of the rounded outer edges and corners, metres.")]
        public float outerRadius = 3f;
        [Tooltip("The hollow inside (one room with rounded inner edges).")]
        public RoomField inside = new RoomField();
        [Tooltip("Points farther than this from the cube centre count as fallen out of the map.")]
        public float outOfBounds = 120f;

        public bool Active => outerHalf > 0f && inside != null && inside.Active;

        public float InnerHalf => inside.half;

        /// <summary>Gravity at <paramref name="p"/> (relative to the cube centre) for a traveller that
        /// has gravity <paramref name="g"/>, with the cube in <paramref name="pose"/> (null = at rest).</summary>
        public Vector3 Resolve(Vector3 g, Vector3 p, CubePose pose)
        {
            if (pose != null && pose.InTurningLayer(p))
            {
                Quaternion q = pose.TurnRotation, back = CubeSchedule.Conjugate(q);
                return (q * ResolveAtRest(back * g, back * p)).normalized;
            }
            return ResolveAtRest(g, p);
        }

        /// <summary>Gravity for someone carried by a turning layer: the rest field of where they
        /// came from, turned by the layer's rotation so far (<paramref name="frame"/>), wherever
        /// they are (standing on the very edge of a piece, their middle can be just past the seam).</summary>
        public Vector3 ResolveInFrame(Vector3 g, Vector3 p, Quaternion frame)
        {
            Quaternion back = CubeSchedule.Conjugate(frame);
            return (frame * ResolveAtRest(back * g, back * p)).normalized;
        }

        /// <summary>The field with every layer in place.</summary>
        public Vector3 ResolveAtRest(Vector3 g, Vector3 p)
        {
            if (Inside(p)) return inside.Resolve(g, p);
            float s = outerHalf - outerRadius;
            var nearest = new Vector3(Mathf.Clamp(p.x, -s, s), Mathf.Clamp(p.y, -s, s), Mathf.Clamp(p.z, -s, s));
            Vector3 down = nearest - p;
            // Within the shell itself (only in passing, through a door): keep what you have.
            if (down.sqrMagnitude < 1e-8f) return g;
            return down.normalized;
        }

        /// <summary>In the hollow middle (at rest coordinates).</summary>
        public bool Inside(Vector3 p)
        {
            float h = InnerHalf;
            return Mathf.Abs(p.x) < h && Mathf.Abs(p.y) < h && Mathf.Abs(p.z) < h;
        }

        public bool OutOfBounds(Vector3 p) => p.sqrMagnitude > outOfBounds * outOfBounds;
    }
}
