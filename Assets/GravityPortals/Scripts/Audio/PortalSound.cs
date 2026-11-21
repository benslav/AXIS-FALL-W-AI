using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Sound through portals (maps with <see cref="MapOptions.soundThroughPortals"/>): a sound on the
    /// other side of a door is heard coming from that door, as loud as the path through the door is
    /// long. Paths go through one door or two in a row (a sound two rooms away), and only the two
    /// shortest are played. A sound you can hear directly (same room) also plays as usual.
    ///
    /// The path through a door is worked out as if the two portals were one opening: the sound's
    /// position is carried through the far portal to the near one, the line from you toward that
    /// point picks the spot on the door's opening it comes through, and the voice is placed along
    /// your line to that spot at the full path length. So it pans to the door and fades with distance.
    /// </summary>
    public static class PortalSound
    {
        public const int MaxRoutes = 2;
        /// <summary>Each door a sound comes through takes a little off (it's a long way round).</summary>
        const float PerDoorVolume = 0.85f;

        struct Route
        {
            public Vector3 Position;
            public float Length;
            public int Doors;
        }

        static readonly List<Route> routes = new List<Route>();

        /// <summary>The positions (and volume scales) to play a sound at for a listener, through portals.</summary>
        public static int Routes(Vector3 source, Vector3 listener, float maxDistance, Vector3[] positions, float[] volumes)
        {
            routes.Clear();
            foreach (var p1 in Portal.All)
            {
                if (!Usable(p1) || !InFront(p1, listener)) continue;
                var q1 = p1.linkedPortal;
                Vector3 door1 = Opening(p1, listener, Map(q1, p1, source));
                Vector3 beyond1 = Map(p1, q1, door1);
                float near = Vector3.Distance(listener, door1);
                if (near >= maxDistance) continue;

                // Through this door.
                if (InFront(q1, source))
                {
                    float length = near + Vector3.Distance(beyond1, source);
                    if (length < maxDistance) Add(listener, door1, length, 1);
                }

                // Through this door and then another one in the room behind it.
                foreach (var p2 in Portal.All)
                {
                    if (p2 == q1 || p2 == p1 || !Usable(p2) || !InFront(p2, beyond1)) continue;
                    var q2 = p2.linkedPortal;
                    if (q2 == p1 || !InFront(q2, source)) continue;
                    float soFar = near + Vector3.Distance(beyond1, p2.transform.position);
                    if (soFar >= maxDistance) continue;
                    Vector3 seenInMiddle = Map(q2, p2, source);
                    Vector3 door2 = Opening(p2, beyond1, seenInMiddle);
                    Vector3 beyond2 = Map(p2, q2, door2);
                    float length = near + Vector3.Distance(beyond1, door2) + Vector3.Distance(beyond2, source);
                    if (length < maxDistance) Add(listener, door1, length, 2);
                }
            }
            routes.Sort((a, b) => a.Length.CompareTo(b.Length));
            int n = Mathf.Min(routes.Count, Mathf.Min(MaxRoutes, positions.Length));
            for (int i = 0; i < n; i++)
            {
                positions[i] = routes[i].Position;
                volumes[i] = Mathf.Pow(PerDoorVolume, routes[i].Doors);
            }
            return n;
        }

        static void Add(Vector3 listener, Vector3 door, float length, int doors)
        {
            Vector3 toward = door - listener;
            Vector3 dir = toward.sqrMagnitude > 1e-6f ? toward.normalized : Vector3.forward;
            routes.Add(new Route { Position = listener + dir * length, Length = length, Doors = doors });
        }

        static bool Usable(Portal p) => p != null && p.linkedPortal != null && !p.worldWrap && p.isActiveAndEnabled;

        static bool InFront(Portal p, Vector3 point) => Vector3.Dot(point - p.transform.position, p.Normal) > 0f;

        /// <summary>A point in front of <paramref name="from"/> as seen through <paramref name="to"/>.</summary>
        static Vector3 Map(Portal from, Portal to, Vector3 point) => Portal.TransferMatrix(from, to).MultiplyPoint3x4(point);

        /// <summary>Where the line from <paramref name="eye"/> toward <paramref name="target"/> (behind
        /// the portal) crosses its opening, kept inside the opening.</summary>
        static Vector3 Opening(Portal p, Vector3 eye, Vector3 target)
        {
            Vector3 n = p.Normal, d = target - eye;
            Vector3 hit = p.transform.position;
            float denom = Vector3.Dot(d, n);
            if (denom < -1e-4f)
            {
                float t = Vector3.Dot(p.transform.position - eye, n) / denom;
                hit = eye + d * Mathf.Clamp01(t);
            }
            Vector3 l = p.transform.InverseTransformPoint(hit);
            l.x = Mathf.Clamp(l.x, -p.size.x * 0.5f, p.size.x * 0.5f);
            l.y = Mathf.Clamp(l.y, -p.size.y * 0.5f, p.size.y * 0.5f);
            l.z = 0f;
            return p.transform.TransformPoint(l);
        }
    }
}
