using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// The gravity of a spinning station round the world Z axis (positions relative to its centre).
    /// Pure maths, so the server, the client and the map checks all use exactly the same rule.
    ///
    /// - Rings: "down" points straight out from the axis or in toward it, keeping the sign your
    ///   gravity already has (only a portal can flip it).
    /// - Hub (optional): a sphere round the centre; down points out from the centre, so you stand
    ///   on the inside of it.
    /// - Spoke tubes (optional, need the hub): round tubes running straight in from a hole in the
    ///   inner ring to a hole in the hub. Inside a tube down points away from its centre line, so
    ///   you can walk anywhere round its wall. Each end is a rounded lip; on a lip down points at
    ///   the lip's core (the circle it curls round), so stepping over the edge of the hole, along the
    ///   tube and out over the hub's lip turns your "down" smoothly the whole way.
    /// </summary>
    [System.Serializable]
    public class SpinField
    {
        public float innerRingRadius = 24f;  // inner ring's walking surface (you stand on its outside)
        public float outerRingRadius = 40f;  // outer ring's walking surface (you stand on its inside)
        public float innerVoidRadius = 18f;
        public float outerVoidRadius = 46f;
        public float hubRadius;              // 0 = no hub (and no spokes)
        public float[] spokeAngles = new float[0];
        public float tubeRadius = 2f;        // inside of a spoke tube's wall
        public float lipRadius = 1f;         // the rounded edge at each end (also the ring's thickness)

        public static Vector3 Out(float degrees) { float a = degrees * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f); }
        public static Vector3 Along(float degrees) { float a = degrees * Mathf.Deg2Rad; return new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f); }
        static float Sq(float x) => x * x;
        public static float Radius(Vector3 p) => new Vector2(p.x, p.y).magnitude;

        /// <summary>How far round each hole the ring's gravity eases into the lip's.</summary>
        public const float MouthBlend = 1.5f;

        /// <summary>The hole's edge, measured from the tube's centre line.</summary>
        public float MouthRadius => tubeRadius + lipRadius;
        /// <summary>How far out along a spoke the hub lip's core circle sits (it sits one lip
        /// radius outside the glass).</summary>
        public float HubLipCentre => Mathf.Sqrt(Sq(hubRadius + lipRadius) - Sq(MouthRadius));
        /// <summary>How far out along a spoke the inner-ring lip's core sits, at a point on the
        /// mouth <paramref name="sideways"/> metres round the ring from the tube's centre line: one
        /// lip radius under the (curved) ring floor.</summary>
        public float RingLipCentre(float sideways) => Mathf.Sqrt(Mathf.Max(0f, Sq(innerRingRadius) - Sq(sideways))) - lipRadius;

        /// <summary>Gravity <paramref name="g"/> at <paramref name="p"/> (relative to the centre).</summary>
        public Vector3 Resolve(Vector3 g, Vector3 p)
        {
            if (hubRadius > 0f && spokeAngles != null)
            {
                float mouth = MouthRadius, sH = HubLipCentre, reach = lipRadius + 2f;
                foreach (float a in spokeAngles)
                {
                    Vector3 e = Out(a);
                    float s = Vector3.Dot(p, e);
                    if (s <= 0f) continue;
                    Vector3 perp = p - e * s;
                    float rho = perp.magnitude;
                    if (rho > mouth + MouthBlend) continue;
                    Vector3 side = rho > 1e-4f ? perp / rho : Vector3.zero;
                    float sR = RingLipCentre(rho > 1e-4f ? mouth * Vector3.Dot(perp, Along(a)) / rho : 0f);

                    if (rho > mouth)
                    {
                        // On the ring floor round the hole: ease from the ring's own down to the lip's
                        // (straight in along the spoke), so stepping onto the lip turns you smoothly.
                        float r0 = Radius(p);
                        if (s > sR && s < sR + lipRadius + 2.5f && r0 > 1e-3f)
                        {
                            Vector3 o = new Vector3(p.x / r0, p.y / r0, 0f);
                            if (Vector3.Dot(g, o) < 0f) return Vector3.Lerp(-e, -o, (rho - mouth) / MouthBlend).normalized;
                        }
                        continue;
                    }

                    if (s > sH && s < sR)
                    {
                        // In the tube: down onto its wall.
                        if (rho < tubeRadius + 0.5f) return rho > 1e-3f ? side : g;
                        continue;
                    }
                    if (side == Vector3.zero) continue;
                    if (s <= sH)
                    {
                        // The hub's lip: between the tube wall and the line from the centre through its core.
                        Vector3 c = e * sH + side * mouth, v = c - p;
                        if (rho * sH <= mouth * s && v.magnitude <= reach) return v.normalized;
                    }
                    else
                    {
                        // The ring's lip: between the tube wall and the line straight up through its core.
                        Vector3 c = e * sR + side * mouth, v = c - p;
                        if (rho <= mouth && v.magnitude <= reach) return v.normalized;
                    }
                }
            }

            if (hubRadius > 0f && p.sqrMagnitude < Sq(hubRadius + 0.5f) && p.sqrMagnitude > 1e-6f)
                return p.normalized;

            float r = Radius(p);
            if (r < 1e-3f) return g;
            Vector3 outward = new Vector3(p.x / r, p.y / r, 0f);
            return Vector3.Dot(g, outward) >= 0f ? outward : -outward;
        }

        /// <summary>Fallen off a ring: too close to the axis (but not in the hub or a spoke) or too far from it.</summary>
        public bool OutOfBounds(Vector3 p)
        {
            if (hubRadius > 0f)
            {
                if (p.sqrMagnitude < Sq(hubRadius + 1f)) return false;
                if (spokeAngles != null)
                    foreach (float a in spokeAngles)
                    {
                        Vector3 e = Out(a);
                        float s = Vector3.Dot(p, e);
                        if (s > hubRadius - 1f && s < innerRingRadius + 1f && (p - e * s).magnitude < MouthRadius + 0.5f) return false;
                    }
            }
            float r = Radius(p);
            return r < innerVoidRadius || r > outerVoidRadius;
        }
    }
}
