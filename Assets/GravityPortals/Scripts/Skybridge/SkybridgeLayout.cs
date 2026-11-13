using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// "Skybridge": a space station whose deck folded back over itself. The floor (down = -Y) and
    /// the ceiling (down = +Y) are its two halves, 40 m apart, joined round a hull bend on the +X
    /// side; the other edges are torn, open to the void. Scaffolding bridges run from floor to
    /// ceiling at random spots: on a bridge your "down" is sideways into its deck, and an airlock
    /// door at each end of its walkway (every walkway the same length) gets you on and off. A few
    /// more door pairs link the floor and ceiling directly. The floor and ceiling are laid out
    /// separately at random (reactor below, command deck above, modules, containers, conduits,
    /// barriers), so they don't match. Doors leave you upright; falling off a bridge or a plane's
    /// edge is a fall into the void.
    ///
    /// Generated from a fixed seed (same map every match). Pure data in the Pavilion's
    /// containers, built by <see cref="PavilionBuilder"/>.
    /// </summary>
    public static class SkybridgeLayout
    {
        public const int Seed = 4;
        public const float Gap = 40f;              // floor surface at y = 0, ceiling surface at y = Gap
        public const float HalfX = 32f;            // the planes are 64 wide (x)...
        public const float HalfZ = 80f;            // ...and 160 long (z)
        public const float BridgeBottom = 7f, BridgeTop = 33f;
        public const float BridgeLength = BridgeTop - BridgeBottom; // every walkway the same length
        public const int BridgeTarget = 10;

        public const int MatHullFloor = 0, MatHullCeiling = 1, MatSteel = 2, MatHazard = 3, MatDarkSteel = 4,
            MatLight = 5, MatFloorGlow = 6, MatCeilingGlow = 7, MatGrate = 8;
        public const int MaterialCount = 9;

        public static readonly string[] PlaneNames = { "Floor", "Ceiling" };

        /// <summary>The void: leave this and you've fallen. On the +X side it stops just past the
        /// planes' edge, so anyone flung toward the fold dies before reaching its curve.</summary>
        public static readonly Bounds VoidBounds = new Bounds(new Vector3(-3.5f, Gap * 0.5f, 0f), new Vector3(73f, Gap + 30f, 2f * HalfZ + 16f));

        /// <summary>The fold: the floor and ceiling are one deck bent back round this curve.</summary>
        public static readonly Vector3 FoldCentre = new Vector3(HalfX, Gap * 0.5f, 0f);
        public const float FoldRadius = Gap * 0.5f;

        static readonly Color[] PairColors =
        {
            new Color(1f, 0.55f, 0.2f), new Color(0.3f, 0.8f, 1f), new Color(1f, 0.85f, 0.3f), new Color(0.75f, 0.45f, 1f),
            new Color(0.4f, 1f, 0.6f), new Color(1f, 0.35f, 0.45f), new Color(0.95f, 0.95f, 0.65f), new Color(0.45f, 1f, 1f),
            new Color(1f, 0.6f, 0.85f), new Color(0.65f, 0.9f, 0.35f), new Color(0.6f, 0.7f, 1f), new Color(1f, 0.75f, 0.55f),
        };

        public static PavilionLayout Generate(int seed = Seed)
        {
            var g = new Generator(seed);
            g.Run();
            return g.Layout;
        }

        /// <summary>An axis-aligned rectangle on the planes (world x, z).</summary>
        struct Rect2
        {
            public float X0, X1, Z0, Z1;
            public Rect2(float x0, float x1, float z0, float z1) { X0 = Mathf.Min(x0, x1); X1 = Mathf.Max(x0, x1); Z0 = Mathf.Min(z0, z1); Z1 = Mathf.Max(z0, z1); }
            public bool Overlaps(Rect2 o, float gap = 0f) => X0 < o.X1 + gap && o.X0 < X1 + gap && Z0 < o.Z1 + gap && o.Z0 < Z1 + gap;
            public bool InsidePlane(float margin) => X0 >= -HalfX + margin && X1 <= HalfX - margin && Z0 >= -HalfZ + margin && Z1 <= HalfZ - margin;
        }

        sealed class Generator
        {
            public readonly PavilionLayout Layout = new PavilionLayout();
            readonly System.Random rng;
            string frameName;
            Vector3 origin;
            Quaternion rot;
            int plane;  // 0 = floor, 1 = ceiling (for the current frame), -1 = elsewhere

            // Taken ground: bridges (both planes, as their scaffolding spans the gap), and each
            // plane's own items and door fronts.
            readonly List<Rect2> bridgeRects = new List<Rect2>();
            readonly List<Rect2>[] planeRects = { new List<Rect2>(), new List<Rect2>() };
            readonly List<(PavilionPortal a, PavilionPortal b)> pairs = new List<(PavilionPortal, PavilionPortal)>();

            public Generator(int seed)
            {
                rng = new System.Random(seed);
                Layout.Seed = seed;
            }

            float Value() => (float)rng.NextDouble();
            float Range(float a, float b) => a + (b - a) * Value();
            int Int(int a, int bExclusive) => rng.Next(a, bExclusive);

            Vector3 W(Vector3 local) => origin + rot * local;
            void Frame(string name, Vector3 o, Quaternion r, int p = -1) { frameName = name; origin = o; rot = r; plane = p; }

            void B(string name, Vector3 c, Vector3 s, int mat, bool collide = true, Quaternion? localRot = null)
            {
                Layout.Boxes.Add(new PavilionBox
                {
                    Name = $"{frameName}: {name}", Center = W(c), Size = s, Rotation = rot * (localRot ?? Quaternion.identity),
                    Material = (PavilionMat)mat, Collide = collide, Visible = true, BreakId = -1,
                });
            }

            static Quaternion AxisAngle(Vector3 axis, float degrees)
            {
                float a = degrees * 0.5f * Mathf.Deg2Rad, s = Mathf.Sin(a);
                axis = axis.normalized;
                return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(a));
            }

            static Quaternion Conj(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

            PavilionPortal Door(string name, Vector3 position, Vector3 normal, Vector2 size) => new PavilionPortal
            {
                Name = $"{frameName} {name}", Position = W(position), Normal = rot * normal, Up = rot * Vector3.up,
                Size = size, Doorway = true, Link = -1,
            };

            // The ceiling's frame is the floor's turned 180 on Z: local x = -world x, local z = world z.
            static Quaternion PlaneRot(int p) => p == 0 ? Quaternion.identity : new Quaternion(0f, 0f, 1f, 0f);
            static Vector3 PlaneOrigin(int p) => new Vector3(0f, p == 0 ? 0f : Gap, 0f);
            void PlaneFrame(int p) => Frame(PlaneNames[p], PlaneOrigin(p), PlaneRot(p), p);
            /// <summary>World x, z (and a height off the plane) to the plane's local frame.</summary>
            static Vector3 Local(int p, float x, float h, float z) => new Vector3(p == 0 ? x : -x, h, z);

            public void Run()
            {
                for (int p = 0; p < 2; p++) { PlaneFrame(p); Hull(p); }
                Bridges();
                for (int p = 0; p < 2; p++) { PlaneFrame(p); Furnish(p); }
                DirectDoors();
                Fold();
                Stars();
                Link();
            }

            void Link()
            {
                for (int i = 0; i < pairs.Count; i++)
                {
                    var (a, b) = pairs[i];
                    a.Color = b.Color = PairColors[i % PairColors.Length];
                    int ia = Layout.Portals.Count;
                    a.Link = ia + 1;
                    b.Link = ia;
                    Layout.Portals.Add(a);
                    Layout.Portals.Add(b);
                }
            }

            // ---------------------------------------------------------------- hull and edges

            void Hull(int p)
            {
                int surface = p == 0 ? MatHullFloor : MatHullCeiling, glow = p == 0 ? MatFloorGlow : MatCeilingGlow;
                B("Hull", new Vector3(0, -0.5f, 0), new Vector3(2 * HalfX, 1f, 2 * HalfZ), surface);
                for (float a = -HalfX + 8f; a < HalfX; a += 8f)
                    B("Hull Seam", new Vector3(a, 0.01f, 0), new Vector3(0.08f, 0.02f, 2 * HalfZ), glow, collide: false);
                for (float a = -HalfZ + 8f; a < HalfZ; a += 8f)
                    B("Hull Seam", new Vector3(0, 0.01f, a), new Vector3(2 * HalfX, 0.02f, 0.08f), glow, collide: false);

                Vector3[] sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                foreach (var outward in sides)
                {
                    bool alongZ = outward.x != 0f;
                    float half = alongZ ? HalfX : HalfZ, length = 2f * (alongZ ? HalfZ : HalfX);
                    Vector3 along = alongZ ? Vector3.forward : Vector3.right;
                    Vector3 Size(float l, float hgt, float t) => alongZ ? new Vector3(t, hgt, l) : new Vector3(l, hgt, t);
                    if (Vector3.Dot(rot * outward, Vector3.right) > 0.9f)
                    {
                        // The fold side: a barrier keeps you off the bend.
                        B("Fold Barrier", outward * (half - 0.3f) + Vector3.up * 0.6f, Size(length, 1.2f, 0.4f), MatDarkSteel);
                        B("Fold Barrier Stripe", outward * (half - 0.3f) + Vector3.up * 1.22f, Size(length, 0.05f, 0.42f), MatHazard, collide: false);
                        continue;
                    }
                    // A torn edge: a low lip, then ragged plates and snapped beams over the void.
                    B("Lip", outward * (half - 0.2f) + Vector3.up * 0.2f, Size(length - 0.8f, 0.4f, 0.4f), MatDarkSteel);
                    B("Edge Glow", outward * (half - 0.2f) + Vector3.up * 0.42f, Size(length - 0.8f, 0.04f, 0.2f), glow, collide: false);
                    for (float a = -length * 0.5f + 2f; a < length * 0.5f - 1f; a += 3f + Value() * 3f)
                    {
                        float len = 1.5f + Value() * 2.5f, wide = 2f + Value() * 3f, tilt = 10f + Value() * 30f;
                        B("Torn Plate", outward * (half + len * 0.5f - 0.2f) + along * a + Vector3.down * (0.5f + len * 0.15f),
                            Size(wide, 0.3f, len), surface, collide: false, localRot: AxisAngle(Vector3.Cross(outward, Vector3.up), tilt));
                        if (Value() < 0.5f)
                            B("Snapped Beam", outward * (half + 1.2f) + along * (a + wide * 0.5f) + Vector3.down * 0.9f,
                                Size(0.35f, 0.35f, 2.6f), MatSteel, collide: false, localRot: AxisAngle(along, Value() * 30f - 15f));
                    }
                }
            }

            // ---------------------------------------------------------------- bridges

            static readonly Vector3[] Normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };

            /// <summary>Ground a bridge takes (both planes): the truss behind its deck, the walkway
            /// and handrails, and its doors with room in front of them.</summary>
            static Rect2 BridgeRect(Vector3 deck, Vector3 n)
            {
                Vector3 across = new Vector3(Mathf.Abs(n.z), 0f, Mathf.Abs(n.x));
                Vector3 a = deck - n * 1.7f - across * 1.9f, b = deck + n * 10.6f + across * 1.9f;
                return new Rect2(a.x, b.x, a.z, b.z);
            }

            /// <summary>Where someone stepping off the bridge's side falls (down = -n), out to the void.</summary>
            static Rect2[] FallRects(Vector3 deck, Vector3 n)
            {
                Vector3 across = new Vector3(Mathf.Abs(n.z), 0f, Mathf.Abs(n.x));
                var b = VoidBounds;
                float reach = n.x > 0.5f ? deck.x - b.min.x : n.x < -0.5f ? b.max.x - deck.x : n.z > 0.5f ? deck.z - b.min.z : b.max.z - deck.z;
                var rects = new Rect2[2];
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    Vector3 p0 = deck + n * 1f + across * side * 1.9f, p1 = deck - n * reach + across * side * 3.4f;
                    rects[s] = new Rect2(p0.x, p1.x, p0.z, p1.z);
                }
                return rects;
            }

            void Bridges()
            {
                var placed = new List<(Vector3 deck, Vector3 n)>();
                for (int attempt = 0; attempt < 600 && placed.Count < BridgeTarget; attempt++)
                {
                    Vector3 n = Normals[Int(0, 4)];
                    var deck = new Vector3(Mathf.Round(Range(-HalfX + 3f, HalfX - 3f)), 0f, Mathf.Round(Range(-HalfZ + 3f, HalfZ - 3f)));
                    var rect = BridgeRect(deck, n);
                    if (!rect.InsidePlane(2.5f)) continue;
                    bool ok = true;
                    foreach (var r in bridgeRects) if (r.Overlaps(rect, 3f)) { ok = false; break; }
                    if (!ok) continue;
                    // Falling off this bridge mustn't land on another, nor anyone falling off another land on this.
                    foreach (var f in FallRects(deck, n)) foreach (var r in bridgeRects) if (f.Overlaps(r)) ok = false;
                    foreach (var other in placed) foreach (var f in FallRects(other.deck, other.n)) if (f.Overlaps(rect)) ok = false;
                    if (!ok) continue;
                    bridgeRects.Add(rect);
                    placed.Add((deck, n));
                }
                for (int i = 0; i < placed.Count; i++) Bridge(i, placed[i].deck, placed[i].n);
            }

            /// <summary>A scaffolding bridge from floor to ceiling whose deck faces <paramref name="n"/>:
            /// standing on it, down is -n. Its walkway runs between airlock bulkheads at y = 7 and 33,
            /// with a door on each; partner doors stand on the floor and ceiling just beyond its base.</summary>
            void Bridge(int index, Vector3 deck, Vector3 n)
            {
                // Bridge frame: origin on the deck at floor level, up = n (off the deck), forward = +Y.
                Frame($"Bridge {index}", deck, PavilionLayout.Basis(Vector3.up, n));
                float H = Gap, w0 = BridgeBottom, w1 = BridgeTop;
                B("Deck", new Vector3(0, -0.075f, (w0 + w1) * 0.5f), new Vector3(3.2f, 0.15f, w1 - w0), MatGrate);
                // The deck carries on behind the bulkheads down to the floor and up to the ceiling.
                B("Deck Lower", new Vector3(0, -0.075f, w0 * 0.5f), new Vector3(3.2f, 0.15f, w0 - 0.4f), MatGrate);
                B("Deck Upper", new Vector3(0, -0.075f, (w1 + H) * 0.5f + 0.2f), new Vector3(3.2f, 0.15f, H - w1 - 0.4f), MatGrate);
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = s * 1.45f;
                    B("Deck Edge Tube", new Vector3(s * 1.5f, -0.2f, H * 0.5f), new Vector3(0.22f, 0.22f, H), MatSteel);
                    B("Bottom Chord", new Vector3(s * 1.4f, -1.3f, H * 0.5f), new Vector3(0.22f, 0.22f, H), MatSteel);
                    B("Handrail", new Vector3(s * 1.5f, 1.05f, H * 0.5f), new Vector3(0.1f, 0.1f, H), MatSteel);
                    B("Mid Rail", new Vector3(s * 1.5f, 0.55f, H * 0.5f), new Vector3(0.08f, 0.08f, H), MatSteel);
                    for (float z = 0f; z <= H + 0.01f; z += 2f)
                    {
                        B("Truss Vertical", new Vector3(x, -0.75f, z), new Vector3(0.16f, 1.1f, 0.16f), MatSteel);
                        B("Rail Post", new Vector3(s * 1.5f, 0.52f, z), new Vector3(0.1f, 1.05f, 0.1f), MatSteel);
                        if (z < H - 0.5f)
                        {
                            float brace = Mathf.Atan2(1.1f, 2f) * Mathf.Rad2Deg * ((int)(z / 2f) % 2 == 0 ? 1f : -1f);
                            B("Truss Brace", new Vector3(x, -0.75f, z + 1f), new Vector3(0.12f, 0.12f, 2.28f), MatSteel, localRot: AxisAngle(Vector3.right, brace));
                        }
                        if (s < 0) B("Cross Bar", new Vector3(0, -1.3f, z), new Vector3(2.9f, 0.12f, 0.12f), MatSteel);
                    }
                    for (float z = w0 + 3f; z < w1 - 1f; z += 6.5f)
                        B("Work Light", new Vector3(s * 1.5f, 1.2f, z), new Vector3(0.25f, 0.2f, 0.25f), MatLight, collide: false);
                }
                // Feet where the scaffolding meets the floor and the ceiling.
                foreach (float z in new[] { 0.15f, H - 0.15f })
                    B("Scaffold Foot", new Vector3(0, -0.7f, z), new Vector3(3.6f, 1.6f, 0.3f), MatDarkSteel);

                // Airlock bulkheads at both ends of the walkway, the doors on their inner faces.
                B("Bottom Bulkhead", new Vector3(0, 2f, w0 - 0.2f), new Vector3(3.2f, 4f, 0.4f), MatDarkSteel);
                B("Top Bulkhead", new Vector3(0, 2f, w1 + 0.2f), new Vector3(3.2f, 4f, 0.4f), MatDarkSteel);
                foreach (float z in new[] { w0 + 0.03f, w1 - 0.03f })
                {
                    for (int s = -1; s <= 1; s += 2)
                        B("Airlock Jamb", new Vector3(s * 1.4f, 1.75f, z), new Vector3(0.2f, 3.5f, 0.05f), MatHazard, collide: false);
                    B("Airlock Light", new Vector3(0, 3.75f, z), new Vector3(2.8f, 0.2f, 0.05f), MatLight, collide: false);
                }
                var bottom = Door("Bottom Door", new Vector3(0, 1.7f, w0 + 0.01f), Vector3.forward, new Vector2(2.4f, 3.4f));
                var top = Door("Top Door", new Vector3(0, 1.7f, w1 - 0.01f), Vector3.back, new Vector2(2.4f, 3.4f));
                Layout.Lights.Add(new PavilionLight { Position = W(new Vector3(0, 3f, (w0 + w1) * 0.5f)), Range = 16f, Intensity = 1.5f, Color = new Color(0.85f, 0.9f, 1f) });

                // Partner doors beyond the base: on the floor and on the ceiling, facing away from
                // the bridge (walk toward the bridge, into the door, and you're on its walkway).
                Vector3 spot = deck + n * 7f;
                PlaneFrame(0);
                var floorDoor = StandingDoor($"Bridge {index} Door", Local(0, spot.x, 0f, spot.z), Conj(rot) * n);
                PlaneFrame(1);
                var ceilingDoor = StandingDoor($"Bridge {index} Door", Local(1, spot.x, 0f, spot.z), Conj(rot) * n);
                pairs.Add((floorDoor, bottom));
                pairs.Add((top, ceilingDoor));
            }

            /// <summary>An airlock frame standing on a plane, the portal on its front.</summary>
            PavilionPortal StandingDoor(string name, Vector3 baseCenter, Vector3 normal)
            {
                Vector3 n = normal.normalized;
                Vector3 across = Vector3.Cross(Vector3.up, n);
                Vector3 Size(float w, float hgt, float t) => new Vector3(Mathf.Abs(across.x) * w + Mathf.Abs(n.x) * t, hgt, Mathf.Abs(across.z) * w + Mathf.Abs(n.z) * t);
                B($"{name} Bulkhead", baseCenter + Vector3.up * 2f - n * 0.2f, Size(3.2f, 4f, 0.4f), MatDarkSteel);
                for (int s = -1; s <= 1; s += 2)
                    B($"{name} Jamb", baseCenter + across * (s * 1.4f) + Vector3.up * 1.75f + n * 0.03f, Size(0.2f, 3.5f, 0.06f), MatHazard, collide: false);
                B($"{name} Light Bar", baseCenter + Vector3.up * 3.75f + n * 0.03f, Size(2.8f, 0.2f, 0.06f), MatLight, collide: false);
                return Door(name, baseCenter + Vector3.up * 1.7f + n * 0.01f, n, new Vector2(2.4f, 3.4f));
            }

            // ---------------------------------------------------------------- furnishing a plane (at random)

            bool Free(int p, Rect2 r, float gap)
            {
                if (!r.InsidePlane(1.2f)) return false;
                foreach (var b in bridgeRects) if (b.Overlaps(r, gap)) return false;
                foreach (var o in planeRects[p]) if (o.Overlaps(r, gap)) return false;
                return true;
            }

            /// <summary>Finds a free spot for something of local half-size (hx, hz) that can turn in
            /// quarter turns. Returns false if nothing fits.</summary>
            bool Spot(int p, float hx, float hz, float gap, out float x, out float z, out int turn, float near = 1f)
            {
                for (int t = 0; t < 120; t++)
                {
                    turn = Int(0, 4);
                    bool swap = turn % 2 == 1;
                    float ex = swap ? hz : hx, ez = swap ? hx : hz;
                    x = Mathf.Round(Range(-HalfX * near + ex, HalfX * near - ex) * 2f) * 0.5f;
                    z = Mathf.Round(Range(-HalfZ * near + ez, HalfZ * near - ez) * 2f) * 0.5f;
                    var r = new Rect2(x - ex, x + ex, z - ez, z + ez);
                    if (!Free(p, r, gap)) continue;
                    planeRects[p].Add(r);
                    return true;
                }
                x = z = 0f;
                turn = 0;
                return false;
            }

            /// <summary>Places a thing at world (x, z) on plane p, built in its own frame turned by
            /// <paramref name="turn"/> quarter turns (so a plane's furniture reads the same either way up).</summary>
            void Thing(int p, float x, float z, int turn, System.Action<System.Func<Vector3, Vector3>, System.Func<Vector3, Vector3>, System.Func<Vector3, Vector3>> build)
            {
                float a = turn * 45f * Mathf.Deg2Rad;
                var q = new Quaternion(0f, Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 c = Local(p, x, 0f, z);
                Vector3 P(Vector3 v) => c + q * v;
                Vector3 D(Vector3 v) => q * v;
                Vector3 S(Vector3 v) => turn % 2 == 1 ? new Vector3(v.z, v.y, v.x) : v;
                build(P, D, S);
            }

            void Furnish(int p)
            {
                int glow = p == 0 ? MatFloorGlow : MatCeilingGlow;
                Color light = p == 0 ? new Color(1f, 0.72f, 0.48f) : new Color(0.55f, 0.78f, 1f);

                // A centrepiece: the reactor on the floor, the command deck on the ceiling.
                float cx = 0f, cz = 0f; int ct;
                if (p == 0 && Spot(p, 5f, 5f, 3f, out cx, out cz, out ct, 0.6f)) Thing(p, cx, cz, ct, (P, D, S) => Reactor(P, S, glow));
                if (p == 1 && Spot(p, 6f, 4f, 3f, out cx, out cz, out ct, 0.6f)) Thing(p, cx, cz, ct, (P, D, S) => CommandDeck(P, S, glow));
                Layout.Lights.Add(new PavilionLight { Position = W(Local(p, cx, 7f, cz)), Range = 22f, Intensity = 2.6f, Color = light });

                int habs = 4 + Int(0, 3), containers = 7 + Int(0, 4), conduits = 8 + Int(0, 5), barriers = 10 + Int(0, 5), crates = 6 + Int(0, 4);
                for (int i = 0; i < habs; i++)
                    if (Spot(p, 4.2f, 3.4f, 2.5f, out float x, out float z, out int t))
                    {
                        Thing(p, x, z, t, (P, D, S) => Hab(P, D, S, glow));
                        Layout.Lights.Add(new PavilionLight { Position = W(Local(p, x, 7f, z)), Range = 14f, Intensity = 1.8f, Color = light });
                    }
                for (int i = 0; i < containers; i++)
                    if (Spot(p, 3.1f, 1.3f, 1.5f, out float x, out float z, out int t))
                    {
                        bool stacked = Value() < 0.35f, orange = Value() < 0.4f;
                        Thing(p, x, z, t, (P, D, S) => Container(P, S, stacked, orange));
                    }
                for (int i = 0; i < conduits; i++)
                    if (Spot(p, 0.7f, 0.7f, 2f, out float x, out float z, out int t))
                        Thing(p, x, z, t, (P, D, S) =>
                        {
                            B("Conduit Stack", P(new Vector3(0, 2.5f, 0)), new Vector3(1.2f, 5f, 1.2f), MatDarkSteel);
                            B("Conduit Band", P(new Vector3(0, 1.5f, 0)), new Vector3(1.3f, 0.15f, 1.3f), glow, collide: false);
                            B("Conduit Band", P(new Vector3(0, 3.5f, 0)), new Vector3(1.3f, 0.15f, 1.3f), glow, collide: false);
                        });
                for (int i = 0; i < barriers; i++)
                    if (Spot(p, 2.1f, 0.4f, 1.5f, out float x, out float z, out int t))
                        Thing(p, x, z, t, (P, D, S) =>
                        {
                            B("Cargo Barrier", P(new Vector3(0, 0.6f, 0)), S(new Vector3(4f, 1.2f, 0.6f)), MatDarkSteel);
                            B("Barrier Stripe", P(new Vector3(0, 1.22f, 0)), S(new Vector3(4f, 0.05f, 0.62f)), MatHazard, collide: false);
                        });
                for (int i = 0; i < crates; i++)
                    if (Spot(p, 0.8f, 0.8f, 1.2f, out float x, out float z, out int t))
                    {
                        bool two = Value() < 0.4f;
                        Thing(p, x, z, t, (P, D, S) =>
                        {
                            B("Crate", P(new Vector3(0, 0.7f, 0)), new Vector3(1.4f, 1.4f, 1.4f), MatSteel);
                            if (two) B("Crate", P(new Vector3(0, 2.1f, 0)), new Vector3(1.4f, 1.4f, 1.4f), MatHazard);
                        });
                    }

                // General lighting down the length, so the long deck isn't dark between things.
                for (float z = -HalfZ + 12f; z < HalfZ; z += 24f)
                    Layout.Lights.Add(new PavilionLight { Position = W(Local(p, 0f, 10f, z)), Range = 24f, Intensity = 1.4f, Color = light });

                // Spawns: open spots, facing down the deck toward the middle.
                int spawns = 0;
                for (int t = 0; t < 200 && spawns < 6; t++)
                {
                    float x = Mathf.Round(Range(-HalfX + 3f, HalfX - 3f)), z = Mathf.Round(Range(-HalfZ + 4f, HalfZ - 4f));
                    var r = new Rect2(x - 1.5f, x + 1.5f, z - 1.5f, z + 1.5f);
                    if (!Free(p, r, 1f)) continue;
                    planeRects[p].Add(r);
                    Vector3 facing = Local(p, 0f, 0f, z > 0f ? -1f : 1f);
                    Layout.Spawns.Add(new PavilionMarker { Position = W(Local(p, x, 0.95f, z)), Rotation = rot * PavilionLayout.Basis(facing, Vector3.up) });
                    spawns++;
                }
                int props = 0;
                for (int t = 0; t < 100 && props < 3; t++)
                {
                    float x = Mathf.Round(Range(-HalfX + 3f, HalfX - 3f)), z = Mathf.Round(Range(-HalfZ + 4f, HalfZ - 4f));
                    var r = new Rect2(x - 1f, x + 1f, z - 1f, z + 1f);
                    if (!Free(p, r, 0.8f)) continue;
                    planeRects[p].Add(r);
                    Layout.Props.Add(new PavilionMarker { Position = W(Local(p, x, 0.5f, z)), Rotation = rot });
                    props++;
                }
            }

            void Reactor(System.Func<Vector3, Vector3> P, System.Func<Vector3, Vector3> S, int glow)
            {
                B("Reactor Deck", P(new Vector3(0, 0.6f, 0)), new Vector3(8f, 1.2f, 8f), MatDarkSteel);
                B("Reactor Ring", P(new Vector3(0, 1.22f, 0)), new Vector3(4.2f, 0.04f, 4.2f), glow, collide: false);
                B("Reactor Core", P(new Vector3(0, 2.7f, 0)), new Vector3(1.8f, 3f, 1.8f), glow);
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        B("Core Cage", P(new Vector3(x * 1.1f, 2.8f, z * 1.1f)), new Vector3(0.2f, 3.2f, 0.2f), MatSteel);
                B("Core Cap", P(new Vector3(0, 4.35f, 0)), new Vector3(2.6f, 0.3f, 2.6f), MatSteel);
                // Barriers round it, open on two sides.
                B("Barrier", P(new Vector3(-4.6f, 0.7f, 2f)), S(new Vector3(0.6f, 1.4f, 4f)), MatSteel);
                B("Barrier", P(new Vector3(4.6f, 0.7f, -2f)), S(new Vector3(0.6f, 1.4f, 4f)), MatSteel);
            }

            void CommandDeck(System.Func<Vector3, Vector3> P, System.Func<Vector3, Vector3> S, int glow)
            {
                B("Command Deck", P(new Vector3(0, 0.75f, 0)), S(new Vector3(10f, 1.5f, 6f)), MatDarkSteel);
                B("Command Deck Edge", P(new Vector3(0, 1.52f, 0)), S(new Vector3(10.1f, 0.05f, 6.1f)), MatHazard, collide: false);
                for (int i = -1; i <= 1; i++)
                {
                    B("Console", P(new Vector3(i * 3f, 2f, 2f)), S(new Vector3(2f, 1f, 0.8f)), MatSteel);
                    B("Console Screen", P(new Vector3(i * 3f, 2.52f, 2f)), S(new Vector3(1.8f, 0.04f, 0.6f)), glow, collide: false);
                }
                B("Mast", P(new Vector3(-4f, 3.5f, -2f)), new Vector3(0.3f, 4f, 0.3f), MatSteel);
                B("Mast Light", P(new Vector3(-4f, 5.6f, -2f)), new Vector3(0.35f, 0.35f, 0.35f), glow, collide: false);
            }

            /// <summary>A habitation module (6 x 4 x 6) with an outside stair and landing on its +x
            /// side; its own frame spans x -3..5.4, z -3.8..3 round its spot (centre offset 1.2, -0.4).</summary>
            void Hab(System.Func<Vector3, Vector3> P, System.Func<Vector3, Vector3> D, System.Func<Vector3, Vector3> S, int glow)
            {
                Vector3 o = new Vector3(-1.2f, 0f, 0.4f); // module centre within the spot
                B("Hab Module", P(o + new Vector3(0, 2f, 0)), new Vector3(6f, 4f, 6f), MatSteel);
                B("Hab Roof Edge", P(o + new Vector3(0, 4.05f, 0)), new Vector3(6.1f, 0.1f, 6.1f), MatHazard, collide: false);
                for (int s = -1; s <= 1; s += 2)
                {
                    B("Window Strip", P(o + new Vector3(0, 2.6f, s * 3.03f)), S(new Vector3(5f, 0.45f, 0.05f)), MatLight, collide: false);
                    B("Window Strip", P(o + new Vector3(s * 3.03f, 2.6f, 0)), S(new Vector3(0.05f, 0.45f, 5f)), MatLight, collide: false);
                }
                B("Antenna", P(o + new Vector3(-2.4f, 5.4f, -2.4f)), new Vector3(0.12f, 2.8f, 0.12f), MatSteel, collide: false);
                B("Antenna Light", P(o + new Vector3(-2.4f, 6.9f, -2.4f)), new Vector3(0.25f, 0.25f, 0.25f), glow, collide: false);
                // Rails round the roof except the stair side (+x).
                Rail(P(o + new Vector3(-3f, 4f, 0)), 6f, D(Vector3.forward));
                Rail(P(o + new Vector3(0, 4f, 3f)), 6f, D(Vector3.right));
                Rail(P(o + new Vector3(0, 4f, -3f)), 6f, D(Vector3.right));
                // Stair up the +x side, climbing toward +z, then a landing level with the roof.
                Stair(P(o + new Vector3(4.2f, 0f, -3f)), D(Vector3.forward), 4f, 2.4f);
                B("Landing", P(o + new Vector3(4.2f, 3.8f, 1.8f)), S(new Vector3(2.4f, 0.4f, 1.6f)), MatSteel);
                Rail(P(o + new Vector3(4.2f, 4f, 2.6f)), 2.4f, D(Vector3.right));
                Rail(P(o + new Vector3(5.4f, 4f, 1.8f)), 1.6f, D(Vector3.forward));
            }

            void Container(System.Func<Vector3, Vector3> P, System.Func<Vector3, Vector3> S, bool stacked, bool orange)
            {
                int mat = orange ? MatHazard : MatDarkSteel;
                B("Container", P(new Vector3(0, 1.3f, 0)), S(new Vector3(6f, 2.6f, 2.4f)), mat);
                for (float x = -2.4f; x <= 2.41f; x += 1.2f)
                    B("Container Rib", P(new Vector3(x, 1.3f, 0)), S(new Vector3(0.1f, 2.5f, 2.5f)), MatSteel, collide: false);
                if (stacked) B("Container", P(new Vector3(0.3f, 3.9f, 0)), S(new Vector3(6f, 2.6f, 2.4f)), orange ? MatDarkSteel : MatHazard);
            }

            void Rail(Vector3 baseCenter, float length, Vector3 along)
            {
                Vector3 Size(float l, float hgt, float t) => Mathf.Abs(along.x) > 0.5f ? new Vector3(l, hgt, t) : new Vector3(t, hgt, l);
                B("Rail", baseCenter + Vector3.up * 0.95f, Size(length, 0.1f, 0.12f), MatSteel);
                for (float a = -length * 0.5f + 0.3f; a < length * 0.5f; a += 2f)
                    B("Rail Post", baseCenter + along * a + Vector3.up * 0.47f, Size(0.12f, 0.95f, 0.12f), MatSteel);
            }

            void Stair(Vector3 bottom, Vector3 run, float rise, float width)
            {
                const float step = 0.25f, thickness = 0.3f;
                int count = Mathf.RoundToInt(rise / step);
                Vector3 up = Vector3.up, side = Vector3.Cross(up, run);
                Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
                for (int i = 0; i < count; i++)
                {
                    float v0 = i * step - 0.4f, v1 = (i + 1) * step;
                    Vector3 c = bottom + run * ((i + 0.5f) * step) + up * ((v0 + v1) * 0.5f);
                    B($"Step {i + 1}", c, Abs(side) * width + up * (v1 - v0) + Abs(run) * step, i % 2 == 0 ? MatSteel : MatDarkSteel, collide: i == count - 1);
                }
                Vector3 a = bottom - run * step, b = bottom + run * (rise - step) + up * rise;
                Vector3 nrm = (up - run).normalized;
                Layout.Boxes.Add(new PavilionBox
                {
                    Name = $"{frameName}: Walkable Ramp (hidden)", Center = W((a + b) * 0.5f - nrm * thickness * 0.5f),
                    Size = new Vector3(width, thickness, (b - a).magnitude),
                    Rotation = rot * PavilionLayout.Basis((b - a).normalized, nrm),
                    Collide = true, Visible = false, StairRamp = true, StairOrigin = W(bottom), BreakId = -1,
                });
            }

            /// <summary>Four door pairs straight between the floor and the ceiling, at random spots.</summary>
            void DirectDoors()
            {
                var floorDoors = new List<PavilionPortal>();
                var ceilingDoors = new List<PavilionPortal>();
                for (int p = 0; p < 2; p++)
                {
                    PlaneFrame(p);
                    var list = p == 0 ? floorDoors : ceilingDoors;
                    for (int t = 0; t < 300 && list.Count < 4; t++)
                    {
                        // A door panel plus clear ground in front: spot 3.2 wide, 4.5 deep (front along local +z).
                        if (!Spot(p, 1.8f, 2.4f, 1.5f, out float x, out float z, out int turn)) break;
                        float a = turn * 45f * Mathf.Deg2Rad;
                        var q = new Quaternion(0f, Mathf.Sin(a), 0f, Mathf.Cos(a));
                        Vector3 c = Local(p, x, 0f, z);
                        Vector3 n = q * Vector3.forward;
                        list.Add(StandingDoor($"Plane Door {list.Count}", c - n * 1.8f, n));
                    }
                }
                for (int i = 0; i < Mathf.Min(floorDoors.Count, ceilingDoors.Count); i++)
                    pairs.Add((floorDoors[i], ceilingDoors[(i + 1) % ceilingDoors.Count]));
            }

            // ---------------------------------------------------------------- the fold and space

            void Fold()
            {
                Frame("Fold", Vector3.zero, Quaternion.identity);
                const int segments = 18;
                const float t = 1f;
                float R = FoldRadius, step = 180f / segments;
                float chord = 2f * (R + t) * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) + 0.15f;
                for (int i = 0; i < segments; i++)
                {
                    float theta = -90f + (i + 0.5f) * step, rad = theta * Mathf.Deg2Rad;
                    Vector3 radial = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
                    var spin = AxisAngle(Vector3.forward, theta + 90f); // local x along the curve, local y inward
                    bool lower = i < segments / 2;
                    B("Fold Hull", FoldCentre + radial * (R + t * 0.5f), new Vector3(chord, t, 2f * HalfZ), lower ? MatHullFloor : MatHullCeiling, localRot: spin);
                    for (float z = -HalfZ + 8f; z < HalfZ; z += 16f)
                        B("Fold Light Ring", FoldCentre + radial * (R - 0.03f) + Vector3.forward * z, new Vector3(chord, 0.06f, 0.35f),
                            lower ? MatFloorGlow : MatCeilingGlow, collide: false, localRot: spin);
                    for (float z = -HalfZ + 2f; z < HalfZ; z += 10f)
                        B("Fold Rib", FoldCentre + radial * (R + t + 0.45f) + Vector3.forward * z, new Vector3(chord, 0.9f, 0.7f),
                            MatSteel, collide: false, localRot: spin);
                }
                for (float z = -HalfZ + 20f; z < HalfZ; z += 40f)
                    Layout.Lights.Add(new PavilionLight { Position = new Vector3(HalfX + 10f, Gap * 0.5f, z), Range = 24f, Intensity = 1.5f, Color = new Color(0.75f, 0.85f, 1f) });
            }

            void Stars()
            {
                Frame("Space", Vector3.zero, Quaternion.identity);
                var random = new System.Random(99);
                var centre = new Vector3(0f, Gap * 0.5f, 0f);
                int made = 0;
                while (made < 320)
                {
                    var d = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
                    float m = d.magnitude;
                    if (m < 0.2f || m > 1f) continue;
                    float r = 170f + (float)random.NextDouble() * 60f, size = 0.3f + (float)random.NextDouble() * 0.7f;
                    B("Star", centre + d / m * r, Vector3.one * size, MatLight, collide: false);
                    made++;
                }
            }
        }
    }
}
