using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// "Möbius Spin": a spinning station with two concentric rings round an axis (world Z through
    /// the origin), using <see cref="SpinGravity"/> with the field from <see cref="Field"/>.
    /// - Outer walkway: the inside of the outer ring; gravity points outward (you look up at the hub).
    /// - Inner corridor: the outside of the inner ring; gravity points in toward the hub.
    /// The two face each other across the gap, each side seeing the other upside down. One door
    /// pair, the twist, joins the top of the outer walkway to the bottom of the inner corridor:
    /// walk through and you come out with the other side's gravity. Rings are many flat segments,
    /// so they walk like smooth curves. Falling off a ring is a fall into the void.
    /// - Four round spoke tubes run from holes in the inner ring in to the hub; walk their walls.
    /// - The glass hub in the middle (stand on the inside, look down at both rings): by the tubes
    ///   from the inner ring, by a door from the outer ring.
    ///
    /// Uses the Skybridge station materials. Pure data, built by <see cref="PavilionBuilder"/>.
    /// </summary>
    public static class MobiusSpinLayout
    {
        public const float OuterRadius = 40f;   // the outer walkway's surface (you stand on the inside)
        public const float InnerRadius = 24f;   // the inner corridor's surface (you stand on the outside)
        public const float HalfWidth = 8f;      // both rings are 16 m wide along the axis
        public const int Segments = 64;
        public const float OuterDoorAngle = 90f, InnerDoorAngle = 270f;
        public const float InnerVoidRadius = 18f, OuterVoidRadius = 46f;
        public const float HubRadius = 12f;      // the glass hub's inside surface (you stand on the inside)
        public const float TubeRadius = 2f;      // spoke tubes (inner ring to hub): inside of the wall
        public const float LipRadius = 1f;       // the rounded edge at each end of a tube (= the inner ring's thickness)
        public static readonly float[] SpokeAngles = { 45f, 135f, 225f, 315f };
        /// <summary>The steel collar round each tube's hole in the glass reaches this far round the
        /// hub from the tube's centre line (degrees).</summary>
        public const float HubHoleAngle = 28f;
        /// <summary>The inner ring is cut away round each tube mouth out to this distance from the
        /// tube's centre line; the mouth's rim strips fill it.</summary>
        public const float RimRadius = 5.7f;
        public const float OuterHubDoorAngle = 270f;

        public static readonly Bounds VoidBounds = new Bounds(Vector3.zero, new Vector3(110f, 110f, 60f));

        const int MatOuterHull = SkybridgeLayout.MatHullFloor, MatInnerHull = SkybridgeLayout.MatHullCeiling,
            MatSteel = SkybridgeLayout.MatSteel, MatHazard = SkybridgeLayout.MatHazard, MatDark = SkybridgeLayout.MatDarkSteel,
            MatLight = SkybridgeLayout.MatLight, MatOuterGlow = SkybridgeLayout.MatFloorGlow, MatInnerGlow = SkybridgeLayout.MatCeilingGlow,
            MatGrate = SkybridgeLayout.MatGrate;
        /// <summary>The hub's glass: one extra material after the station's own.</summary>
        public const int MatGlass = SkybridgeLayout.MaterialCount, MaterialCount = SkybridgeLayout.MaterialCount + 1;

        public static PavilionLayout Generate()
        {
            var g = new Generator();
            g.Run();
            return g.Layout;
        }

        /// <summary>This map's gravity (the scene's <see cref="SpinGravity"/> gets a copy).</summary>
        public static SpinField Field() => new SpinField
        {
            innerRingRadius = InnerRadius, outerRingRadius = OuterRadius,
            innerVoidRadius = InnerVoidRadius, outerVoidRadius = OuterVoidRadius,
            hubRadius = HubRadius, spokeAngles = (float[])SpokeAngles.Clone(),
            tubeRadius = TubeRadius, lipRadius = LipRadius,
        };

        /// <summary>Straight out from the axis at an angle (degrees, in the XY plane).</summary>
        public static Vector3 Out(float degrees) => SpinField.Out(degrees);
        /// <summary>Along the ring (increasing angle).</summary>
        public static Vector3 Along(float degrees) => SpinField.Along(degrees);

        sealed class Generator
        {
            public readonly PavilionLayout Layout = new PavilionLayout();
            string frameName;
            Vector3 origin;
            Quaternion rot;

            Vector3 W(Vector3 local) => origin + rot * local;

            /// <summary>A frame standing on a ring at an angle: local up away from its surface (toward
            /// the axis on the outer ring, away from it on the inner), forward along the ring,
            /// local x across the band.</summary>
            void RingFrame(bool outer, float degrees)
            {
                Vector3 o = Out(degrees);
                frameName = outer ? "Outer" : "Inner";
                origin = o * (outer ? OuterRadius : InnerRadius);
                rot = PavilionLayout.Basis(Along(degrees), outer ? -o : o);
            }

            void B(string name, Vector3 c, Vector3 s, int mat, bool collide = true, Quaternion? worldRot = null, bool world = false, bool visible = true)
            {
                Layout.Boxes.Add(new PavilionBox
                {
                    Name = $"{frameName}: {name}", Center = world ? c : W(c), Size = s, Rotation = world ? (worldRot ?? Quaternion.identity) : rot,
                    Material = (PavilionMat)mat, Collide = collide, Visible = visible, BreakId = -1,
                });
            }

            public void Run()
            {
                Rings();
                Twist();
                HubDoors();
                Spokes();
                Hub();
                Cover();
                Spawns();
                Structure();
                Stars();
            }

            // ---------------------------------------------------------------- the rings

            void Rings()
            {
                float step = 360f / Segments;
                float outerChord = 2f * (OuterRadius + 1f) * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) + 0.05f;
                float innerChord = 2f * InnerRadius * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) + 0.05f;
                for (int k = 0; k < Segments; k++)
                {
                    float a = k * step;
                    foreach (bool outer in new[] { true, false })
                    {
                        RingFrame(outer, a);
                        float chord = outer ? outerChord : innerChord;
                        int glow = outer ? MatOuterGlow : MatInnerGlow;
                        bool mouth = !outer && SpokeAngles.Any(sa => Mathf.Abs(Mathf.DeltaAngle(a, sa)) < 18f);
                        if (!mouth)
                            B("Ring", new Vector3(0, -0.5f, 0), new Vector3(2f * HalfWidth + 0.4f, 1f, chord), outer ? MatOuterHull : MatInnerHull);
                        else
                            MouthSegment(chord);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            B("Rail", new Vector3(s * (HalfWidth - 0.1f), 0.95f, 0), new Vector3(0.12f, 0.1f, chord), MatSteel);
                            B("Rail Post", new Vector3(s * (HalfWidth - 0.1f), 0.47f, 0), new Vector3(0.1f, 0.95f, 0.1f), MatSteel);
                            B("Edge Glow", new Vector3(s * (HalfWidth - 0.6f), 0.01f, 0), new Vector3(0.15f, 0.02f, chord), glow, collide: false);
                        }
                        if (mouth) continue; // no ribs or seams across a tube's hole
                        if (k % 4 == 0) B("Hull Rib", new Vector3(0, -1.4f, 0), new Vector3(2f * HalfWidth + 1f, 0.9f, 0.6f), MatSteel, collide: false);
                        if (k % 2 == 0) B("Hull Seam", new Vector3(0, 0.01f, chord * 0.5f), new Vector3(2f * HalfWidth - 1.6f, 0.02f, 0.08f), glow, collide: false);
                    }
                    if (k % 5 == 0)
                    {
                        // Warm lights over the outer walkway, cool ones over the inner corridor.
                        Layout.Lights.Add(new PavilionLight { Position = Out(a) * (OuterRadius - 6f), Range = 16f, Intensity = 1.8f, Color = new Color(1f, 0.78f, 0.55f) });
                        Layout.Lights.Add(new PavilionLight { Position = Out(a) * (InnerRadius + 6f), Range = 16f, Intensity = 1.8f, Color = new Color(0.55f, 0.8f, 1f) });
                    }
                }
            }

            /// <summary>An inner-ring segment near a tube mouth: tiles of the same flat plate, leaving out
            /// the ones the mouth's rim strips cover (in the current ring frame).</summary>
            void MouthSegment(float chord)
            {
                const int Cols = 24, Rows = 4;
                float tw = (2f * HalfWidth + 0.4f) / Cols, th = chord / Rows;
                for (int i = 0; i < Cols; i++)
                    for (int j = 0; j < Rows; j++)
                    {
                        var local = new Vector3(-HalfWidth - 0.2f + tw * (i + 0.5f), 0f, -chord * 0.5f + th * (j + 0.5f));
                        Vector3 p = W(local);
                        bool covered = SpokeAngles.Any(sa =>
                        {
                            Vector3 e = Out(sa);
                            return Vector3.Dot(p, e) > 0f && (p - e * Vector3.Dot(p, e)).magnitude < RimRadius - 0.5f;
                        });
                        if (!covered) B("Ring Tile", local + new Vector3(0, -0.5f, 0), new Vector3(tw + 0.02f, 1f, th + 0.02f), MatInnerHull);
                    }
            }

            // ---------------------------------------------------------------- the twist

            /// <summary>One door pair: the top of the outer walkway to the bottom of the inner corridor.
            /// Each is upright on its own ring, so you come out standing with the other side's gravity.</summary>
            void Twist()
            {
                RingFrame(true, OuterDoorAngle);
                var outerDoor = Door("Outer Twist Door");
                RingFrame(false, InnerDoorAngle);
                var innerDoor = Door("Inner Twist Door");
                outerDoor.Color = innerDoor.Color = new Color(0.65f, 0.45f, 1f);
                outerDoor.Link = 1;
                innerDoor.Link = 0;
                Layout.Portals.Add(outerDoor);
                Layout.Portals.Add(innerDoor);
            }

            /// <summary>A door into the glass hub from the outer ring (the inner ring has the spoke
            /// tubes), to a door standing on the hub's equator. In the hub down is out from the
            /// centre, so you stand on the glass.</summary>
            void HubDoors()
            {
                RingFrame(true, OuterHubDoorAngle);
                var outerRing = Door("Outer Hub Door");
                HubFrame(180f);
                var hub = Door("Hub Door to Outer Ring");
                int first = Layout.Portals.Count;
                outerRing.Color = hub.Color = new Color(0.3f, 0.9f, 1f);
                outerRing.Link = first + 1; hub.Link = first;
                Layout.Portals.Add(outerRing); Layout.Portals.Add(hub);
            }

            /// <summary>A frame standing on the inside of the hub's glass at a point on its equator,
            /// facing along the axis (+Z).</summary>
            void HubFrame(float degrees)
            {
                Vector3 d = Out(degrees);
                frameName = "Hub";
                origin = d * HubRadius;
                rot = PavilionLayout.Basis(Vector3.forward, -d);
            }

            PavilionPortal Door(string name)
            {
                B("Twist Bulkhead", new Vector3(0, 2f, -0.2f), new Vector3(3.2f, 4f, 0.4f), MatDark);
                for (int s = -1; s <= 1; s += 2)
                    B("Twist Jamb", new Vector3(s * 1.4f, 1.75f, 0.03f), new Vector3(0.2f, 3.5f, 0.06f), MatHazard, collide: false);
                B("Twist Light", new Vector3(0, 3.75f, 0.03f), new Vector3(2.8f, 0.2f, 0.06f), MatLight, collide: false);
                Layout.Lights.Add(new PavilionLight { Position = W(new Vector3(0, 3f, 2f)), Range = 9f, Intensity = 1.5f, Color = new Color(0.8f, 0.6f, 1f) });
                return new PavilionPortal
                {
                    Name = name, Position = W(new Vector3(0, 1.7f, 0.01f)), Normal = rot * Vector3.forward, Up = rot * Vector3.up,
                    Size = new Vector2(2.4f, 3.4f), Doorway = true, Link = -1,
                };
            }

            // ---------------------------------------------------------------- spoke tubes

            const int TubeSides = 24;

            /// <summary>Round tubes from a hole in the inner ring straight in to a hole in the hub.
            /// Walk onto the hole and its rounded lip takes you over the edge and onto the tube wall;
            /// gravity follows (<see cref="SpinField"/>), and inside the tube down is out from its
            /// centre line, so any part of the wall is floor. At the hub end another lip rolls you out
            /// onto the glass. Every third strip of the wall is glass.</summary>
            void Spokes()
            {
                var field = Field();
                float mouth = field.MouthRadius, sH = field.HubLipCentre, l = LipRadius;
                float dPsi = 360f / TubeSides, half = dPsi * 0.5f * Mathf.Deg2Rad;
                foreach (float sa in SpokeAngles)
                {
                    frameName = $"Spoke {sa:0}";
                    Vector3 e = Out(sa), along = Along(sa);
                    for (int i = 0; i < TubeSides; i++)
                    {
                        float psi = (i + 0.5f) * dPsi * Mathf.Deg2Rad;
                        Vector3 w = Vector3.forward * Mathf.Cos(psi) + along * Mathf.Sin(psi); // out from the tube's centre line
                        float sR = field.RingLipCentre(mouth * Mathf.Sin(psi));

                        // The wall: hidden flat strips just behind the smooth surface you walk on (they
                        // only stop anything fast punching through it).
                        bool glass = i % 3 == 0;
                        float width = 2f * (TubeRadius + 0.35f) * Mathf.Tan(half) + 0.04f;
                        B(glass ? "Glass" : "Tube Wall", e * ((sH + sR) * 0.5f) + w * (TubeRadius + 0.2f), new Vector3(width, 0.3f, sR - sH + 0.05f),
                            glass ? MatGlass : MatSteel, worldRot: PavilionLayout.Basis(e, -w), world: true, visible: false);
                        if (i % 3 == 2)
                            B("Tube Glow", e * ((sH + sR) * 0.5f) + w * (TubeRadius - 0.01f), new Vector3(0.1f, 0.02f, sR - sH - 1f),
                                MatInnerGlow, collide: false, worldRot: PavilionLayout.Basis(e, -w), world: true);

                        // The ring's lip: from the mouth's top edge, curling down onto the wall.
                        Vector3 cR = e * sR + w * mouth;
                        Lip(cR, e, -w, e, half);
                        // The hub's lip: from the wall, curling out onto the glass.
                        Vector3 cH = e * sH + w * mouth;
                        Lip(cH, -w, -cH.normalized, e, half);

                        // A flat rim round the hole, two strips deep, following the ring's curve.
                        for (int k = 0; k < 2; k++)
                        {
                            float r0 = mouth + 1.35f * k, rm = r0 + 0.675f;
                            float side = rm * Mathf.Sin(psi);
                            Vector3 q = e * Mathf.Sqrt(InnerRadius * InnerRadius - side * side) + w * rm;
                            Vector3 up = Flat(q), fwd = (w - up * Vector3.Dot(w, up)).normalized;
                            B("Mouth Rim", q - up * 0.5f, new Vector3(2f * (r0 + 1.35f) * Mathf.Tan(half) + 0.05f, 1f, 1.4f), k == 0 ? MatDark : MatInnerHull,
                                worldRot: PavilionLayout.Basis(fwd, up), world: true);
                        }
                        if (i % 2 == 0)
                        {
                            Vector3 q = e * Mathf.Sqrt(InnerRadius * InnerRadius - Mathf.Pow((mouth + 0.15f) * Mathf.Sin(psi), 2f)) + w * (mouth + 0.15f);
                            B("Mouth Glow", q + Flat(q) * 0.01f, new Vector3(0.6f, 0.02f, 0.12f), MatHazard, collide: false,
                                worldRot: PavilionLayout.Basis(w, Flat(q)), world: true);
                        }
                    }

                    // A steel collar round the hole in the glass.
                    float aT = Mathf.Atan2(mouth, sH) * Mathf.Rad2Deg;
                    for (float a0 = aT - 0.3f; a0 < HubHoleAngle; a0 += 2.5f)
                    {
                        float a1 = a0 + 2.5f, am = (a0 + a1) * 0.5f * Mathf.Deg2Rad;
                        float ring = (HubRadius + 0.5f) * Mathf.Sin(a1 * Mathf.Deg2Rad);
                        int n = Mathf.Max(12, Mathf.CeilToInt(2f * Mathf.PI * ring / 1f));
                        for (int j = 0; j < n; j++)
                        {
                            float psi = (j + 0.5f) * 2f * Mathf.PI / n;
                            Vector3 w = Vector3.forward * Mathf.Cos(psi) + along * Mathf.Sin(psi);
                            Vector3 d = e * Mathf.Cos(am) + w * Mathf.Sin(am), meridian = -e * Mathf.Sin(am) + w * Mathf.Cos(am);
                            B("Collar", d * (HubRadius + 0.25f), new Vector3(2f * ring * Mathf.Tan(Mathf.PI / n) + 0.05f, 0.5f, 2f * (HubRadius + 0.5f) * Mathf.Tan(1.25f * Mathf.Deg2Rad) + 0.05f),
                                MatSteel, worldRot: PavilionLayout.Basis(meridian, -d), world: true);
                        }
                    }

                    TubeSurface(field, e, along);
                    Layout.Lights.Add(new PavilionLight { Position = e * ((sH + InnerRadius) * 0.5f), Range = 9f, Intensity = 1.4f, Color = new Color(0.6f, 0.85f, 1f) });
                    Layout.Lights.Add(new PavilionLight { Position = e * (InnerRadius + 2f), Range = 7f, Intensity = 1.2f, Color = new Color(1f, 0.85f, 0.4f) });
                }
            }

            /// <summary>The smooth surface you actually walk on through a tube: one sheet from the
            /// ring's lip, along the wall, round the hub's lip, at 144 steps round. With flat strips the
            /// floor tilts against gravity (which points straight out from the centre line) and nudges
            /// you sideways; this is round to within a degree or two. The glass windows are their own
            /// surface, so shots pass through them.</summary>
            void TubeSurface(SpinField field, Vector3 e, Vector3 along)
            {
                const int Around = 144, LipRows = 12, WallRows = 4;
                float mouth = field.MouthRadius, sH = field.HubLipCentre, l = LipRadius;
                var steel = new PavilionSurface { Name = $"{frameName}: Tube", Material = (PavilionMat)MatSteel };
                var glass = new PavilionSurface { Name = $"{frameName}: Glass", Material = (PavilionMat)MatGlass };
                static Vector3 Slerp(Vector3 d0, Vector3 d1, float t)
                {
                    float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d0, d1), -1f, 1f));
                    return th < 1e-4f ? d0 : (Mathf.Sin((1f - t) * th) * d0 + Mathf.Sin(t * th) * d1) / Mathf.Sin(th);
                }

                // The profile at one angle round: (point, normal, wall?) from the ring's edge to the glass.
                var columns = new List<List<(Vector3 p, Vector3 n, bool wall)>>();
                for (int j = 0; j <= Around; j++)
                {
                    float psi = j * 2f * Mathf.PI / Around;
                    Vector3 w = Vector3.forward * Mathf.Cos(psi) + along * Mathf.Sin(psi);
                    Vector3 cR = e * field.RingLipCentre(mouth * Mathf.Sin(psi)) + w * mouth, cH = e * sH + w * mouth;
                    var col = new List<(Vector3, Vector3, bool)>();
                    for (int r = 0; r <= LipRows; r++) { var d = Slerp(e, -w, r / (float)LipRows); col.Add((cR + d * l, d, false)); }
                    float s0 = Vector3.Dot(cR, e);
                    for (int r = 1; r <= WallRows; r++) col.Add((e * Mathf.Lerp(s0, sH, r / (float)WallRows) + w * TubeRadius, -w, true));
                    for (int r = 1; r <= LipRows; r++) { var d = Slerp(-w, -cH.normalized, r / (float)LipRows); col.Add((cH + d * l, d, false)); }
                    columns.Add(col);
                }

                int rows = columns[0].Count;
                foreach (var surface in new[] { steel, glass })
                {
                    for (int j = 0; j <= Around; j++)
                        for (int r = 0; r < rows; r++)
                        {
                            surface.Vertices.Add(columns[j][r].p);
                            surface.Normals.Add(columns[j][r].n);
                            surface.UVs.Add(new Vector2(j / 8f, r / 4f));
                        }
                }
                for (int j = 0; j < Around; j++)
                    for (int r = 0; r < rows - 1; r++)
                    {
                        // Windows: every third of 24 strips round, along the straight wall only.
                        bool window = (j / (Around / 24)) % 3 == 0 && columns[j][r].wall && columns[j][r + 1].wall
                            || (j / (Around / 24)) % 3 == 0 && r == LipRows && columns[j][r + 1].wall;
                        var target = window ? glass : steel;
                        int a = j * rows + r, b = (j + 1) * rows + r, c = a + 1, d = b + 1;
                        Quad(target, a, b, c, d);
                    }
                Layout.Surfaces.Add(steel);
                Layout.Surfaces.Add(glass);
            }

            /// <summary>Two triangles, wound so they face the walking side (the vertex normals).</summary>
            static void Quad(PavilionSurface s, int a, int b, int c, int d)
            {
                void Tri(int i0, int i1, int i2)
                {
                    Vector3 n = Vector3.Cross(s.Vertices[i1] - s.Vertices[i0], s.Vertices[i2] - s.Vertices[i0]);
                    if (Vector3.Dot(n, s.Normals[i0] + s.Normals[i1] + s.Normals[i2]) >= 0f) { s.Triangles.Add(i0); s.Triangles.Add(i1); s.Triangles.Add(i2); }
                    else { s.Triangles.Add(i0); s.Triangles.Add(i2); s.Triangles.Add(i1); }
                }
                Tri(a, b, c);
                Tri(c, b, d);
            }

            /// <summary>Straight out from the axis at a point (the ring's "up" there).</summary>
            static Vector3 Flat(Vector3 p) => new Vector3(p.x, p.y, 0f).normalized;

            /// <summary>A rounded edge you walk over: flat strips at one lip radius round a core
            /// point, turning from direction d0 to d1 (the walking side), one strip of the tube's
            /// circumference wide.</summary>
            void Lip(Vector3 core, Vector3 d0, Vector3 d1, Vector3 tubeAxis, float halfSide)
            {
                const int Steps = 6;
                float theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d0, d1), -1f, 1f));
                Vector3 D(float t) => (Mathf.Sin((1f - t) * theta) * d0 + Mathf.Sin(t * theta) * d1) / Mathf.Sin(theta);
                for (int j = 0; j < Steps; j++)
                {
                    float t = (j + 0.5f) / Steps;
                    Vector3 d = D(t), tangent = (D(t + 0.5f / Steps) - D(t - 0.5f / Steps)).normalized;
                    Vector3 surface = core + d * LipRadius;
                    float fromAxis = (surface - tubeAxis * Vector3.Dot(surface, tubeAxis)).magnitude;
                    B("Lip", core + d * (LipRadius - 0.3f),
                        new Vector3(2f * (fromAxis + 0.1f) * Mathf.Tan(halfSide) + 0.05f, 0.5f, 2f * LipRadius * Mathf.Tan(theta / Steps * 0.5f) + 0.05f),
                        MatDark, worldRot: PavilionLayout.Basis(tangent, d), world: true, visible: false);
                }
            }

            // ---------------------------------------------------------------- the glass hub

            /// <summary>A glass sphere round the centre, walked on its inside (down = out from the
            /// centre): you look down through the floor at both rings. Shots go through the glass;
            /// players and bolts don't. Flat strips in bands of latitude round the spin axis, with a
            /// hole (and steel collar) where each spoke tube comes in.</summary>
            void Hub()
            {
                frameName = "Hub";
                float rOut = HubRadius + 0.5f;
                bool NearSpoke(Vector3 d) => SpokeAngles.Any(a => Vector3.Angle(d, Out(a)) < HubHoleAngle - 7.5f);
                for (int band = -8; band <= 8; band++)
                {
                    float lat = band * 10f, widest = Mathf.Cos(Mathf.Max(0f, Mathf.Abs(lat) - 5f) * Mathf.Deg2Rad);
                    int n = Mathf.Max(8, 2 * Mathf.RoundToInt(Mathf.PI * HubRadius * Mathf.Cos(lat * Mathf.Deg2Rad) / 2f));
                    float width = 2f * rOut * widest * Mathf.Tan(Mathf.PI / n) + 0.06f, height = 2f * rOut * Mathf.Tan(5f * Mathf.Deg2Rad) + 0.06f;
                    for (int j = 0; j < n; j++)
                    {
                        float lon = j * 360f / n;
                        Vector3 d = Mathf.Cos(lat * Mathf.Deg2Rad) * Out(lon) + Vector3.forward * Mathf.Sin(lat * Mathf.Deg2Rad);
                        if (NearSpoke(d)) continue;
                        Vector3 north = -Mathf.Sin(lat * Mathf.Deg2Rad) * Out(lon) + Vector3.forward * Mathf.Cos(lat * Mathf.Deg2Rad);
                        var r = PavilionLayout.Basis(north, -d);
                        B("Glass", d * (HubRadius + 0.25f), new Vector3(width, 0.5f, height), MatGlass, worldRot: r, world: true);
                        if (band == 0 || j % (n / 4) == 0)
                            B("Glass Rib", d * (HubRadius + 0.56f), band == 0 ? new Vector3(width, 0.12f, 0.14f) : new Vector3(0.14f, 0.12f, height), MatSteel, collide: false, worldRot: r, world: true);
                    }
                }
                foreach (int s in new[] { -1, 1 })
                {
                    float cap = 2f * rOut * Mathf.Tan(5f * Mathf.Deg2Rad) + 0.3f;
                    B("Glass", Vector3.forward * s * (HubRadius + 0.25f), new Vector3(cap, 0.5f, cap), MatGlass,
                        worldRot: PavilionLayout.Basis(Vector3.right, -s * Vector3.forward), world: true);
                }
                B("Hub Core", Vector3.zero, new Vector3(1.6f, 1.6f, 1.6f), MatLight, collide: false, world: true);
                Layout.Lights.Add(new PavilionLight { Position = Vector3.zero, Range = 16f, Intensity = 1.6f, Color = new Color(0.85f, 0.95f, 1f) });
            }

            // ---------------------------------------------------------------- cover and spawns

            void Cover()
            {
                // Kept within 4.5 m of the middle, so the lanes along both edges stay open all the way round.
                void Item(bool outer, float a, float x, string kind)
                {
                    RingFrame(outer, a);
                    switch (kind)
                    {
                        case "barrier":
                            B("Barrier", new Vector3(x, 0.6f, 0), new Vector3(3f, 1.2f, 0.6f), MatDark);
                            B("Barrier Stripe", new Vector3(x, 1.22f, 0), new Vector3(3f, 0.05f, 0.62f), MatHazard, collide: false);
                            break;
                        case "crate":
                            B("Crate", new Vector3(x, 0.7f, 0), new Vector3(1.4f, 1.4f, 1.4f), MatSteel);
                            break;
                        default:
                            B("Conduit", new Vector3(x, 1.75f, 0), new Vector3(1.2f, 3.5f, 1.2f), MatDark);
                            B("Conduit Band", new Vector3(x, 2.4f, 0), new Vector3(1.3f, 0.15f, 1.3f), outer ? MatOuterGlow : MatInnerGlow, collide: false);
                            break;
                    }
                }
                // Clear of the spoke mouths (the 20 degrees before each spoke's angle) and the doors.
                Item(true, 108f, 0f, "barrier"); Item(true, 140f, -3f, "crate"); Item(true, 198f, 2f, "conduit");
                Item(true, 232f, -2f, "barrier"); Item(true, 296f, 3f, "crate"); Item(true, 322f, -2f, "conduit");
                Item(true, 18f, 1f, "barrier"); Item(true, 6f, -3f, "crate"); Item(true, 62f, 2f, "conduit");
                Item(false, 286f, 0f, "barrier"); Item(false, 334f, 2.5f, "crate"); Item(false, 18f, -2f, "conduit");
                Item(false, 64f, 1f, "barrier"); Item(false, 100f, -2.5f, "crate"); Item(false, 158f, 2f, "conduit");
                Item(false, 196f, -1f, "barrier"); Item(false, 246f, 2.5f, "crate");
            }

            void Spawns()
            {
                // Outer walkway (outward gravity) and inner corridor (inward gravity): each side sees
                // the other upside down. Clear of the doors and spoke mouths.
                foreach (var (a, x) in new[] { (165f, -2f), (180f, 2f), (192f, -2f), (252f, 3f), (285f, -3f), (348f, 2f) })
                    Spawn(true, a, x);
                foreach (var (a, x) in new[] { (345f, -2f), (0f, 2f), (12f, -2f), (76f, 3f), (115f, -3f), (182f, 2f) })
                    Spawn(false, a, x);
                RingFrame(true, 152f);
                Layout.Props.Add(new PavilionMarker { Position = W(new Vector3(3f, 0.5f, 0)), Rotation = rot });
                RingFrame(false, 160f);
                Layout.Props.Add(new PavilionMarker { Position = W(new Vector3(-3f, 0.5f, 0)), Rotation = rot });
            }

            void Spawn(bool outer, float a, float x)
            {
                RingFrame(outer, a);
                Layout.Spawns.Add(new PavilionMarker { Position = W(new Vector3(x, 0.95f, 0)), Rotation = rot });
            }

            // ---------------------------------------------------------------- structure and space

            /// <summary>Struts tying the rings together (beside the walkways, not across them) and a
            /// glowing core at the hub. Scenery: nobody walks here.</summary>
            void Structure()
            {
                frameName = "Station";
                float strutZ = HalfWidth + 2.5f;
                for (int k = 0; k < 4; k++)
                {
                    float a = 45f + 90f * k;
                    Vector3 o = Out(a);
                    var r = PavilionLayout.Basis(o, Vector3.forward);
                    foreach (float z in new[] { -strutZ, strutZ })
                    {
                        float r0 = InnerRadius - 1f, r1 = OuterRadius + 1f;
                        B("Strut", o * ((r0 + r1) * 0.5f) + Vector3.forward * z, new Vector3(0.8f, 0.8f, r1 - r0), MatSteel, worldRot: r, world: true);
                        // Out from the glass hub's side (it is about 7 m from the axis at this height).
                        const float h0 = 7.5f;
                        B("Hub Spoke", o * ((h0 + r0) * 0.5f) + Vector3.forward * z, new Vector3(0.6f, 0.6f, r0 - h0), MatSteel, collide: false, worldRot: r, world: true);
                    }
                }
            }

            void Stars()
            {
                frameName = "Space";
                var random = new System.Random(47);
                int made = 0;
                while (made < 300)
                {
                    var d = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
                    float m = d.magnitude;
                    if (m < 0.2f || m > 1f) continue;
                    float r = 160f + (float)random.NextDouble() * 60f, size = 0.3f + (float)random.NextDouble() * 0.7f;
                    B("Star", d / m * r, Vector3.one * size, MatLight, collide: false, world: true);
                    made++;
                }
            }
        }
    }
}
