using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>A player's hit volume at some moment: a capsule along their own up axis,
    /// so hits work the same whether they stand on a floor, wall or ceiling.</summary>
    public struct HitCapsule
    {
        public Vector3 Center;
        public Vector3 Up;
        public float HalfHeight;
        public float Radius;
        public int Index;            // caller's id for who this is

        public Vector3 Bottom => Center - Up * (HalfHeight - Radius);
        public Vector3 Top => Center + Up * (HalfHeight - Radius);

        /// <summary>Top 0.35 m of the capsule counts as the head.</summary>
        public bool IsHeadshot(Vector3 point) => Vector3.Dot(point - Center, Up) > HalfHeight - 0.35f;
    }

    public struct TraceResult
    {
        public const int MaxLegs = 4;
        public Vector3[] Points;     // shot path: start, portal entries/exits..., end
        public int PointCount;
        public bool HitWorld;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public Collider HitCollider;
        public int CapsuleIndex;     // -1 = no capsule hit
        public bool Headshot;
    }

    /// <summary>
    /// Shot tracing that understands portals: a ray that enters a portal's opening keeps
    /// going out of the linked portal (up to a few hops), so you can shoot someone on the
    /// ceiling through a floor portal. The server traces against rewound player capsules;
    /// clients trace against colliders just to draw the tracer.
    /// </summary>
    public static class CombatTrace
    {
        static readonly RaycastHit[] hits = new RaycastHit[32];

        public static TraceResult Trace(Vector3 origin, Vector3 direction, float range,
            Func<Collider, bool> ignoreCollider, IList<HitCapsule> capsules, int ignoreCapsule)
        {
            var result = new TraceResult { Points = new Vector3[TraceResult.MaxLegs * 2], CapsuleIndex = -1 };
            result.Points[result.PointCount++] = origin;
            direction.Normalize();
            float remaining = range;

            for (int leg = 0; leg < TraceResult.MaxLegs && remaining > 0f; leg++)
            {
                // Nearest solid surface.
                float worldDist = remaining;
                RaycastHit worldHit = default;
                bool hitWorld = false;
                int n = Physics.RaycastNonAlloc(origin, direction, hits, remaining, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (hits[i].distance >= worldDist) continue;
                    if (ignoreCollider != null && ignoreCollider(hits[i].collider)) continue;
                    worldDist = hits[i].distance;
                    worldHit = hits[i];
                    hitWorld = true;
                }

                // Nearest portal opening (it sits on a surface, so allow a hair past the wall hit).
                Portal portal = null;
                float portalDist = float.MaxValue;
                foreach (var p in Portal.All)
                {
                    if (p == null || p.linkedPortal == null) continue;
                    float t = p.RaycastOpening(origin, direction);
                    if (t >= 0f && t < portalDist && t <= worldDist + 0.05f)
                    {
                        portalDist = t;
                        portal = p;
                    }
                }

                // Nearest player capsule.
                int capsule = -1;
                float capsuleDist = Mathf.Min(worldDist, portalDist);
                if (capsules != null)
                {
                    for (int i = 0; i < capsules.Count; i++)
                    {
                        if (capsules[i].Index == ignoreCapsule) continue;
                        float t = RayCapsule(origin, direction, capsules[i]);
                        if (t >= 0f && t < capsuleDist)
                        {
                            capsuleDist = t;
                            capsule = i;
                        }
                    }
                }

                if (capsule >= 0)
                {
                    Vector3 point = origin + direction * capsuleDist;
                    result.Points[result.PointCount++] = point;
                    result.CapsuleIndex = capsules[capsule].Index;
                    result.Headshot = capsules[capsule].IsHeadshot(point);
                    result.HitPoint = point;
                    result.HitNormal = -direction;
                    return result;
                }

                if (portal != null)
                {
                    Vector3 entry = origin + direction * portalDist;
                    result.Points[result.PointCount++] = entry;
                    Matrix4x4 m = Portal.TransferMatrix(portal, portal.linkedPortal);
                    origin = m.MultiplyPoint3x4(entry) + portal.linkedPortal.Normal * 0.02f;
                    direction = m.MultiplyVector(direction).normalized;
                    result.Points[result.PointCount++] = origin;
                    remaining -= portalDist;
                    continue;
                }

                Vector3 end = origin + direction * worldDist;
                result.Points[result.PointCount++] = end;
                if (hitWorld)
                {
                    result.HitWorld = true;
                    result.HitPoint = worldHit.point;
                    result.HitNormal = worldHit.normal;
                    result.HitCollider = worldHit.collider;
                }
                return result;
            }

            return result;
        }

        /// <summary>Ray (normalized dir) vs capsule. Returns distance to the first hit, or -1.</summary>
        public static float RayCapsule(Vector3 ro, Vector3 rd, HitCapsule c)
        {
            Vector3 pa = c.Bottom, pb = c.Top;
            float r = c.Radius;
            Vector3 ba = pb - pa, oa = ro - pa;
            float baba = Vector3.Dot(ba, ba);
            float bard = Vector3.Dot(ba, rd);
            float baoa = Vector3.Dot(ba, oa);
            float rdoa = Vector3.Dot(rd, oa);
            float oaoa = Vector3.Dot(oa, oa);

            float best = -1f;
            float a = baba - bard * bard;
            if (a > 1e-6f)
            {
                float b = baba * rdoa - baoa * bard;
                float cc = baba * oaoa - baoa * baoa - r * r * baba;
                float h = b * b - a * cc;
                if (h >= 0f)
                {
                    float t = (-b - Mathf.Sqrt(h)) / a;
                    float y = baoa + t * bard;
                    if (t >= 0f && y > 0f && y < baba) best = t; // hit the cylinder body
                }
            }
            if (best >= 0f) return best;

            // End caps.
            best = Closer(best, RaySphere(ro, rd, pa, r));
            best = Closer(best, RaySphere(ro, rd, pb, r));
            return best;
        }

        static float Closer(float a, float b)
        {
            if (a < 0f) return b;
            if (b < 0f) return a;
            return Mathf.Min(a, b);
        }

        static float RaySphere(Vector3 ro, Vector3 rd, Vector3 center, float r)
        {
            Vector3 oc = ro - center;
            float b = Vector3.Dot(oc, rd);
            float c = Vector3.Dot(oc, oc) - r * r;
            float h = b * b - c;
            if (h < 0f) return -1f;
            float t = -b - Mathf.Sqrt(h);
            return t >= 0f ? t : -1f;
        }

        public static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return a + ab * t;
        }

        /// <summary>True if this collider belongs to a player or a physics prop (not level geometry).</summary>
        public static bool IsDynamic(Collider c)
        {
            var rb = c.attachedRigidbody;
            return rb != null && (rb.GetComponent<GravityPlayerController>() != null || rb.GetComponent<GravityBody>() != null);
        }
    }
}
