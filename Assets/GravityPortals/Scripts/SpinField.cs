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
        public float lipRadius = 2.5f;       // the rounded edge at each end (bigger = a gentler turn)

        public static Vector3 Out(float degrees) { float a = degrees * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f); }
        public static Vector3 Along(float degrees) { float a = degrees * Mathf.Deg2Rad; return new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f); }
        static float Sq(float x) => x * x;
        public static float Radius(Vector3 p) => new Vector2(p.x, p.y).magnitude;

        /// <summary>The hole's edge, measured from the tube's centre line.</summary>
        public float MouthRadius => tubeRadius + lipRadius;
        /// <summary>How far out along a spoke the hub lip's core circle sits (it sits one lip
        /// radius outside the glass).</summary>
        public float HubLipCentre => Mathf.Sqrt(Sq(hubRadius + lipRadius) - Sq(MouthRadius));

        /// <summary>Out from a spoke's centre line, at angle psi (radians) round it: 0 = along +Z,
        /// 90 degrees = along the ring (increasing angle).</summary>
        public static Vector3 Round(Vector3 along, float psi) => Vector3.forward * Mathf.Cos(psi) + along * Mathf.Sin(psi);

        /// <summary>
        /// The core of the inner ring's lip at angle psi round a spoke: a point one lip radius
        /// under the ring's (curved) floor and one mouth radius from the tube's centre line. The
        /// lip is everything one lip radius from this curve, from straight up off the ring floor
        /// (<see cref="Flat"/> of the core) round to the tube wall (-<see cref="Round"/>). Both of
        /// those are square to the curve, so the lip meets the ring floor and the tube wall
        /// exactly, with no twist, and its gravity (toward the nearest core point) is always
        /// straight into it.
        /// </summary>
        public Vector3 RingLipCore(Vector3 e, Vector3 along, float psi)
        {
            float rc = innerRingRadius - lipRadius, mouth = MouthRadius;
            float sinPhi = Mathf.Clamp(mouth * Mathf.Sin(psi) / rc, -1f, 1f), cosPhi = Mathf.Sqrt(1f - sinPhi * sinPhi);
            return (e * cosPhi + along * sinPhi) * rc + Vector3.forward * (mouth * Mathf.Cos(psi));
        }

        public static Vector3 Flat(Vector3 p) => new Vector3(p.x, p.y, 0f).normalized;

        /// <summary>The angle round the spoke of the ring-lip core point nearest <paramref name="p"/>
        /// (a few Newton steps from <paramref name="psi"/>, which is already close).</summary>
        float NearestOnRingCore(Vector3 e, Vector3 along, Vector3 p, float psi)
        {
            // Minimise |p - c(psi)|^2: its slope is -2 (p - c).c' and its curvature 2 (|c'|^2 - (p - c).c'').
            // (Derivatives of the curve itself, not of the distance, so float precision is plenty.)
            const float h = 0.01f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 cm = RingLipCore(e, along, psi - h), c0 = RingLipCore(e, along, psi), cp = RingLipCore(e, along, psi + h);
                Vector3 d1 = (cp - cm) / (2f * h), d2 = (cp - 2f * c0 + cm) / (h * h), r = p - c0;
                float slope = -Vector3.Dot(r, d1), curve = d1.sqrMagnitude - Vector3.Dot(r, d2);
                if (curve <= 1e-4f) break;
                float stepPsi = Mathf.Clamp(slope / curve, -0.2f, 0.2f);
                psi -= stepPsi;
                if (Mathf.Abs(stepPsi) < 1e-5f) break;
            }
            return psi;
        }

        /// <summary>Gravity <paramref name="g"/> at <paramref name="p"/> (relative to the centre).</summary>
        public Vector3 Resolve(Vector3 g, Vector3 p)
        {
            if (hubRadius > 0f && spokeAngles != null)
            {
                float mouth = MouthRadius, sH = HubLipCentre, reach = lipRadius + 2f;
                foreach (float a in spokeAngles)
                {
                    Vector3 e = Out(a), along = Along(a);
                    float s = Vector3.Dot(p, e);
                    if (s <= 0f) continue;
                    Vector3 perp = p - e * s;
                    float rho = perp.magnitude;
                    if (rho > mouth + 1f) continue;
                    if (rho < 1e-4f)
                    {
                        if (s > sH && s < innerRingRadius) return g; // on the tube's centre line
                        continue;
                    }
                    Vector3 side = perp / rho;
                    float psi0 = Mathf.Atan2(Vector3.Dot(side, along), side.z);
                    float sR = Vector3.Dot(RingLipCore(e, along, psi0), e);

                    if (s > sH && s < sR)
                    {
                        // In the tube: down onto its wall.
                        if (rho < tubeRadius + 0.5f) return side;
                        continue;
                    }
                    if (s <= sH)
                    {
                        // The hub's lip (a true torus: the hub is round the tube's line): between the
                        // tube wall and the line from the centre through its core.
                        if (rho > mouth) continue;
                        Vector3 c = e * sH + side * mouth, v = c - p;
                        if (rho * sH <= mouth * s && v.magnitude <= reach) return v.normalized;
                        continue;
                    }

                    // The ring's lip: toward the nearest point of its core curve, while that point's
                    // direction lies on the lip's arc (between straight up and the tube wall).
                    float psi = NearestOnRingCore(e, along, p, psi0);
                    Vector3 core = RingLipCore(e, along, psi), d = p - core;
                    float dist = d.magnitude;
                    if (dist > reach || dist < 1e-4f) continue;
                    Vector3 u = d / dist, d0 = Flat(core), d1 = -Round(along, psi);
                    float arc = Vector3.Angle(d0, d1) + 0.5f;
                    if (Vector3.Angle(u, d0) <= arc && Vector3.Angle(u, d1) <= arc) return -u;
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
                        if (s > hubRadius - 1f && s < innerRingRadius + 1f && (p - e * s).magnitude < MouthRadius + 1f) return false;
                    }
            }
            float r = Radius(p);
            return r < innerVoidRadius || r > outerVoidRadius;
        }
    }
}
