using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>Sizes for the turning cube, kept in one place.</summary>
    [System.Serializable]
    public sealed class CubeConfig
    {
        [Tooltip("Size of one piece (the cube is three pieces across), metres.")]
        public float piece = 14f;
        [Tooltip("Thickness of the cube's shell round the hollow middle, metres. The outer edges are rounded with this radius.")]
        public float shell = 3f;
        [Tooltip("Radius of the rounded inner edges and corners (walk round onto the next inner wall), metres.")]
        public float innerRadius = 3f;
        [Tooltip("Gap between neighbouring pieces, metres.")]
        public float seamGap = 0.02f;
        [Tooltip("Door width and height, metres.")]
        public float doorWidth = 2.8f, doorHeight = 4f;
        [Tooltip("Depth of the block each door stands in front of, metres (covers the portal's trigger behind it).")]
        public float boothDepth = 2.2f;
        [Tooltip("How far the inside door stands from the middle of its face (clear of the axle's gears), metres.")]
        public float innerDoorOffset = 5f;
        [Tooltip("Seed for the cover blocks on the pieces (fixed: the same cube every match).")]
        public int coverSeed = 7;

        public float Half => piece * 1.5f;
        public float Seam => piece * 0.5f;
        public float Inner => Half - shell;
        public float OuterRadius => shell;
    }

    /// <summary>A gear of the machinery (drawn and animated by the builder; no collider).</summary>
    public struct CubeGear
    {
        public string Name;
        public Vector3 Centre, Axis;
        public float Radius, Thickness;
        public int Teeth;
        /// <summary>Internal teeth (a ring gear) rather than external.</summary>
        public bool Ring;
        /// <summary>A bevel gear (teeth on a cone) at the core.</summary>
        public bool Bevel;
        /// <summary>The piece it is fixed to (moves with it), or -1 for the fixed machinery.</summary>
        public int Piece;
        /// <summary>The face whose turns drive it (0..5).</summary>
        public int Face;
        /// <summary>Degrees it spins round its own axis per degree its face turns (0 = doesn't spin
        /// on its own; a ring gear turns with its piece).</summary>
        public float Ratio;
    }

    /// <summary>A door of the cube: a portal on a centre piece (they only spin in place).</summary>
    public struct CubeDoor
    {
        public PavilionPortal Portal;
        public int Piece;
        public int Face;
        public bool Inside;
    }

    public struct CubeSpawn
    {
        public PavilionMarker Marker;
        public int Piece;
        public bool Inside;
    }

    /// <summary>
    /// The turning cube, as pure data (the builder turns it into GameObjects).
    ///
    /// A 3x3x3 cube of 14 m pieces (42 m across), hollow: the pieces form a 3 m shell round a 36 m
    /// room. Outer edges are rounded with the shell's thickness and inner edges with
    /// <see cref="CubeConfig.innerRadius"/>, so you can walk anywhere outside (gravity points into
    /// the cube) and anywhere inside (gravity follows the walls, like a Tesseract room). Only the
    /// six outer layers turn (see <see cref="CubeSchedule"/>).
    ///
    /// Every piece's geometry stays inside its own place in the grid (between the seam planes), so
    /// a turning layer never touches the rest. The pieces are given at rest; the builder puts each
    /// piece's geometry under its own object at the cube centre and turns that object.
    ///
    /// The doors (the only portals) stand on the six centre pieces, one outside and one inside per
    /// face: walk in upright on a face and you come out upright on the back of the same face.
    /// The centre pieces only spin, so a door stays on its face and both doors of a pair turn
    /// together. The machinery in the middle (core, axles, gearboxes) never moves; a face's gears
    /// spin while it turns.
    /// </summary>
    public sealed class CubeMap
    {
        public CubeConfig Config;
        /// <summary>Geometry of each piece at rest, by <see cref="CubeSchedule.Index(int,int,int)"/> (the core's is empty).</summary>
        public readonly PavilionLayout[] Pieces = new PavilionLayout[27];
        /// <summary>The machinery that never moves.</summary>
        public readonly PavilionLayout Fixed = new PavilionLayout();
        public readonly List<CubeGear> Gears = new List<CubeGear>();
        /// <summary>Doors; each one's Portal.Link indexes this list.</summary>
        public readonly List<CubeDoor> Doors = new List<CubeDoor>();
        public readonly List<CubeSpawn> Spawns = new List<CubeSpawn>();
        public readonly List<PavilionLight> Lights = new List<PavilionLight>();
    }

    public static class CubeLayout
    {
        // Material slots (cast to PavilionMat; the builder maps them to real materials).
        public const int MatBody = 0;          // the pieces' black plastic
        public const int MatSticker = 1;       // 1..6: sticker colour of face 0..5
        public const int MatInner = 7;         // inside walls
        public const int MatMachine = 8;       // dark steel: core, axles, housings, cover
        public const int MatBrass = 9;         // gears
        public const int MatGlow = 10;         // warning lights
        public const int MatCount = 11;

        public static readonly string[] FaceNames = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
        public static readonly Color[] StickerColors =
        {
            new Color(0.85f, 0.12f, 0.1f), new Color(1f, 0.45f, 0.05f), new Color(0.95f, 0.95f, 0.95f),
            new Color(1f, 0.85f, 0.1f), new Color(0.1f, 0.6f, 0.2f), new Color(0.1f, 0.3f, 0.85f),
        };

        const int ArcSteps = 12;

        static PavilionMat M(int slot) => (PavilionMat)slot;

        /// <summary>The two axes across face <paramref name="face"/>'s axis, in a fixed order.</summary>
        public static void Across(int face, out Vector3 u, out Vector3 v)
        {
            int a = face / 2;
            u = CubeSchedule.AxisVector((a + 1) % 3);
            v = CubeSchedule.AxisVector((a + 2) % 3);
        }

        static float Coord(Vector3Int v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;
        static Vector3 Ax(int a) => CubeSchedule.AxisVector(a);

        /// <summary>The span of a piece along one axis (grid index -1/0/1), seam gaps left out.</summary>
        public static Vector2 Range(CubeConfig c, int index)
        {
            float g = c.seamGap * 0.5f;
            if (index < 0) return new Vector2(-c.Half, -c.Seam - g);
            if (index > 0) return new Vector2(c.Seam + g, c.Half);
            return new Vector2(-c.Seam + g, c.Seam - g);
        }

        /// <summary><see cref="Range"/> clipped to +-<paramref name="limit"/>.</summary>
        static Vector2 Clip(Vector2 r, float limit) => new Vector2(Mathf.Max(r.x, -limit), Mathf.Min(r.y, limit));

        public static CubeMap Generate(CubeConfig c)
        {
            var map = new CubeMap { Config = c };
            for (int i = 0; i < 27; i++) map.Pieces[i] = new PavilionLayout();
            var rng = new Rng((uint)c.coverSeed);
            for (int i = 0; i < 27; i++)
            {
                if (i == CubeSchedule.Core) continue;
                Shell(map.Pieces[i], i, c);
                if (!CubeSchedule.IsCentre(i)) Cover(map.Pieces[i], i, c, rng);
            }
            for (int face = 0; face < 6; face++) Face(map, face, c);
            Machinery(map, c);
            return map;
        }

        // ------------------------------------------------------------------ shell

        static void Shell(PavilionLayout L, int piece, CubeConfig c)
        {
            Vector3Int h = CubeSchedule.Home(piece);
            float half = c.Half, inner = c.Inner, R = c.OuterRadius, r = c.innerRadius;
            float sOut = half - R, sIn = inner - r;
            string name = $"Piece {h.x},{h.y},{h.z}";

            // One slab per outward side: the flat part of the shell, from the inner wall to the outer face.
            for (int a = 0; a < 3; a++)
            {
                int ha = (int)Coord(h, a);
                if (ha == 0) continue;
                int b = (a + 1) % 3, d = (a + 2) % 3;
                Vector2 rb = Clip(Range(c, (int)Coord(h, b)), inner), rd = Clip(Range(c, (int)Coord(h, d)), inner);
                Vector3 n = Ax(a) * ha;
                AddBox(L, $"{name} shell {FaceNames[a * 2 + (ha > 0 ? 0 : 1)]}", n * ((inner + half) * 0.5f), a, b, d,
                    half - inner, rb, rd, MatBody, true, true);

                // The inside wall's colour (a thin panel just off the slab, stopping where the inner curves start).
                Vector2 lb = Clip(rb, sIn), ld = Clip(rd, sIn);
                if (lb.y > lb.x && ld.y > ld.x)
                    AddBox(L, $"{name} inner panel", n * (inner - 0.02f), a, b, d, 0.02f, lb, ld, MatInner, false, true);

                // Sticker on the outside, coloured by the face this piece started on.
                int face = a * 2 + (ha > 0 ? 0 : 1);
                Vector2 sb = Inset(Clip(rb, sOut), 0.6f), sd = Inset(Clip(rd, sOut), 0.6f);
                AddBox(L, $"{name} sticker", n * (half + 0.015f), a, b, d, 0.02f, sb, sd, MatSticker + face, false, true);

                // Warning lamps: a glowing frame round the sticker and round the inside panel, lit
                // (by CubeTurns) before and while this piece's layer turns.
                Frame(L, $"{name} lamp", n * (half + 0.025f), a, b, d, Inset(Clip(rb, sOut), 0.25f), Inset(Clip(rd, sOut), 0.25f));
                if (lb.y > lb.x + 1f && ld.y > ld.x + 1f)
                    Frame(L, $"{name} inner lamp", n * (inner - 0.04f), a, b, d, Inset(lb, 0.3f), Inset(ld, 0.3f));
            }

            // Rounded edges: outside (convex, a quarter of a cylinder) and inside (a concave quarter-pipe).
            var outer = new PavilionSurface { Name = $"{name} outer curves", Material = M(MatBody) };
            var innerCurves = new PavilionSurface { Name = $"{name} inner curves", Material = M(MatInner) };
            var ends = new PavilionSurface { Name = $"{name} curve ends", Material = M(MatBody) };
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                {
                    int ha = (int)Coord(h, a), hb = (int)Coord(h, b);
                    if (ha == 0 || hb == 0) continue;
                    int e = 3 - a - b;
                    int he = (int)Coord(h, e);
                    Vector3 A = Ax(a) * ha, B = Ax(b) * hb, along = Ax(e);
                    Vector2 spanOut = Clip(Range(c, he), sOut), spanIn = Clip(Range(c, he), sIn);
                    Cylinder(outer, A * sOut + B * sOut, A, B, along, spanOut.x, spanOut.y, R, true);
                    Cylinder(innerCurves, A * sIn + B * sIn, A, B, along, spanIn.x, spanIn.y, r, false);
                    // Close the ends that lie on a seam (they show while a layer turns).
                    // (The low end of a span faces down the axis, the high end up it.)
                    if (OnSeam(spanOut.x, c)) QuarterDisc(ends, A * sOut + B * sOut + along * spanOut.x, A, B, R, -along);
                    if (OnSeam(spanOut.y, c)) QuarterDisc(ends, A * sOut + B * sOut + along * spanOut.y, A, B, R, along);
                    if (OnSeam(spanIn.x, c)) Jamb(ends, A * sIn + B * sIn + along * spanIn.x, A, B, r, -along);
                    if (OnSeam(spanIn.y, c)) Jamb(ends, A * sIn + B * sIn + along * spanIn.y, A, B, r, along);
                }
            if (h.x != 0 && h.y != 0 && h.z != 0)
            {
                var sign = new Vector3(h.x, h.y, h.z);
                Cap(outer, sign * sOut, sign, R, true);
                Cap(innerCurves, sign * sIn, sign, r, false);
            }
            if (outer.Triangles.Count > 0) L.Surfaces.Add(outer);
            if (innerCurves.Triangles.Count > 0) L.Surfaces.Add(innerCurves);
            if (ends.Triangles.Count > 0) { ends.Collide = false; L.Surfaces.Add(ends); }
        }

        static bool OnSeam(float t, CubeConfig c) => Mathf.Abs(Mathf.Abs(t) - c.Seam) < c.seamGap;

        static Vector2 Inset(Vector2 r, float by) => new Vector2(r.x + by, r.y - by);

        /// <summary>Four thin glowing strips round the rectangle <paramref name="rb"/> x <paramref name="rd"/>.</summary>
        static void Frame(PavilionLayout L, string name, Vector3 centreAlong, int a, int b, int d, Vector2 rb, Vector2 rd)
        {
            const float w = 0.15f, t = 0.02f;
            AddBox(L, name, centreAlong, a, b, d, t, new Vector2(rb.x, rb.y), new Vector2(rd.x, rd.x + w), MatGlow, false, true);
            AddBox(L, name, centreAlong, a, b, d, t, new Vector2(rb.x, rb.y), new Vector2(rd.y - w, rd.y), MatGlow, false, true);
            AddBox(L, name, centreAlong, a, b, d, t, new Vector2(rb.x, rb.x + w), new Vector2(rd.x + w, rd.y - w), MatGlow, false, true);
            AddBox(L, name, centreAlong, a, b, d, t, new Vector2(rb.y - w, rb.y), new Vector2(rd.x + w, rd.y - w), MatGlow, false, true);
        }

        /// <summary>A box spanning <paramref name="ra"/>/<paramref name="rb"/> across two axes and
        /// <paramref name="thick"/> along the third, centred at <paramref name="centreAlong"/> on it.</summary>
        static void AddBox(PavilionLayout L, string name, Vector3 centreAlong, int a, int b, int d, float thick,
            Vector2 rb, Vector2 rd, int mat, bool collide, bool visible)
        {
            Vector3 centre = centreAlong + Ax(b) * ((rb.x + rb.y) * 0.5f) + Ax(d) * ((rd.x + rd.y) * 0.5f);
            Vector3 size = Ax(a) * thick + Ax(b) * (rb.y - rb.x) + Ax(d) * (rd.y - rd.x);
            L.Boxes.Add(new PavilionBox
            {
                Name = name, Center = centre, Size = size, Rotation = Quaternion.identity,
                Material = M(mat), Collide = collide, Visible = visible,
            });
        }

        // ------------------------------------------------------------------ cover

        /// <summary>Cover blocks bolted to a piece: as the layers turn they travel with it, so the
        /// cover is reshuffled with every turn. Kept 1.5 m inside the piece so they never reach a seam.</summary>
        static void Cover(PavilionLayout L, int piece, CubeConfig c, Rng rng)
        {
            Vector3Int h = CubeSchedule.Home(piece);
            float half = c.Half, inner = c.Inner;
            for (int a = 0; a < 3; a++)
            {
                int ha = (int)Coord(h, a);
                if (ha == 0) continue;
                int b = (a + 1) % 3, d = (a + 2) % 3;
                Vector3 n = Ax(a) * ha;

                // Outside: on the flat part of this piece's tile.
                Vector2 rb = Inset(Clip(Range(c, (int)Coord(h, b)), half - c.OuterRadius), 1.5f);
                Vector2 rd = Inset(Clip(Range(c, (int)Coord(h, d)), half - c.OuterRadius), 1.5f);
                if (rng.Next01() < 0.75f) Block(L, $"Cover {h.x},{h.y},{h.z} {a}", n * half, n, b, d, rb, rd, rng, MatMachine);

                // Inside: machinery housings on the inner wall.
                Vector2 ib = Inset(Clip(Range(c, (int)Coord(h, b)), inner - c.innerRadius), 1.5f);
                Vector2 id = Inset(Clip(Range(c, (int)Coord(h, d)), inner - c.innerRadius), 1.5f);
                if (ib.y > ib.x + 2f && id.y > id.x + 2f && rng.Next01() < 0.5f)
                    Block(L, $"Housing {h.x},{h.y},{h.z} {a}", n * inner, -n, b, d, ib, id, rng, MatMachine);
            }
        }

        /// <summary>One cover block standing on the surface through <paramref name="surface"/>, rising
        /// along <paramref name="up"/> (away from the surface into open space).</summary>
        static void Block(PavilionLayout L, string name, Vector3 surface, Vector3 up, int b, int d, Vector2 rb, Vector2 rd, Rng rng, int mat)
        {
            // Kinds: a low wall to crouch behind, a pillar, a wide block.
            int kind = (int)(rng.Next01() * 3f);
            Vector3 size;          // across b, across d, height
            if (kind == 0) size = new Vector3(4f, 0.8f, 1.2f);
            else if (kind == 1) size = new Vector3(1.4f, 1.4f, 2.8f);
            else size = new Vector3(2.6f, 2f, 1.8f);
            if (rng.Next01() < 0.5f) size = new Vector3(size.y, size.x, size.z);
            float fb = rb.y - rb.x - size.x, fd = rd.y - rd.x - size.y;
            if (fb < 0f || fd < 0f) return;
            float cb = rb.x + size.x * 0.5f + rng.Next01() * fb;
            float cd = rd.x + size.y * 0.5f + rng.Next01() * fd;
            Vector3 centre = surface + up * (size.z * 0.5f) + Ax(b) * cb + Ax(d) * cd;
            Vector3 extent = Abs(up) * size.z + Ax(b) * size.x + Ax(d) * size.y;
            L.Boxes.Add(new PavilionBox
            {
                Name = name, Center = centre, Size = extent, Rotation = Quaternion.identity,
                Material = M(mat), Collide = true, Visible = true,
            });
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // ------------------------------------------------------------------ faces: doors, spawns, gears

        static void Face(CubeMap map, int face, CubeConfig c)
        {
            Vector3 n = CubeSchedule.FaceNormal(face);
            Across(face, out Vector3 u, out Vector3 v);
            int a = face / 2, ha = face % 2 == 0 ? 1 : -1;
            int piece = CubeSchedule.Index(a == 0 ? ha : 0, a == 1 ? ha : 0, a == 2 ? ha : 0);
            var L = map.Pieces[piece];
            float half = c.Half, inner = c.Inner, w = c.doorWidth, dh = c.doorHeight, depth = c.boothDepth;
            string fn = FaceNames[face];

            // Outside door: on the face's middle, facing +v, its booth behind it.
            Vector3 outPos = n * (half + dh * 0.5f);
            Booth(L, $"Door booth {fn} outside", outPos, n, u, v, c);
            // Inside door: on the back of the same piece, off to one side of the axle.
            Vector3 inPos = n * (inner - dh * 0.5f) + u * c.innerDoorOffset;
            Booth(L, $"Door booth {fn} inside", inPos, -n, u, v, c);

            int iOut = map.Doors.Count, iIn = iOut + 1;
            Color col = StickerColors[face];
            map.Doors.Add(new CubeDoor
            {
                Piece = piece, Face = face, Inside = false,
                Portal = new PavilionPortal { Name = $"Door {fn} outside", Position = outPos, Normal = v, Up = n, Size = new Vector2(w, dh), Link = iIn, Color = col, Doorway = true },
            });
            map.Doors.Add(new CubeDoor
            {
                Piece = piece, Face = face, Inside = true,
                Portal = new PavilionPortal { Name = $"Door {fn} inside", Position = inPos, Normal = v, Up = -n, Size = new Vector2(w, dh), Link = iOut, Color = col, Doorway = true },
            });

            // Spawns: one outside, one inside, on the centre piece (it never leaves its face, and no
            // cover can ever land on it).
            float s = 4.5f;
            map.Spawns.Add(new CubeSpawn
            {
                Piece = piece, Inside = false,
                Marker = new PavilionMarker { Position = n * (half + 0.95f) - u * s - v * s, Rotation = PavilionLayout.Basis(v, n) },
            });
            map.Spawns.Add(new CubeSpawn
            {
                Piece = piece, Inside = true,
                Marker = new PavilionMarker { Position = n * (inner - 0.95f) - u * s + v * s, Rotation = PavilionLayout.Basis(-v, -n) },
            });

            // Gearbox at the face end of the axle: a ring gear on the piece, a sun gear on the axle and
            // three planets on a carrier fixed to the core arm (so the axle spins 2.5x the face).
            const float ringR = 2.5f, sunR = 1f, planetR = 0.75f, gearT = 0.7f;
            Vector3 gc = n * (inner - 0.02f - gearT * 0.5f);
            map.Gears.Add(new CubeGear { Name = $"Ring gear {fn}", Centre = gc, Axis = n, Radius = ringR, Thickness = gearT, Teeth = 40, Ring = true, Piece = piece, Face = face, Ratio = 0f });
            map.Gears.Add(new CubeGear { Name = $"Sun gear {fn}", Centre = gc, Axis = n, Radius = sunR, Thickness = gearT, Teeth = 16, Piece = -1, Face = face, Ratio = -ringR / sunR });
            for (int k = 0; k < 3; k++)
            {
                float th = k * Mathf.PI * 2f / 3f;
                Vector3 at = gc + (u * Mathf.Cos(th) + v * Mathf.Sin(th)) * (sunR + planetR);
                map.Gears.Add(new CubeGear { Name = $"Planet gear {fn} {k}", Centre = at, Axis = n, Radius = planetR, Thickness = gearT, Teeth = 12, Piece = -1, Face = face, Ratio = ringR / planetR });
            }
            // Bevel gear where the axle meets the core.
            map.Gears.Add(new CubeGear { Name = $"Bevel gear {fn}", Centre = n * 5.2f, Axis = n, Radius = 1.6f, Thickness = 0.6f, Teeth = 20, Bevel = true, Piece = -1, Face = face, Ratio = -ringR / sunR });

            // The gears have no colliders of their own: one invisible block stands in for the whole
            // gearbox, so you can't walk through it (it's round enough: the gears turn inside it). It
            // stops 15 cm short of the turning wall behind it.
            const float boxT = 0.55f;
            map.Fixed.Boxes.Add(new PavilionBox
            {
                Name = $"Gearbox {fn} collider", Center = n * (inner - 0.15f - boxT * 0.5f), Size = Abs(n) * boxT + Abs(u) * 4f + Abs(v) * 4f,
                Rotation = Quaternion.identity, Material = M(MatMachine), Collide = true, Visible = false,
            });

            // Carrier plate (fixed) and the arm from the core.
            Disc(map.Fixed, $"Carrier {fn}", gc - n * (gearT * 0.5f + 0.15f), n, u, v, 2.2f, 0.2f, MatMachine, false);
            float armEnd = inner - 0.95f; // meets the carrier
            Disc(map.Fixed, $"Arm {fn}", n * ((4.2f + armEnd) * 0.5f), n, u, v, 0.9f, armEnd - 4.2f, MatMachine, true);

            map.Lights.Add(new PavilionLight { Position = n * 11f, Range = 24f, Intensity = 1.6f, Color = Color.Lerp(col, Color.white, 0.6f) });
        }

        /// <summary>The block a door stands in front of (its portal's host), standing on the surface
        /// with up <paramref name="up"/>; the door faces +<paramref name="v"/>.</summary>
        static void Booth(PavilionLayout L, string name, Vector3 doorCentre, Vector3 up, Vector3 u, Vector3 v, CubeConfig c)
        {
            float lintel = 0.4f;
            Vector3 centre = doorCentre + up * (lintel * 0.5f) - v * (c.boothDepth * 0.5f);
            Vector3 size = Abs(u) * (c.doorWidth + 0.8f) + Abs(up) * (c.doorHeight + lintel) + Abs(v) * c.boothDepth;
            L.Boxes.Add(new PavilionBox { Name = name, Center = centre, Size = size, Rotation = Quaternion.identity, Material = M(MatMachine), Collide = true, Visible = true });
        }

        // ------------------------------------------------------------------ machinery

        static void Machinery(CubeMap map, CubeConfig c)
        {
            var core = new PavilionSurface { Name = "Core", Material = M(MatMachine) };
            Sphere(core, Vector3.zero, 4.5f);
            map.Fixed.Surfaces.Add(core);
            // Light in the hollow round the core (the corners), plus the face lights added per face.
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>A quarter of a cylinder round the line <paramref name="core"/> + t*along, from
        /// direction a round to b, t from t0 to t1. Convex (normals outward) or concave (inward).</summary>
        static void Cylinder(PavilionSurface m, Vector3 core, Vector3 a, Vector3 b, Vector3 along, float t0, float t1, float R, bool convex)
        {
            if (t1 <= t0) return;
            int start = m.Vertices.Count;
            for (int k = 0; k <= ArcSteps; k++)
            {
                float th = k * (Mathf.PI * 0.5f / ArcSteps);
                Vector3 radial = a * Mathf.Cos(th) + b * Mathf.Sin(th);
                foreach (float t in new[] { t0, t1 })
                {
                    m.Vertices.Add(core + radial * R + along * t);
                    m.Normals.Add(convex ? radial : -radial);
                    m.UVs.Add(new Vector2(t / 14f, th * R / 14f));
                }
            }
            for (int k = 0; k < ArcSteps; k++)
            {
                int i = start + 2 * k;
                Quad(m, i, i + 1, i + 3, i + 2);
            }
        }

        /// <summary>The cut end of a convex edge: a quarter disc facing <paramref name="facing"/>.</summary>
        static void QuarterDisc(PavilionSurface m, Vector3 centre, Vector3 a, Vector3 b, float R, Vector3 facing)
        {
            int k0 = m.Vertices.Count;
            m.Vertices.Add(centre); m.Normals.Add(facing); m.UVs.Add(Vector2.zero);
            for (int k = 0; k <= ArcSteps; k++)
            {
                float th = k * (Mathf.PI * 0.5f / ArcSteps);
                m.Vertices.Add(centre + (a * Mathf.Cos(th) + b * Mathf.Sin(th)) * R);
                m.Normals.Add(facing); m.UVs.Add(Vector2.zero);
            }
            for (int k = 0; k < ArcSteps; k++) Tri(m, k0, k0 + 1 + k, k0 + 2 + k);
        }

        /// <summary>The cut end of a concave edge: the solid corner between the curve and the two walls.</summary>
        static void Jamb(PavilionSurface m, Vector3 core, Vector3 a, Vector3 b, float R, Vector3 facing)
        {
            int k0 = m.Vertices.Count;
            m.Vertices.Add(core + (a + b) * R); m.Normals.Add(facing); m.UVs.Add(Vector2.zero);
            for (int k = 0; k <= ArcSteps; k++)
            {
                float th = k * (Mathf.PI * 0.5f / ArcSteps);
                m.Vertices.Add(core + (a * Mathf.Cos(th) + b * Mathf.Sin(th)) * R);
                m.Normals.Add(facing); m.UVs.Add(Vector2.zero);
            }
            for (int k = 0; k < ArcSteps; k++) Tri(m, k0, k0 + 1 + k, k0 + 2 + k);
        }

        /// <summary>One eighth of a sphere rounding a corner.</summary>
        static void Cap(PavilionSurface m, Vector3 core, Vector3 sign, float R, bool convex)
        {
            int start = m.Vertices.Count, n = ArcSteps;
            for (int p = 0; p <= n; p++)
                for (int q = 0; q <= n; q++)
                {
                    float phi = p * (Mathf.PI * 0.5f / n), th = q * (Mathf.PI * 0.5f / n);
                    var dir = new Vector3(Mathf.Cos(phi) * Mathf.Cos(th) * sign.x, Mathf.Cos(phi) * Mathf.Sin(th) * sign.y, Mathf.Sin(phi) * sign.z);
                    m.Vertices.Add(core + dir * R);
                    m.Normals.Add(convex ? dir : -dir);
                    m.UVs.Add(new Vector2(th, phi) * (R / 14f));
                }
            for (int p = 0; p < n; p++)
                for (int q = 0; q < n; q++)
                {
                    int i = start + p * (n + 1) + q;
                    Quad(m, i, i + 1, i + n + 2, i + n + 1);
                }
        }

        static void Sphere(PavilionSurface m, Vector3 centre, float R)
        {
            int start = m.Vertices.Count, rings = 16, segs = 32;
            for (int p = 0; p <= rings; p++)
                for (int q = 0; q <= segs; q++)
                {
                    float phi = Mathf.PI * p / rings - Mathf.PI * 0.5f, th = 2f * Mathf.PI * q / segs;
                    var dir = new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th));
                    m.Vertices.Add(centre + dir * R);
                    m.Normals.Add(dir);
                    m.UVs.Add(new Vector2((float)q / segs, (float)p / rings));
                }
            for (int p = 0; p < rings; p++)
                for (int q = 0; q < segs; q++)
                {
                    int i = start + p * (segs + 1) + q;
                    Quad(m, i, i + 1, i + segs + 2, i + segs + 1);
                }
        }

        /// <summary>A closed cylinder (disc or rod) along <paramref name="axis"/>, centred at
        /// <paramref name="centre"/>, as its own surface.</summary>
        static void Disc(PavilionLayout L, string name, Vector3 centre, Vector3 axis, Vector3 u, Vector3 v, float R, float length, int mat, bool collide)
        {
            var m = new PavilionSurface { Name = name, Material = M(mat), Collide = collide };
            int segs = 32;
            Vector3 e0 = centre - axis * (length * 0.5f), e1 = centre + axis * (length * 0.5f);
            int side = m.Vertices.Count;
            for (int q = 0; q <= segs; q++)
            {
                float th = 2f * Mathf.PI * q / segs;
                Vector3 radial = u * Mathf.Cos(th) + v * Mathf.Sin(th);
                m.Vertices.Add(e0 + radial * R); m.Normals.Add(radial); m.UVs.Add(new Vector2((float)q / segs, 0f));
                m.Vertices.Add(e1 + radial * R); m.Normals.Add(radial); m.UVs.Add(new Vector2((float)q / segs, length / 14f));
            }
            for (int q = 0; q < segs; q++)
            {
                int i = side + 2 * q;
                Quad(m, i, i + 1, i + 3, i + 2);
            }
            foreach (var (end, facing) in new[] { (e0, -axis), (e1, axis) })
            {
                int k0 = m.Vertices.Count;
                m.Vertices.Add(end); m.Normals.Add(facing); m.UVs.Add(Vector2.zero);
                for (int q = 0; q <= segs; q++)
                {
                    float th = 2f * Mathf.PI * q / segs;
                    m.Vertices.Add(end + (u * Mathf.Cos(th) + v * Mathf.Sin(th)) * R); m.Normals.Add(facing); m.UVs.Add(Vector2.zero);
                }
                for (int q = 0; q < segs; q++) Tri(m, k0, k0 + 1 + q, k0 + 2 + q);
            }
            L.Surfaces.Add(m);
        }

        static void Quad(PavilionSurface m, int a, int b, int c, int d)
        {
            Tri(m, a, b, c);
            Tri(m, a, c, d);
        }

        /// <summary>A triangle wound to face the way its vertex normals point; slivers skipped.</summary>
        static void Tri(PavilionSurface m, int a, int b, int c)
        {
            Vector3 cross = Vector3.Cross(m.Vertices[b] - m.Vertices[a], m.Vertices[c] - m.Vertices[a]);
            if (cross.sqrMagnitude < 1e-10f) return;
            Vector3 n = m.Normals[a] + m.Normals[b] + m.Normals[c];
            m.Triangles.Add(a);
            if (Vector3.Dot(cross, n) >= 0f) { m.Triangles.Add(b); m.Triangles.Add(c); }
            else { m.Triangles.Add(c); m.Triangles.Add(b); }
        }

        // ------------------------------------------------------------------ field

        public static CubeField Field(CubeConfig c) => new CubeField
        {
            outerHalf = c.Half,
            outerRadius = c.OuterRadius,
            inside = new RoomField
            {
                centres = new[] { Vector3.zero },
                half = c.Inner,
                edgeRadius = c.innerRadius,
                captureAngle = 30f,
            },
        };

        public static CubeSchedule Schedule(CubeConfig c, int seed) => new CubeSchedule { seed = seed, seam = c.Seam };

        // ------------------------------------------------------------------ portal maths (pure, as Portal does it)

        /// <summary>A direction carried through a door pair: Portal.TransferRotation (exit * half
        /// turn round up * entrance^-1), written out for checks outside the engine.</summary>
        public static Vector3 TransferDir(Vector3 fromNormal, Vector3 fromUp, Vector3 toNormal, Vector3 toUp, Vector3 dir)
        {
            Vector3 fr = Vector3.Cross(fromUp, fromNormal), tr = Vector3.Cross(toUp, toNormal);
            float x = Vector3.Dot(dir, fr), y = Vector3.Dot(dir, fromUp), z = Vector3.Dot(dir, fromNormal);
            return tr * -x + toUp * y + toNormal * -z;
        }

        /// <summary>Portal.GravityAfterPass, written out: carried through, snapped onto an axis
        /// within 30 degrees.</summary>
        public static Vector3 GravityAfterPass(Vector3 fromNormal, Vector3 fromUp, Vector3 toNormal, Vector3 toUp, Vector3 g)
        {
            Vector3 carried = TransferDir(fromNormal, fromUp, toNormal, toUp, g.normalized);
            Vector3 best = carried.normalized;
            float bestAngle = 30f;
            for (int a = 0; a < 3; a++)
                foreach (float s in new[] { 1f, -1f })
                {
                    float ang = Vector3.Angle(carried, Ax(a) * s);
                    if (ang <= bestAngle) { bestAngle = ang; best = Ax(a) * s; }
                }
            return best;
        }

        // ------------------------------------------------------------------ report

        public static string Report(CubeMap map)
        {
            var sb = new StringBuilder();
            var c = map.Config;
            sb.AppendLine($"Turning cube: {c.Half * 2f} m across, pieces {c.piece} m, shell {c.shell} m, hollow {c.Inner * 2f} m.");
            int boxes = 0, surfaces = 0;
            foreach (var p in map.Pieces) { boxes += p.Boxes.Count; surfaces += p.Surfaces.Count; }
            sb.AppendLine($"{boxes} boxes and {surfaces} curved surfaces on 26 pieces; {map.Fixed.Surfaces.Count} fixed machinery parts, {map.Gears.Count} gears.");
            sb.AppendLine($"{map.Doors.Count / 2} door pairs, {map.Spawns.Count} spawns.");
            return sb.ToString();
        }

        /// <summary>A tiny deterministic random generator (the same on every runtime).</summary>
        public sealed class Rng
        {
            uint state;
            public Rng(uint seed) { state = seed * 2654435761u + 1u; }
            public float Next01()
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                return (state & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
