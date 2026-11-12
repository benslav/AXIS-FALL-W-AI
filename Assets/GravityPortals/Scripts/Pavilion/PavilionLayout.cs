using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>Material slots for pavilion parts (the builder maps them to real materials).</summary>
    public enum PavilionMat { Tatami, Wood, Lacquer, Paper, Lantern, Stone }

    /// <summary>One box of level geometry (Unity cube scaled to Size, rotated by Rotation).</summary>
    public struct PavilionBox
    {
        public string Name;
        public Vector3 Center;
        public Vector3 Size;
        public Quaternion Rotation;
        public PavilionMat Material;
        public bool Collide;
        public bool Visible;
        /// <summary>Hidden walking ramp under a staircase (gets a StairSteps marker).</summary>
        public bool StairRamp;
        /// <summary>A breakable shoji screen: its own object (not merged), id = BreakId.</summary>
        public bool Breakable;
        public int BreakId;
        public Vector3 StairOrigin;
    }

    public struct PavilionPortal
    {
        public string Name;
        public Vector3 Position, Normal, Up;
        public Vector2 Size;
        public int Link;          // index of the linked portal in Portals
        public Color Color;
        public bool Doorway, WorldWrap;
    }

    public struct PavilionLight
    {
        public Vector3 Position;
        public float Range, Intensity;
        public Color Color;
    }

    public struct PavilionMarker
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>Per-room summary, for checks and debugging.</summary>
    public struct PavilionRoomInfo
    {
        public string Name;
        public Bounds Bounds;
        public Vector3 Up;
        public int Doors;
    }

    /// <summary>
    /// A randomly generated Endless Pavilion: rooms of several kinds (halls, tea rooms, long
    /// galleries, tall halls with a raised floor, open terraces), each facing a random
    /// direction inside one 64 m cell of an endless lattice (every face of the cell is a portal
    /// to the opposite face). Open sides may lead to stairs or a bridge out to a small platform.
    /// Sliding doors are linked in random pairs, always forming one connected map.
    ///
    /// The same seed always gives the same layout on every machine, so the host only has to
    /// share the seed. Pure data: <see cref="PavilionBuilder"/> turns it into GameObjects.
    /// Every door is upright in its room, so walking through one brings you out upright in
    /// the room on the other side, whatever its orientation.
    /// </summary>
    public sealed class PavilionLayout
    {
        public const float CellSize = 64f;

        public int Seed;
        public readonly List<PavilionBox> Boxes = new List<PavilionBox>();
        public readonly List<PavilionPortal> Portals = new List<PavilionPortal>();
        public readonly List<PavilionLight> Lights = new List<PavilionLight>();
        public readonly List<PavilionMarker> Spawns = new List<PavilionMarker>();
        public readonly List<PavilionMarker> Props = new List<PavilionMarker>();
        public readonly List<PavilionRoomInfo> Rooms = new List<PavilionRoomInfo>();
        /// <summary>Bounds of the solid buildings filling the space round the rooms.</summary>
        public readonly List<Bounds> Buildings = new List<Bounds>();
        /// <summary>Bounds of the bridges and beams linking rooms and buildings.</summary>
        public readonly List<Bounds> Connectors = new List<Bounds>();

        public static PavilionLayout Generate(int seed) => new PavilionGenerator(seed).Run();

        /// <summary>Rotation with the given forward and up (both axis-aligned and perpendicular).
        /// Done by hand so the layout can be generated and checked without the engine.</summary>
        public static Quaternion Basis(Vector3 forward, Vector3 up)
        {
            Vector3 f = forward.normalized, u = up.normalized;
            Vector3 r = Vector3.Cross(u, f);
            float m00 = r.x, m01 = u.x, m02 = f.x;
            float m10 = r.y, m11 = u.y, m12 = f.y;
            float m20 = r.z, m21 = u.z, m22 = f.z;
            float trace = m00 + m11 + m22;
            float qw, qx, qy, qz;
            if (trace > 0f)
            {
                float s = Mathf.Sqrt(trace + 1f) * 2f;
                qw = 0.25f * s; qx = (m21 - m12) / s; qy = (m02 - m20) / s; qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = Mathf.Sqrt(1f + m00 - m11 - m22) * 2f;
                qw = (m21 - m12) / s; qx = 0.25f * s; qy = (m01 + m10) / s; qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = Mathf.Sqrt(1f + m11 - m00 - m22) * 2f;
                qw = (m02 - m20) / s; qx = (m01 + m10) / s; qy = 0.25f * s; qz = (m12 + m21) / s;
            }
            else
            {
                float s = Mathf.Sqrt(1f + m22 - m00 - m11) * 2f;
                qw = (m10 - m01) / s; qx = (m02 + m20) / s; qy = (m12 + m21) / s; qz = 0.25f * s;
            }
            return new Quaternion(qx, qy, qz, qw);
        }
    }

    /// <summary>Does the actual generating for <see cref="PavilionLayout.Generate"/>.</summary>
    sealed class PavilionGenerator
    {
        enum Kind { Hall, TeaRoom, Gallery, TallHall, Terrace, Building }
        enum Side { Door, Shoji, TallShoji, Rail, None }
        enum Annex { None, StairDown, StairUp, Bridge }

        // Side order: 0 = west (-x), 1 = east (+x), 2 = north (+z), 3 = south (-z), in the room's frame.
        static readonly Vector3[] Outward = { Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        static readonly Vector3[] Along = { Vector3.forward, Vector3.forward, Vector3.right, Vector3.right };
        static readonly Vector3[] Axes = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        static readonly Color[] PairColors =
        {
            new Color(1f, 0.55f, 0.2f), new Color(0.95f, 0.25f, 0.3f), new Color(1f, 0.8f, 0.35f), new Color(0.7f, 0.35f, 1f),
            new Color(0.3f, 0.8f, 1f), new Color(0.4f, 1f, 0.6f), new Color(1f, 0.45f, 0.75f), new Color(0.95f, 0.95f, 0.6f),
        };

        const float WallHeight = 4.2f, Opening = 1.3f, Step = 0.25f;
        const float Margin = 3f;       // gap kept between rooms
        const float FaceMargin = 1.5f; // gap kept from the cell's wrap-around faces

        sealed class Part
        {
            public string Name;
            public Vector3 Center, Size;    // room frame
            public Quaternion Rotation = Quaternion.identity; // room frame
            public PavilionMat Mat;
            public bool Collide = true, Visible = true, Ramp;
            public Vector3 RampOrigin;
        }

        sealed class Room
        {
            public string Name;
            public Kind Kind;
            public float W, D, Height = 4.5f;
            public Quaternion Rotation;
            public Vector3 Up;
            public Vector3 Position;
            public readonly Side[] Sides = new Side[4];
            public readonly int[] DoorLink = { -1, -1, -1, -1 }; // edge id per door side
            public readonly Annex[] Annexes = new Annex[4];
            public readonly float[] AnnexSize = new float[4];
            public int MaxDoors, DoorCount, MezzSide = -1, StairSide = -1;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<(Vector3 pos, Vector3 normal, int side)> DoorPortals = new List<(Vector3, Vector3, int)>();
            public readonly List<(Vector3 pos, float range, float intensity)> LightSpots = new List<(Vector3, float, float)>();
            public readonly List<(Vector3 pos, float radius)> Reserved = new List<(Vector3, float)>(); // kept clear of furniture
            public readonly List<Vector3> SpawnSpots = new List<Vector3>();
            public readonly List<Vector3> PropSpots = new List<Vector3>();
            public readonly List<float> WalkLevels = new List<float>(); // heights you can stand at (room frame), for bridges
            public readonly List<Bounds> Headroom = new List<Bounds>(); // space people stand in (room frame), kept clear
            public Bounds LocalBounds;

            public float Offset(int s) => s < 2 ? W * 0.5f : D * 0.5f;
            public float HalfLength(int s) => s < 2 ? D * 0.5f : W * 0.5f;
            public Vector3 World(Vector3 local) => Position + Rotation * local;
        }

        readonly System.Random rng;
        readonly PavilionLayout layout;
        readonly List<Room> rooms = new List<Room>();

        public PavilionGenerator(int seed)
        {
            rng = new System.Random(seed);
            layout = new PavilionLayout { Seed = seed };
        }

        int Range(int min, int maxExclusive) => rng.Next(min, maxExclusive);
        float Value() => (float)rng.NextDouble();
        bool Chance(float p) => rng.NextDouble() < p;
        T Pick<T>(T[] options) => options[rng.Next(options.Length)];

        public PavilionLayout Run()
        {
            int count = Range(9, 13);
            for (int i = 0; i < count; i++) rooms.Add(DesignRoom(i));
            ConnectDoors();
            foreach (var r in rooms)
            {
                ChooseOpenSides(r);
                BuildRoom(r);
            }
            Place();
            PlaceBuildings();
            Connect();
            Emit();
            WrapWalls();
            return layout;
        }

        // ------------------------------------------------------------------ design

        Room DesignRoom(int index)
        {
            var kind = Pick(new[] { Kind.Hall, Kind.Hall, Kind.Hall, Kind.TeaRoom, Kind.TeaRoom, Kind.Gallery, Kind.Gallery, Kind.TallHall, Kind.Terrace, Kind.Terrace });
            var r = new Room { Kind = kind };
            switch (kind)
            {
                case Kind.Hall: r.W = Pick(new[] { 12f, 14f, 16f, 18f }); r.D = Pick(new[] { 12f, 14f, 16f, 18f }); r.MaxDoors = 3; break;
                case Kind.TeaRoom: r.W = Pick(new[] { 8f, 10f }); r.D = Pick(new[] { 8f, 10f }); r.MaxDoors = 2; break;
                case Kind.Gallery: r.W = Pick(new[] { 6f, 8f }); r.D = Pick(new[] { 18f, 22f, 26f }); r.MaxDoors = 3; break;
                case Kind.TallHall: r.W = Pick(new[] { 14f, 16f }); r.D = Pick(new[] { 14f, 16f }); r.MaxDoors = 2; r.Height = 8.5f; break;
                default: r.W = Pick(new[] { 10f, 12f, 14f }); r.D = Pick(new[] { 10f, 12f, 14f }); r.MaxDoors = 2; break;
            }
            string[] names = { "Hall", "Tea Room", "Gallery", "Tall Hall", "Terrace" };
            r.Name = $"{names[(int)kind]} {index + 1}";

            // Any of the 24 axis-aligned orientations: the floor can face any way.
            Vector3 up = Pick(Axes);
            Vector3 forward;
            do forward = Pick(Axes); while (Mathf.Abs(Vector3.Dot(forward, up)) > 0.5f);
            r.Up = up;
            r.Rotation = PavilionLayout.Basis(forward, up);

            if (kind == Kind.TallHall)
            {
                // Raised floor along one side, reached by a staircase along a neighbouring side.
                r.MezzSide = Range(0, 4);
                r.StairSide = r.MezzSide < 2 ? Range(2, 4) : Range(0, 2);
                r.Sides[r.MezzSide] = Side.TallShoji;
                r.Sides[r.StairSide] = Side.TallShoji;
            }
            for (int s = 0; s < 4; s++) if (s != r.MezzSide && s != r.StairSide) r.Sides[s] = Side.None; // decided later
            return r;
        }

        bool CanTakeDoor(Room r) => r.DoorCount < r.MaxDoors && FreeDoorSides(r).Count > 0;

        List<int> FreeDoorSides(Room r)
        {
            var list = new List<int>();
            for (int s = 0; s < 4; s++)
            {
                if (r.DoorLink[s] >= 0 || s == r.MezzSide || s == r.StairSide) continue;
                list.Add(s);
            }
            // Galleries prefer doors at their ends.
            if (r.Kind == Kind.Gallery && (list.Contains(2) || list.Contains(3))) list.RemoveAll(s => s < 2);
            return list;
        }

        readonly List<(int a, int sa, int b, int sb)> edges = new List<(int, int, int, int)>();

        void ConnectDoors()
        {
            // A random spanning tree (so every room is reachable), then a few extra links for loops.
            var order = new List<int>();
            for (int i = 0; i < rooms.Count; i++) order.Insert(Range(0, order.Count + 1), i);
            for (int i = 1; i < order.Count; i++)
            {
                var candidates = new List<int>();
                for (int j = 0; j < i; j++) if (CanTakeDoor(rooms[order[j]])) candidates.Add(order[j]);
                if (candidates.Count == 0) continue; // can't happen (each room adds a free slot), but stay safe
                Link(order[i], candidates[Range(0, candidates.Count)]);
            }
            int extra = Range(rooms.Count / 4, rooms.Count / 3 + 1);
            for (int attempt = 0; attempt < 40 && extra > 0; attempt++)
            {
                int a = Range(0, rooms.Count), b = Range(0, rooms.Count);
                if (a == b || !CanTakeDoor(rooms[a]) || !CanTakeDoor(rooms[b]) || Linked(a, b)) continue;
                Link(a, b);
                extra--;
            }
        }

        bool Linked(int a, int b)
        {
            foreach (var e in edges) if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) return true;
            return false;
        }

        void Link(int a, int b)
        {
            var ra = rooms[a];
            var rb = rooms[b];
            var fa = FreeDoorSides(ra);
            var fb = FreeDoorSides(rb);
            int sa = fa[Range(0, fa.Count)], sb = fb[Range(0, fb.Count)];
            int id = edges.Count;
            edges.Add((a, sa, b, sb));
            ra.Sides[sa] = Side.Door; ra.DoorLink[sa] = id; ra.DoorCount++;
            rb.Sides[sb] = Side.Door; rb.DoorLink[sb] = id; rb.DoorCount++;
        }

        void ChooseOpenSides(Room r)
        {
            int annexes = 0;
            for (int s = 0; s < 4; s++)
            {
                if (r.Sides[s] == Side.Door || s == r.MezzSide || s == r.StairSide) continue;
                float roll = Value();
                Side side;
                switch (r.Kind)
                {
                    case Kind.Hall: side = roll < 0.45f ? Side.Shoji : roll < 0.7f ? Side.Rail : Side.None; break;
                    case Kind.TeaRoom: side = roll < 0.7f ? Side.Shoji : Side.None; break;
                    case Kind.Gallery: side = s < 2 ? (roll < 0.55f ? Side.Shoji : Side.Rail) : (roll < 0.5f ? Side.Shoji : Side.None); break;
                    case Kind.TallHall: side = roll < 0.6f ? Side.Shoji : Side.Rail; break;
                    default: side = roll < 0.5f ? Side.Rail : Side.None; break;
                }
                r.Sides[s] = side;
                if (side == Side.None && annexes < 2 && Chance(0.7f))
                {
                    r.Annexes[s] = Pick(new[] { Annex.StairDown, Annex.StairUp, Annex.Bridge });
                    r.AnnexSize[s] = r.Annexes[s] == Annex.Bridge ? Pick(new[] { 5f, 7f, 9f }) : Pick(new[] { 3f, 4f, 5f });
                    annexes++;
                }
            }
        }

        // ------------------------------------------------------------------ room geometry (room frame)

        void P(Room r, string name, Vector3 center, Vector3 size, PavilionMat mat, bool collide = true)
        {
            r.Parts.Add(new Part { Name = name, Center = center, Size = size, Mat = mat, Collide = collide });
        }

        static Vector3 SizeAlong(Vector3 along, float length, float height, float thickness) =>
            along.x != 0f ? new Vector3(length, height, thickness) : new Vector3(thickness, height, length);

        void BuildRoom(Room r)
        {
            float hw = r.W * 0.5f, hd = r.D * 0.5f, h = r.Height;
            bool terrace = r.Kind == Kind.Terrace;
            P(r, "Floor", new Vector3(0, -0.25f, 0), new Vector3(r.W, 0.5f, r.D), terrace ? PavilionMat.Stone : PavilionMat.Tatami);
            P(r, "Floor Edge", new Vector3(-hw + 0.15f, 0.03f, 0), new Vector3(0.3f, 0.06f, r.D), PavilionMat.Wood, false);
            P(r, "Floor Edge", new Vector3(hw - 0.15f, 0.03f, 0), new Vector3(0.3f, 0.06f, r.D), PavilionMat.Wood, false);
            P(r, "Floor Edge", new Vector3(0, 0.03f, hd - 0.15f), new Vector3(r.W, 0.06f, 0.3f), PavilionMat.Wood, false);
            P(r, "Floor Edge", new Vector3(0, 0.03f, -hd + 0.15f), new Vector3(r.W, 0.06f, 0.3f), PavilionMat.Wood, false);
            if (!terrace)
            {
                P(r, "Beam", new Vector3(-hw, h, 0), new Vector3(0.35f, 0.35f, r.D), PavilionMat.Wood);
                P(r, "Beam", new Vector3(hw, h, 0), new Vector3(0.35f, 0.35f, r.D), PavilionMat.Wood);
                P(r, "Beam", new Vector3(0, h, hd), new Vector3(r.W, 0.35f, 0.35f), PavilionMat.Wood);
                P(r, "Beam", new Vector3(0, h, -hd), new Vector3(r.W, 0.35f, 0.35f), PavilionMat.Wood);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        P(r, "Pillar", new Vector3(sx * hw, h * 0.5f, sz * hd), new Vector3(0.45f, h + 0.2f, 0.45f), PavilionMat.Lacquer);
            }

            for (int s = 0; s < 4; s++) BuildSide(r, s);
            r.WalkLevels.Add(0f); // the floor
            if (r.Kind == Kind.TallHall) r.WalkLevels.Add(4f); // the raised floor

            switch (r.Kind)
            {
                case Kind.Hall: HallFeatures(r); break;
                case Kind.TeaRoom: TeaRoomFeatures(r); break;
                case Kind.Gallery: GalleryFeatures(r); break;
                case Kind.TallHall: TallHallFeatures(r); break;
                case Kind.Terrace: TerraceFeatures(r); break;
            }

            if (!terrace)
            {
                float y = r.Kind == Kind.TallHall ? 3f : 3.5f; // under the raised floor in a tall hall
                P(r, "Lantern", new Vector3(-hw + 1.2f, y, -hd + 1.2f), new Vector3(0.45f, 0.6f, 0.45f), PavilionMat.Lantern, false);
                P(r, "Lantern", new Vector3(hw - 1.2f, y, hd - 1.2f), new Vector3(0.45f, 0.6f, 0.45f), PavilionMat.Lantern, false);
            }
            if (r.Kind != Kind.Gallery)
                r.LightSpots.Add((new Vector3(0, r.Kind == Kind.TallHall ? 6.5f : 3.4f, 0), Mathf.Max(r.W, r.D) + 8f, 2.6f));

            // The building around the room: a roof with eaves (or a tower on top), and closed,
            // lit storeys underneath, so it stands as a whole building rather than a floor in the air.
            bool tower = (r.Kind == Kind.Hall || r.Kind == Kind.TeaRoom) && Chance(0.35f);
            if (!terrace)
            {
                Vector3 roofBase = new Vector3(0, h + 0.175f, 0);
                Roof(r, roofBase, r.W, r.D, tier: !tower);
                r.WalkLevels.Add(roofBase.y + 0.4f); // the roof
                if (tower) Block(r, roofBase + Vector3.up * 0.4f, Mathf.Round(r.W * 0.5f), Mathf.Round(r.D * 0.5f), Range(1, 3), roof: true);
            }
            int below = terrace ? Range(1, 4) : Range(1, 3);
            Block(r, new Vector3(0, -0.5f - below * Storey, 0), r.W - 0.8f, r.D - 0.8f, below, roof: false);
            P(r, "Foundation", new Vector3(0, -0.5f - below * Storey - 0.15f, 0), new Vector3(r.W - 0.3f, 0.3f, r.D - 0.3f), PavilionMat.Lacquer);

            // Bounds of everything visible, plus the space people stand in (open terraces and the
            // platforms out on stairs and bridges), so nothing gets built through it.
            r.LocalBounds = PartBounds(r);
            if (terrace) r.Headroom.Add(new Bounds(new Vector3(0, 2f, 0), new Vector3(r.W, 4f, r.D)));
            foreach (var space in r.Headroom) r.LocalBounds.Encapsulate(space);
        }

        static Bounds PartBounds(Room r)
        {
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var p in r.Parts)
            {
                if (!p.Visible) continue;
                var pb = new Bounds(p.Center, p.Size);
                if (first) { b = pb; first = false; } else b.Encapsulate(pb);
            }
            return b;
        }

        // ------------------------------------------------------------------ building exteriors (room frame)

        const float Storey = 3.6f;

        /// <summary>A solid block of <paramref name="storeys"/> storeys standing on <paramref name="bottom"/>,
        /// lit windows on every face, corner posts, and optionally a roof.</summary>
        void Block(Room r, Vector3 bottom, float w, float d, int storeys, bool roof)
        {
            float height = storeys * Storey;
            P(r, "Building", bottom + Vector3.up * (height * 0.5f), new Vector3(w, height, d), PavilionMat.Wood);
            for (int s = 0; s < 4; s++)
            {
                float off = s < 2 ? w * 0.5f : d * 0.5f, length = s < 2 ? d : w;
                Windows(r, bottom + Outward[s] * off, Along[s], Outward[s], length - 0.7f, storeys);
            }
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    P(r, "Corner Post", bottom + new Vector3(sx * w * 0.5f, height * 0.5f, sz * d * 0.5f), new Vector3(0.32f, height, 0.32f), PavilionMat.Lacquer, false);
            if (roof) Roof(r, bottom + Vector3.up * height, w, d, tier: w >= 6f && d >= 6f);
        }

        /// <summary>Rows of glowing paper windows across one face, a row per storey, with a
        /// band between storeys.</summary>
        void Windows(Room r, Vector3 faceBottom, Vector3 along, Vector3 outward, float length, int storeys)
        {
            if (length < 1.4f) return;
            int count = Mathf.Max(1, Mathf.FloorToInt((length + 0.6f) / 2.2f));
            float spacing = length / count;
            for (int s = 0; s < storeys; s++)
            {
                float y0 = s * Storey;
                for (int i = 0; i < count; i++)
                {
                    float a = -length * 0.5f + spacing * (i + 0.5f);
                    P(r, "Window", faceBottom + along * a + Vector3.up * (y0 + 1.9f) + outward * 0.04f,
                        SizeAlong(along, Mathf.Min(1.1f, spacing - 0.4f), 1.6f, 0.08f), PavilionMat.Paper, false);
                }
                if (s > 0)
                    P(r, "Storey Band", faceBottom + Vector3.up * y0 + outward * 0.05f, SizeAlong(along, length + 0.6f, 0.18f, 0.1f), PavilionMat.Lacquer, false);
            }
        }

        /// <summary>A roof slab with overhanging eaves, a lacquered edge, lanterns hanging at the
        /// corners, and optionally a smaller second tier on top.</summary>
        void Roof(Room r, Vector3 top, float w, float d, bool tier)
        {
            const float eave = 0.7f;
            P(r, "Roof", top + Vector3.up * 0.2f, new Vector3(w + eave * 2f, 0.4f, d + eave * 2f), PavilionMat.Wood);
            for (int s = 0; s < 4; s++)
            {
                float off = (s < 2 ? w : d) * 0.5f + eave, length = (s < 2 ? d : w) + eave * 2f;
                P(r, "Roof Edge", top + Outward[s] * off + Vector3.up * 0.2f, SizeAlong(Along[s], length + 0.12f, 0.5f, 0.12f), PavilionMat.Lacquer, false);
            }
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    P(r, "Eave Lantern", top + new Vector3(sx * (w * 0.5f + eave - 0.25f), -0.35f, sz * (d * 0.5f + eave - 0.25f)),
                        new Vector3(0.3f, 0.42f, 0.3f), PavilionMat.Lantern, false);
            if (tier)
            {
                float tw = Mathf.Round(w * 0.55f), td = Mathf.Round(d * 0.55f);
                P(r, "Roof Tier", top + Vector3.up * 0.75f, new Vector3(tw, 0.7f, td), PavilionMat.Wood);
                P(r, "Roof Tier Cap", top + Vector3.up * 1.25f, new Vector3(tw + 1f, 0.3f, td + 1f), PavilionMat.Wood);
                P(r, "Roof Lantern", top + Vector3.up * 1.6f, new Vector3(0.35f, 0.4f, 0.35f), PavilionMat.Lantern, false);
            }
        }

        /// <summary>A solid building with no way in: city around the rooms. You can land on it,
        /// grapple to it and walk its balconies.</summary>
        Room DesignBuilding(int index)
        {
            var r = new Room { Kind = Kind.Building, Name = $"Building {index + 1}" };
            r.W = Pick(new[] { 6f, 8f, 10f, 12f, 14f });
            r.D = Pick(new[] { 6f, 8f, 10f, 12f, 14f });
            // Mostly share a room's "down", so many neighbours can be joined by walkable bridges.
            Vector3 up = Chance(0.7f) && rooms.Count > 0 ? rooms[Range(0, rooms.Count)].Up : Pick(Axes), forward;
            do forward = Pick(Axes); while (Mathf.Abs(Vector3.Dot(forward, up)) > 0.5f);
            r.Up = up;
            r.Rotation = PavilionLayout.Basis(forward, up);

            int storeys = Range(2, 6);
            bool tower = Chance(0.3f) && r.W >= 8f && r.D >= 8f;
            Block(r, Vector3.zero, r.W, r.D, storeys, roof: !tower);
            float top = storeys * Storey;
            r.WalkLevels.Add(top + 0.4f); // the roof
            if (tower)
            {
                Roof(r, Vector3.up * top, r.W, r.D, tier: false);
                Block(r, Vector3.up * (top + 0.4f), Mathf.Round(r.W * 0.5f), Mathf.Round(r.D * 0.5f), Range(1, 3), roof: true);
            }
            if (Chance(0.45f))
            {
                // A balcony walk round one storey, with a rail.
                float y = Range(1, storeys) * Storey;
                r.WalkLevels.Add(y);
                P(r, "Balcony", new Vector3(0, y - 0.125f, 0), new Vector3(r.W + 2.4f, 0.25f, r.D + 2.4f), PavilionMat.Wood);
                for (int s = 0; s < 4; s++)
                {
                    float off = (s < 2 ? r.W : r.D) * 0.5f + 1.2f, length = (s < 2 ? r.D : r.W) + 2.4f;
                    Rail(r, Outward[s] * off + Vector3.up * y, length, Along[s]);
                }
            }
            r.LocalBounds = PartBounds(r);
            return r;
        }

        readonly List<Room> buildings = new List<Room>();

        /// <summary>Fills the space round the rooms with solid buildings, wherever they fit.</summary>
        void PlaceBuildings()
        {
            var taken = new List<Bounds>();
            foreach (var r in rooms)
                taken.Add(new Bounds(r.Position + r.Rotation * r.LocalBounds.center, Abs(r.Rotation * r.LocalBounds.size) + Vector3.one * (BuildingRoomGap * 2f)));
            int wanted = Range(22, 34);
            for (int attempt = 0; attempt < wanted * 5 && buildings.Count < wanted; attempt++)
            {
                var b = DesignBuilding(buildings.Count);
                Vector3 c = b.Rotation * b.LocalBounds.center;
                Vector3 e = Abs(b.Rotation * b.LocalBounds.extents);
                var min = Vector3.one * FaceMargin + e - c;
                var max = Vector3.one * (PavilionLayout.CellSize - FaceMargin) - e - c;
                if (min.x > max.x || min.y > max.y || min.z > max.z) continue;
                for (int t = 0; t < 30; t++)
                {
                    var pos = new Vector3(
                        Mathf.Round(Mathf.Lerp(min.x, max.x, Value()) * 2f) * 0.5f,
                        Mathf.Round(Mathf.Lerp(min.y, max.y, Value()) * 2f) * 0.5f,
                        Mathf.Round(Mathf.Lerp(min.z, max.z, Value()) * 2f) * 0.5f);
                    var bounds = new Bounds(pos + c, e * 2f);
                    var padded = new Bounds(bounds.center, bounds.size + Vector3.one * (BuildingGap * 2f));
                    bool clash = false;
                    foreach (var other in taken) if (other.Intersects(padded)) { clash = true; break; }
                    if (clash) continue;
                    b.Position = pos;
                    buildings.Add(b);
                    // Rooms' bounds already carry their gap; buildings carry half of theirs each side.
                    taken.Add(bounds);
                    break;
                }
            }
        }

        const float BuildingGap = 1.5f;      // between buildings
        const float BuildingRoomGap = 2.5f;  // between a building and a room

        // ------------------------------------------------------------------ connections

        /// <summary>A room or building, as seen by the connection pass.</summary>
        sealed class Structure
        {
            public Bounds Bounds;   // world
            public Vector3 Up;
            public int UpAxis;
            public float UpSign;
            public bool Solid;      // a building: connectors may sink into it a little
            public int Degree;
            public readonly List<float> Levels = new List<float>(); // walkable heights (world coordinate on the up axis)
        }

        readonly List<PavilionBox> connectorBoxes = new List<PavilionBox>();
        readonly List<Bounds> connectorBounds = new List<Bounds>();
        const int MaxLinks = 3;

        static int AxisOf(Vector3 v) => Mathf.Abs(v.x) > 0.5f ? 0 : Mathf.Abs(v.y) > 0.5f ? 1 : 2;
        static float Comp(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;
        static Vector3 Axes3(int a0, float v0, int a1, float v1, int a2, float v2)
        {
            var v = Vector3.zero;
            v[a0] = v0; v[a1] = v1; v[a2] = v2;
            return v;
        }

        Structure MakeStructure(Room r, bool solid)
        {
            var s = new Structure
            {
                Bounds = new Bounds(r.World(r.LocalBounds.center), Abs(r.Rotation * r.LocalBounds.size)),
                Up = r.Up,
                UpAxis = AxisOf(r.Up),
                Solid = solid,
            };
            s.UpSign = Comp(r.Up, s.UpAxis);
            foreach (float y in r.WalkLevels) s.Levels.Add(Comp(r.World(Vector3.up * y), s.UpAxis));
            return s;
        }

        /// <summary>Links neighbouring rooms and buildings: railed walkway bridges between ones that
        /// share a "down" (at a floor, roof or balcony height where possible), timber beams between
        /// ones that don't. First enough links to join everything into one cluster, then a few more.</summary>
        void Connect()
        {
            var all = new List<Structure>();
            foreach (var r in rooms) all.Add(MakeStructure(r, false));
            foreach (var b in buildings) all.Add(MakeStructure(b, true));

            // Pairs facing each other across a gap along one axis, overlapping enough on the other two.
            var candidates = new List<(int a, int b, int k, float gap)>();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    Bounds A = all[i].Bounds, B = all[j].Bounds;
                    for (int k = 0; k < 3; k++)
                    {
                        float gap = Mathf.Max(B.min[k] - A.max[k], A.min[k] - B.max[k]);
                        if (gap < 1f || gap > 26f) continue;
                        bool overlaps = true;
                        for (int o = 0; o < 3; o++)
                        {
                            if (o == k) continue;
                            if (Mathf.Min(A.max[o], B.max[o]) - Mathf.Max(A.min[o], B.min[o]) < 3f) overlaps = false;
                        }
                        if (overlaps) candidates.Add((i, j, k, gap));
                    }
                }
            candidates.Sort((x, y) => x.gap.CompareTo(y.gap));

            var parent = new int[all.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var linked = new HashSet<(int, int)>();

            // Join separate clusters first (shortest gaps first)...
            foreach (var c in candidates)
            {
                if (Find(c.a) == Find(c.b) || all[c.a].Degree >= MaxLinks || all[c.b].Degree >= MaxLinks) continue;
                if (!TryConnect(all, c.a, c.b, c.k)) continue;
                parent[Find(c.a)] = Find(c.b);
                linked.Add((c.a, c.b));
            }
            // ...then a few extra links for more routes.
            int extra = all.Count / 3;
            foreach (var c in candidates)
            {
                if (extra <= 0) break;
                if (linked.Contains((c.a, c.b)) || all[c.a].Degree >= MaxLinks || all[c.b].Degree >= MaxLinks || !Chance(0.5f)) continue;
                if (!TryConnect(all, c.a, c.b, c.k)) continue;
                linked.Add((c.a, c.b));
                extra--;
            }
        }

        bool TryConnect(List<Structure> all, int ia, int ib, int k)
        {
            Structure A = all[ia], B = all[ib];
            // Low = the one at the smaller coordinate along k.
            bool aLow = A.Bounds.max[k] <= B.Bounds.min[k];
            Structure low = aLow ? A : B, high = aLow ? B : A;
            // Sink a little into solid buildings so the joint looks built, not glued on.
            float s0 = low.Bounds.max[k] - (low.Solid ? 0.6f : 0f);
            float s1 = high.Bounds.min[k] + (high.Solid ? 0.6f : 0f);
            float span = s1 - s0, mid = (s0 + s1) * 0.5f;

            var parts = new List<PavilionBox>();
            bool built = false;

            if (A.Up == B.Up && A.UpAxis != k)
            {
                int a = A.UpAxis, w = 3 - k - a;
                float sign = A.UpSign;
                float loA = Mathf.Max(A.Bounds.min[a], B.Bounds.min[a]), hiA = Mathf.Min(A.Bounds.max[a], B.Bounds.max[a]);
                float loW = Mathf.Max(A.Bounds.min[w], B.Bounds.min[w]) + 1.6f, hiW = Mathf.Min(A.Bounds.max[w], B.Bounds.max[w]) - 1.6f;
                // A walk height that fits the overlap (deck below it, rail above): one both share
                // if there is one, else one of either side's.
                bool Fits(float L) => sign > 0f ? (L - 0.5f >= loA && L + 1.3f <= hiA) : (L + 0.5f <= hiA && L - 1.3f >= loA);
                float level = float.NaN;
                foreach (float la in A.Levels)
                    foreach (float lb in B.Levels)
                        if (Mathf.Abs(la - lb) < 0.3f && Fits(la)) level = la;
                if (float.IsNaN(level))
                {
                    var options = new List<float>();
                    foreach (float l in A.Levels) if (Fits(l)) options.Add(l);
                    foreach (float l in B.Levels) if (Fits(l)) options.Add(l);
                    if (options.Count > 0) level = options[Range(0, options.Count)];
                }
                if (float.IsNaN(level))
                {
                    // No shared floor height: still a walkway, somewhere in the height they share.
                    float lo = sign > 0f ? loA + 0.5f : loA + 1.3f, hi = sign > 0f ? hiA - 1.3f : hiA - 0.5f;
                    if (hi >= lo) level = Mathf.Round(Mathf.Lerp(lo, hi, Value()) * 2f) * 0.5f;
                    if (!float.IsNaN(level) && !Fits(level)) level = float.NaN;
                }
                if (!float.IsNaN(level) && hiW >= loW)
                {
                    float wPos = Mathf.Round(Mathf.Lerp(loW, hiW, Value()) * 2f) * 0.5f;
                    Bridge(parts, k, a, w, sign, s0, s1, level, wPos);
                    built = true;
                }
            }
            if (!built)
            {
                // Timber beams across the gap.
                int p = (k + 1) % 3, q = (k + 2) % 3;
                float loP = Mathf.Max(A.Bounds.min[p], B.Bounds.min[p]) + 0.8f, hiP = Mathf.Min(A.Bounds.max[p], B.Bounds.max[p]) - 0.8f;
                float loQ = Mathf.Max(A.Bounds.min[q], B.Bounds.min[q]) + 0.8f, hiQ = Mathf.Min(A.Bounds.max[q], B.Bounds.max[q]) - 0.8f;
                float pp = Mathf.Lerp(loP, hiP, Value()), qq = Mathf.Lerp(loQ, hiQ, Value());
                parts.Add(Box("Beam", Axes3(k, mid, p, pp, q, qq), Axes3(k, span, p, 0.8f, q, 0.8f), PavilionMat.Lacquer));
                if (hiP - loP > 2f)
                {
                    float pp2 = pp + (pp + 1.8f <= hiP ? 1.8f : -1.8f);
                    parts.Add(Box("Beam", Axes3(k, mid, p, pp2, q, qq), Axes3(k, span, p, 0.5f, q, 0.5f), PavilionMat.Wood));
                }
            }

            // Mustn't run into any other structure or connector.
            var bounds = new Bounds(parts[0].Center, Vector3.zero);
            foreach (var part in parts) bounds.Encapsulate(new Bounds(part.Center, part.Size));
            // Checked over the open gap between the two (the ends inside them don't count).
            float g0 = low.Bounds.max[k] + 0.05f, g1 = high.Bounds.min[k] - 0.05f;
            var check = new Bounds(bounds.center, bounds.size + Vector3.one * 0.4f);
            check.center = With(check.center, k, (g0 + g1) * 0.5f);
            check.size = With(check.size, k, Mathf.Max(0.1f, g1 - g0));
            for (int i = 0; i < all.Count; i++)
                if (i != ia && i != ib && all[i].Bounds.Intersects(check)) return false;
            foreach (var other in connectorBounds) if (other.Intersects(check)) return false;

            connectorBoxes.AddRange(parts);
            connectorBounds.Add(bounds);
            A.Degree++;
            B.Degree++;
            return true;
        }

        static Vector3 With(Vector3 v, int axis, float value) { v[axis] = value; return v; }

        static PavilionBox Box(string name, Vector3 center, Vector3 size, PavilionMat mat, bool collide = true) => new PavilionBox
        {
            Name = name, Center = center, Size = size, Rotation = Quaternion.identity, Material = mat, Collide = collide, Visible = true,
        };

        /// <summary>A railed walkway along axis k from s0 to s1, walking surface at <paramref name="level"/>
        /// on axis a (up is +a when sign &gt; 0), centred at <paramref name="wPos"/> across it, with
        /// lanterns along the rails and a beam underneath.</summary>
        void Bridge(List<PavilionBox> parts, int k, int a, int w, float sign, float s0, float s1, float level, float wPos)
        {
            float span = s1 - s0, mid = (s0 + s1) * 0.5f;
            float At(float height) => level + sign * height; // height above the walking surface
            parts.Add(Box("Bridge Deck", Axes3(k, mid, a, At(-0.175f), w, wPos), Axes3(k, span, a, 0.35f, w, 2.6f), PavilionMat.Wood));
            parts.Add(Box("Bridge Beam", Axes3(k, mid, a, At(-0.55f), w, wPos), Axes3(k, span, a, 0.4f, w, 0.6f), PavilionMat.Lacquer));
            for (int side = -1; side <= 1; side += 2)
            {
                float rw = wPos + side * 1.24f;
                parts.Add(Box("Bridge Rail", Axes3(k, mid, a, At(0.95f), w, rw), Axes3(k, span, a, 0.1f, w, 0.12f), PavilionMat.Wood));
                int posts = Mathf.Max(1, Mathf.FloorToInt(span / 2f));
                for (int i = 0; i <= posts; i++)
                {
                    float t = s0 + span * i / posts;
                    bool lantern = i % 2 == 0;
                    float h = lantern ? 1.3f : 0.95f;
                    parts.Add(Box("Bridge Post", Axes3(k, t, a, At(h * 0.5f), w, rw), Axes3(k, 0.12f, a, h, w, 0.12f), PavilionMat.Wood));
                    if (lantern)
                        parts.Add(Box("Bridge Lantern", Axes3(k, t, a, At(1.45f), w, rw), Axes3(k, 0.28f, a, 0.3f, w, 0.28f), PavilionMat.Lantern, collide: false));
                }
            }
        }

        void BuildSide(Room r, int s)
        {
            Vector3 outward = Outward[s], along = Along[s];
            float offset = r.Offset(s), half = r.HalfLength(s);
            Vector3 At(float a, float y, float inset = 0f) => outward * (offset - inset) + along * a + Vector3.up * y;

            switch (r.Sides[s])
            {
                case Side.Shoji:
                    Screen(r, At(0, WallHeight * 0.5f), half * 2f, WallHeight, along, outward);
                    break;
                case Side.TallShoji:
                    Screen(r, At(0, (r.Height - 0.3f) * 0.5f), half * 2f, r.Height - 0.3f, along, outward);
                    break;
                case Side.Door:
                {
                    float segment = half - Opening;
                    float mid = Opening + segment * 0.5f;
                    Screen(r, At(-mid, WallHeight * 0.5f), segment, WallHeight, along, outward);
                    Screen(r, At(mid, WallHeight * 0.5f), segment, WallHeight, along, outward);
                    P(r, "Door Lintel", At(0, 3.95f), SizeAlong(along, Opening * 2f, 0.5f, 0.3f), PavilionMat.Wood);
                    P(r, "Door Post", At(-Opening - 0.05f, WallHeight * 0.5f), SizeAlong(along, 0.2f, WallHeight, 0.28f), PavilionMat.Lacquer);
                    P(r, "Door Post", At(Opening + 0.05f, WallHeight * 0.5f), SizeAlong(along, 0.2f, WallHeight, 0.28f), PavilionMat.Lacquer);
                    // The sliding door itself; the portal sits on its inner face, facing into the room.
                    P(r, "Sliding Door", At(0, 1.85f), SizeAlong(along, Opening * 2f, 3.7f, 0.3f), PavilionMat.Wood);
                    r.DoorPortals.Add((At(0, 1.85f, 0.16f), -outward, s));
                    break;
                }
                case Side.Rail:
                    Rail(r, At(0, 0f), half * 2f, along);
                    break;
                case Side.None:
                    if (r.Annexes[s] != Annex.None) BuildAnnex(r, s);
                    break;
            }
        }

        /// <summary>A paper screen wall with a dark wooden lattice.</summary>
        void Screen(Room r, Vector3 center, float length, float height, Vector3 along, Vector3 outward)
        {
            P(r, "Shoji Paper", center, SizeAlong(along, length, height, 0.12f), PavilionMat.Paper);
            Vector3 inside = -outward * 0.08f;
            for (float a = -length * 0.5f; a <= length * 0.5f + 0.01f; a += 1.2f)
                P(r, "Shoji Bar", center + along * a + inside, SizeAlong(along, 0.06f, height, 0.05f), PavilionMat.Wood, false);
            for (float y = -height * 0.5f + 0.05f; y < height * 0.5f; y += 1.4f)
                P(r, "Shoji Rail", center + Vector3.up * y + inside, SizeAlong(along, length, 0.06f, 0.05f), PavilionMat.Wood, false);
        }

        /// <summary>A waist-high wooden rail standing on <paramref name="baseCenter"/>.</summary>
        void Rail(Room r, Vector3 baseCenter, float length, Vector3 along)
        {
            if (length < 0.3f) return;
            P(r, "Rail", baseCenter + Vector3.up * 0.95f, SizeAlong(along, length, 0.1f, 0.12f), PavilionMat.Wood);
            for (float a = -length * 0.5f + 0.5f; a < length * 0.5f; a += 2f)
                P(r, "Rail Post", baseCenter + along * a + Vector3.up * 0.47f, SizeAlong(along, 0.12f, 0.95f, 0.12f), PavilionMat.Wood);
        }

        /// <summary>A 45-degree flight climbing along <paramref name="run"/> from <paramref name="bottom"/>
        /// (room frame), real steps over a hidden walking ramp.</summary>
        void Stair(Room r, Vector3 bottom, Vector3 run, float rise, float width)
        {
            int count = Mathf.RoundToInt(rise / Step);
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.Cross(up, run);
            for (int i = 0; i < count; i++)
            {
                float v0 = i * Step - 0.4f, v1 = (i + 1) * Step;
                Vector3 c = bottom + run * ((i + 0.5f) * Step) + up * ((v0 + v1) * 0.5f);
                Vector3 size = Abs(side) * width + up * (v1 - v0) + Abs(run) * Step;
                r.Parts.Add(new Part { Name = $"Step {i + 1}", Center = c, Size = size, Mat = i % 2 == 0 ? PavilionMat.Wood : PavilionMat.Lacquer, Collide = i == count - 1 });
            }
            const float thickness = 0.3f;
            Vector3 a = bottom - run * Step;
            Vector3 b = bottom + run * (rise - Step) + up * rise;
            Vector3 n = (up - run).normalized;
            r.Parts.Add(new Part
            {
                Name = "Walkable Ramp (hidden)",
                Center = (a + b) * 0.5f - n * thickness * 0.5f,
                Size = new Vector3(width, thickness, (b - a).magnitude),
                Rotation = PavilionLayout.Basis((b - a).normalized, n),
                Collide = true, Visible = false, Ramp = true, RampOrigin = bottom,
            });
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        void BuildAnnex(Room r, int s)
        {
            Vector3 outward = Outward[s], along = Along[s];
            float offset = r.Offset(s), size = r.AnnexSize[s];
            Vector3 edge = outward * offset;
            const float platform = 6f;

            float floorY, start; // platform floor height, and its distance out from the edge
            switch (r.Annexes[s])
            {
                case Annex.StairDown:
                    floorY = -size; start = size;
                    Stair(r, edge + outward * size + Vector3.up * -size, -outward, size, 3f);
                    break;
                case Annex.StairUp:
                    floorY = size; start = size;
                    Stair(r, edge, outward, size, 3f);
                    break;
                default: // bridge
                    floorY = 0f; start = size;
                    P(r, "Bridge", edge + outward * (size * 0.5f) + Vector3.up * -0.2f, SizeAlong(along, 2.6f, 0.4f, size), PavilionMat.Wood);
                    Rail(r, edge + outward * (size * 0.5f) + along * 1.25f, size, outward);
                    Rail(r, edge + outward * (size * 0.5f) - along * 1.25f, size, outward);
                    break;
            }

            Vector3 center = edge + outward * (start + platform * 0.5f) + Vector3.up * floorY;
            P(r, "Platform", center + Vector3.up * -0.25f, SizeAlong(along, platform, 0.5f, platform), PavilionMat.Stone);
            // Rails round the three open sides, leaving the side that joins the stair/bridge.
            Rail(r, center + outward * (platform * 0.5f), platform, along);
            Rail(r, center + along * (platform * 0.5f), platform, outward);
            Rail(r, center - along * (platform * 0.5f), platform, outward);
            P(r, "Stone Lantern", center + outward * 2f + along * 2f + Vector3.up * 0.6f, new Vector3(0.5f, 1.2f, 0.5f), PavilionMat.Stone);
            P(r, "Lantern Light", center + outward * 2f + along * 2f + Vector3.up * 1.4f, new Vector3(0.35f, 0.35f, 0.35f), PavilionMat.Lantern, false);
            if (r.Annexes[s] == Annex.Bridge)
            {
                // A little roof on posts over the platform.
                for (int a = -1; a <= 1; a += 2)
                    for (int o = -1; o <= 1; o += 2)
                        P(r, "Gazebo Post", center + along * (a * 2.6f) + outward * (o * 2.6f) + Vector3.up * 1.6f, new Vector3(0.25f, 3.2f, 0.25f), PavilionMat.Lacquer);
                P(r, "Gazebo Roof", center + Vector3.up * 3.35f, SizeAlong(along, platform + 0.6f, 0.3f, platform + 0.6f), PavilionMat.Wood);
            }
            r.SpawnSpots.Add(center + along * -1.5f);
            r.Headroom.Add(new Bounds(center + Vector3.up * 1.6f, SizeAlong(along, platform, 3.2f, platform)));
            r.LightSpots.Add((center + Vector3.up * 2.6f, 10f, 1.6f));
        }

        // ------------------------------------------------------------------ room features

        /// <summary>Spots in front of each door (and the room centre) that must stay clear.</summary>
        bool Blocked(Room r, Vector3 p, float radius, bool keepCentre = true)
        {
            foreach (var (pos, rad) in r.Reserved)
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(pos.x, pos.z)) < rad + radius) return true;
            for (int s = 0; s < 4; s++)
            {
                if (r.Sides[s] != Side.Door) continue;
                Vector3 local = new Vector3(Vector3.Dot(p, Along[s]), 0f, Vector3.Dot(p, Outward[s]));
                // Clear 4 m in front of the door and 1 m either side of its opening.
                if (Mathf.Abs(local.x) < Opening + 1f + radius && local.z > r.Offset(s) - 4f - radius) return true;
            }
            return keepCentre && new Vector2(p.x, p.z).magnitude < 2.5f + radius; // spawn spot
        }

        void Scatter(Room r, int count, Vector3 size, PavilionMat mat, string name, float edge = 1.5f)
        {
            var placed = new List<Vector3>();
            for (int attempt = 0; attempt < 60 && placed.Count < count; attempt++)
            {
                float x = (Value() * 2f - 1f) * (r.W * 0.5f - edge - size.x * 0.5f);
                float z = (Value() * 2f - 1f) * (r.D * 0.5f - edge - size.z * 0.5f);
                var p = new Vector3(Mathf.Round(x * 2f) * 0.5f, 0f, Mathf.Round(z * 2f) * 0.5f);
                float radius = Mathf.Max(size.x, size.z) * 0.5f;
                if (Blocked(r, p, radius)) continue;
                bool clash = false;
                foreach (var q in placed) if (Vector3.Distance(p, q) < radius * 2f + 1.5f) clash = true;
                if (clash) continue;
                placed.Add(p);
                P(r, name, p + Vector3.up * (size.y * 0.5f), size, mat);
            }
        }

        void HallFeatures(Room r)
        {
            var crate = new Vector3(r.W * 0.25f, 0.5f, -r.D * 0.25f);
            bool hasCrate = Chance(0.5f) && !Blocked(r, crate, 0.6f);
            if (hasCrate)
            {
                r.PropSpots.Add(crate);
                r.Reserved.Add((crate, 1f));
            }
            Scatter(r, Range(2, 5), new Vector3(1.8f, 0.75f, 1f), PavilionMat.Lacquer, "Low Table");
            if (r.W >= 16f && r.D >= 16f)
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var p = new Vector3(sx * r.W * 0.25f, 0f, sz * r.D * 0.25f);
                        if (!Blocked(r, p, 0.4f))
                            P(r, "Inner Pillar", p + Vector3.up * (r.Height * 0.5f), new Vector3(0.45f, r.Height, 0.45f), PavilionMat.Lacquer);
                    }
            r.SpawnSpots.Add(Vector3.zero);
        }

        void TeaRoomFeatures(Room r)
        {
            Scatter(r, 1, new Vector3(1.4f, 0.6f, 1.4f), PavilionMat.Lacquer, "Tea Table", 1.2f);
            r.SpawnSpots.Add(Vector3.zero);
        }

        void GalleryFeatures(Room r)
        {
            // Pillars down both sides and lanterns along the middle.
            float x = r.W * 0.5f - 1.3f;
            for (float z = -r.D * 0.5f + 4f; z <= r.D * 0.5f - 4f; z += 4f)
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    var p = new Vector3(sx * x, 0f, z);
                    if (Blocked(r, p, 0.3f, keepCentre: false)) continue; // not in front of a door
                    P(r, "Gallery Pillar", p + Vector3.up * (r.Height * 0.5f), new Vector3(0.4f, r.Height, 0.4f), PavilionMat.Lacquer);
                }
            for (float z = -r.D * 0.5f + 3f; z <= r.D * 0.5f - 3f; z += 6f)
                P(r, "Lantern", new Vector3(0, 3.6f, z), new Vector3(0.4f, 0.55f, 0.4f), PavilionMat.Lantern, false);
            r.SpawnSpots.Add(new Vector3(0, 0, -r.D * 0.25f));
            // Two lights down the length (instead of the usual one in the middle).
            r.LightSpots.Add((new Vector3(0, 3.4f, r.D * 0.25f), r.D * 0.5f + 6f, 2.4f));
            r.LightSpots.Add((new Vector3(0, 3.4f, -r.D * 0.25f), r.D * 0.5f + 6f, 2.4f));
        }

        void TallHallFeatures(Room r)
        {
            int m = r.MezzSide, k = r.StairSide;
            Vector3 om = Outward[m], am = Along[m], ok = Outward[k];
            float offM = r.Offset(m), halfM = r.HalfLength(m), offK = r.Offset(k);
            const float depth = 4f, level = 4f, width = 2.4f;

            // The raised floor along side m.
            Vector3 mezzCenter = om * (offM - 0.1f - (depth - 0.1f) * 0.5f) + Vector3.up * (level - 0.25f);
            P(r, "Raised Floor", mezzCenter, SizeAlong(am, halfM * 2f - 0.5f, 0.5f, depth - 0.1f), PavilionMat.Tatami);

            // Staircase against side k, climbing toward side m and arriving at the raised floor's edge.
            float lateral = offK - 0.35f - width * 0.5f;
            Vector3 top = om * (offM - depth) + ok * lateral;
            Vector3 bottom = top - om * level;
            Stair(r, bottom, om, level, width);

            // Rail along the raised floor's edge, with a gap where the stair arrives.
            float sign = Vector3.Dot(ok, am); // +1 or -1: where side k sits along side m
            float g0 = sign * (lateral - width * 0.5f - 0.1f), g1 = sign * (lateral + width * 0.5f + 0.1f);
            float gapMin = Mathf.Min(g0, g1), gapMax = Mathf.Max(g0, g1);
            float lo = -halfM + 0.3f, hi = halfM - 0.3f;
            Vector3 edgeBase = om * (offM - depth) + Vector3.up * level;
            if (gapMin > lo) Rail(r, edgeBase + am * ((lo + gapMin) * 0.5f), gapMin - lo, am);
            if (hi > gapMax) Rail(r, edgeBase + am * ((gapMax + hi) * 0.5f), hi - gapMax, am);

            Scatter(r, Range(1, 3), new Vector3(1.8f, 0.75f, 1f), PavilionMat.Lacquer, "Low Table", 2.5f);
            r.SpawnSpots.Add(Vector3.zero);
            r.SpawnSpots.Add(om * (offM - depth * 0.5f) + am * (sign * -halfM * 0.5f) + Vector3.up * level);
            r.LightSpots.Add((om * (offM - depth * 0.5f) + Vector3.up * (level + 2.5f), 10f, 1.8f));
        }

        void TerraceFeatures(Room r)
        {
            // A small shrine in the middle for cover, stone lanterns at the corners.
            // The shrine sits off-centre (the spawn spot is on the other side of the middle);
            // try the four diagonals for one that's clear of the doorways.
            var shrine = Vector3.zero;
            bool hasShrine = false;
            int first = Range(0, 4);
            for (int i = 0; i < 4 && !hasShrine; i++)
            {
                int q = (first + i) % 4;
                var c = new Vector3(q % 2 == 0 ? -1.5f : 1.5f, 0f, q < 2 ? -1.5f : 1.5f);
                if (Blocked(r, c, 1.3f, keepCentre: false) || Blocked(r, -c * 1.6f, 0.6f, keepCentre: false)) continue;
                shrine = c;
                hasShrine = true;
            }
            if (hasShrine)
            {
                P(r, "Shrine Base", shrine + Vector3.up * 0.2f, new Vector3(2.4f, 0.4f, 2.4f), PavilionMat.Stone);
                P(r, "Shrine", shrine + Vector3.up * 1.2f, new Vector3(1.4f, 1.6f, 1.4f), PavilionMat.Wood);
                P(r, "Shrine Roof", shrine + Vector3.up * 2.15f, new Vector3(2.2f, 0.3f, 2.2f), PavilionMat.Lacquer);
            }
            else shrine = new Vector3(1.5f, 0f, 1.5f); // only used for the spawn and crate spots below
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var p = new Vector3(sx * (r.W * 0.5f - 1.2f), 0f, sz * (r.D * 0.5f - 1.2f));
                    if (Blocked(r, p, 0.4f)) continue;
                    P(r, "Stone Lantern", p + Vector3.up * 0.6f, new Vector3(0.5f, 1.2f, 0.5f), PavilionMat.Stone);
                    P(r, "Lantern Light", p + Vector3.up * 1.4f, new Vector3(0.35f, 0.35f, 0.35f), PavilionMat.Lantern, false);
                }
            r.SpawnSpots.Add(-shrine * 1.6f);
            if (hasShrine) r.PropSpots.Add(shrine + new Vector3(0f, 0.5f, shrine.z > 0 ? -2f : 2f));
        }

        // ------------------------------------------------------------------ placement

        void Place()
        {
            for (int attempt = 0; ; attempt++)
            {
                if (TryPlaceAll(attempt < 30 ? Margin : 1f)) return;
                if (attempt > 60) throw new InvalidOperationException($"Pavilion seed {layout.Seed}: couldn't fit the rooms.");
            }
        }

        bool TryPlaceAll(float margin)
        {
            var placed = new List<Bounds>();
            // Biggest first: they're the hardest to fit.
            var order = new List<Room>(rooms);
            order.Sort((a, b) => (b.LocalBounds.size.x * b.LocalBounds.size.y * b.LocalBounds.size.z)
                .CompareTo(a.LocalBounds.size.x * a.LocalBounds.size.y * a.LocalBounds.size.z));
            foreach (var r in order)
            {
                Vector3 c = r.Rotation * r.LocalBounds.center;
                Vector3 e = Abs(r.Rotation * r.LocalBounds.extents);
                bool ok = false;
                for (int t = 0; t < 400 && !ok; t++)
                {
                    var min = Vector3.one * FaceMargin + e - c;
                    var max = Vector3.one * (PavilionLayout.CellSize - FaceMargin) - e - c;
                    if (min.x > max.x || min.y > max.y || min.z > max.z) return false;
                    var pos = new Vector3(
                        Mathf.Round(Mathf.Lerp(min.x, max.x, Value()) * 2f) * 0.5f,
                        Mathf.Round(Mathf.Lerp(min.y, max.y, Value()) * 2f) * 0.5f,
                        Mathf.Round(Mathf.Lerp(min.z, max.z, Value()) * 2f) * 0.5f);
                    var bounds = new Bounds(pos + c, e * 2f + Vector3.one * (margin * 2f)); // margin on every side
                    bool clash = false;
                    foreach (var other in placed) if (other.Intersects(bounds)) { clash = true; break; }
                    if (clash) continue;
                    r.Position = pos;
                    placed.Add(new Bounds(pos + c, e * 2f));
                    ok = true;
                }
                if (!ok) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ output

        void Emit()
        {
            var doorPortal = new Dictionary<(int room, int side), int>();
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                foreach (var p in r.Parts)
                {
                    layout.Boxes.Add(new PavilionBox
                    {
                        Name = $"{r.Name}: {p.Name}",
                        Center = r.World(p.Center),
                        Size = p.Size,
                        Rotation = r.Rotation * p.Rotation,
                        Material = p.Mat,
                        Collide = p.Collide,
                        Visible = p.Visible,
                        StairRamp = p.Ramp,
                        StairOrigin = r.World(p.RampOrigin),
                    });
                }
                foreach (var (pos, normal, side) in r.DoorPortals)
                {
                    doorPortal[(i, side)] = layout.Portals.Count;
                    layout.Portals.Add(new PavilionPortal
                    {
                        Name = $"{r.Name} Door ({r.DoorLink[side]})",
                        Position = r.World(pos),
                        Normal = r.Rotation * normal,
                        Up = r.Up,
                        Size = new Vector2(2.4f, 3.6f),
                        Color = PairColors[r.DoorLink[side] % PairColors.Length],
                        Doorway = true,
                        Link = -1,
                    });
                }
                foreach (var (pos, range, intensity) in r.LightSpots)
                    layout.Lights.Add(new PavilionLight { Position = r.World(pos), Range = range, Intensity = intensity * 1.35f, Color = new Color(1f, 0.74f, 0.5f) });
                foreach (var s in r.SpawnSpots)
                {
                    // Face the first door, else along the room.
                    Vector3 facing = Vector3.forward;
                    for (int d = 0; d < 4; d++) if (r.Sides[d] == Side.Door) { facing = Outward[d]; break; }
                    layout.Spawns.Add(new PavilionMarker { Position = r.World(s + Vector3.up * 0.95f), Rotation = r.Rotation * PavilionLayout.Basis(facing, Vector3.up) });
                }
                foreach (var s in r.PropSpots)
                    layout.Props.Add(new PavilionMarker { Position = r.World(s), Rotation = r.Rotation });

                var bounds = new Bounds(r.World(r.LocalBounds.center), Abs(r.Rotation * r.LocalBounds.size));
                layout.Rooms.Add(new PavilionRoomInfo { Name = r.Name, Bounds = bounds, Up = r.Up, Doors = r.DoorCount });
            }

            foreach (var b in buildings)
            {
                foreach (var p in b.Parts)
                {
                    layout.Boxes.Add(new PavilionBox
                    {
                        Name = $"{b.Name}: {p.Name}",
                        Center = b.World(p.Center),
                        Size = p.Size,
                        Rotation = b.Rotation * p.Rotation,
                        Material = p.Mat,
                        Collide = p.Collide,
                        Visible = p.Visible,
                    });
                }
                layout.Buildings.Add(new Bounds(b.World(b.LocalBounds.center), Abs(b.Rotation * b.LocalBounds.size)));
            }
            layout.Boxes.AddRange(connectorBoxes);
            layout.Connectors.AddRange(connectorBounds);

            foreach (var e in edges)
            {
                int a = doorPortal[(e.a, e.sa)], b = doorPortal[(e.b, e.sb)];
                var pa = layout.Portals[a]; pa.Link = b; layout.Portals[a] = pa;
                var pb = layout.Portals[b]; pb.Link = a; layout.Portals[b] = pb;
            }
        }

        /// <summary>Each face of the cell is an invisible portal to the opposite face.</summary>
        void WrapWalls()
        {
            float s = PavilionLayout.CellSize, h = s * 0.5f;
            void Pair(string a, Vector3 aPos, Vector3 aNormal, string b, Vector3 bPos, Vector3 bNormal, Vector3 upHint)
            {
                int ia = layout.Portals.Count;
                layout.Portals.Add(new PavilionPortal { Name = a, Position = aPos, Normal = aNormal, Up = upHint, Size = new Vector2(s, s), WorldWrap = true, Color = Color.white, Link = ia + 1 });
                layout.Portals.Add(new PavilionPortal { Name = b, Position = bPos, Normal = bNormal, Up = upHint, Size = new Vector2(s, s), WorldWrap = true, Color = Color.white, Link = ia });
            }
            Pair("Wrap West", new Vector3(0, h, h), Vector3.right, "Wrap East", new Vector3(s, h, h), Vector3.left, Vector3.up);
            Pair("Wrap Floor", new Vector3(h, 0, h), Vector3.up, "Wrap Ceiling", new Vector3(h, s, h), Vector3.down, Vector3.forward);
            Pair("Wrap South", new Vector3(h, h, 0), Vector3.forward, "Wrap North", new Vector3(h, h, s), Vector3.back, Vector3.up);
        }
    }
}
