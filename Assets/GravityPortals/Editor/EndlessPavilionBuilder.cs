using System.Collections.Generic;
using GravityPortals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "Endless Pavilion": traditional wooden rooms floating in a dark, foggy void, each facing a
/// different direction. Sliding doors are portals that lead between rooms with different
/// "down"s, a long staircase climbs between them, and every wall of the space is a portal to
/// the opposite wall, so it repeats forever (falling forever counts as falling into the void).
///
/// Every room is described in its own frame (floor at y = 0, up = +Y) and rotated into place;
/// doors are upright in their room, so walking through one brings you out upright in the
/// room on the other side, whatever its orientation.
/// </summary>
[InitializeOnLoad]
public static class EndlessPavilionBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/EndlessPavilion.unity";
    const string VersionKey = "ClaudeTest.EndlessPavilion.Version";
    const int Version = 2; // 2 = wrap-around portal walls instead of a void

    static readonly Vector3 Center = new Vector3(32f, 32f, 32f);
    static readonly Color Fog = new Color(0.07f, 0.035f, 0.03f);

    enum Side { Door, Shoji, Rail, None }

    sealed class Room
    {
        public string Name;
        public Vector3 Position;     // floor center
        public Quaternion Rotation;  // local +Y -> the room's up
        public float Width, Depth;   // along local X and Z
        public Side West, East, North, South;
        public int WestDoor = -1, EastDoor = -1, NorthDoor = -1, SouthDoor = -1; // pair index per door
        public Vector3 World(Vector3 local) => Position + Rotation * local;
        public Vector3 Up => Rotation * Vector3.up;
    }

    static Material tatami, wood, lacquer, paper, lantern;

    static EndlessPavilionBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build Endless Pavilion")]
    public static void Build()
    {
        GravityArenaBuilder.CameraBackground = Fog;
        GravityArenaBuilder.BuildScene(ScenePath, Layout, new Vector3(54f, 22f, 52f), new Vector3(32f, 22f, 30f));
    }

    // ------------------------------------------------------------------ layout

    static List<Room> Rooms() => new List<Room>
    {
        // Main hall (spawn). South side is open onto the staircase.
        new Room { Name = "Main Hall", Position = new Vector3(32, 6, 32), Rotation = Quaternion.identity, Width = 20, Depth = 20,
                   West = Side.Door, WestDoor = 0, East = Side.Door, EastDoor = 1, North = Side.Door, NorthDoor = 2, South = Side.None },
        // Small gallery at the top of the staircase.
        new Room { Name = "Gallery", Position = new Vector3(38, 16, 7), Rotation = Quaternion.identity, Width = 10, Depth = 10,
                   West = Side.Door, WestDoor = 6, East = Side.Shoji, North = Side.None, South = Side.Rail },
        // Rooms on their sides and upside down.
        new Room { Name = "West Room (floor faces east)", Position = new Vector3(8, 30, 30), Rotation = Quaternion.Euler(0, 0, -90), Width = 14, Depth = 14,
                   West = Side.Shoji, East = Side.Rail, North = Side.Door, NorthDoor = 0, South = Side.Door, SouthDoor = 3 },
        new Room { Name = "East Room (floor faces west)", Position = new Vector3(56, 30, 30), Rotation = Quaternion.Euler(0, 0, 90), Width = 14, Depth = 14,
                   West = Side.Rail, East = Side.Shoji, North = Side.Door, NorthDoor = 1, South = Side.Door, SouthDoor = 4 },
        new Room { Name = "North Room (floor faces south)", Position = new Vector3(32, 30, 56), Rotation = Quaternion.Euler(-90, 0, 0), Width = 14, Depth = 14,
                   West = Side.Door, WestDoor = 5, East = Side.Door, EastDoor = 2, North = Side.Shoji, South = Side.Rail },
        new Room { Name = "South Room (floor faces north)", Position = new Vector3(32, 36, 8), Rotation = Quaternion.Euler(90, 0, 0), Width = 14, Depth = 14,
                   West = Side.Door, WestDoor = 6, East = Side.Door, EastDoor = 4, North = Side.Rail, South = Side.Shoji },
        new Room { Name = "Inverted Room (floor faces down)", Position = new Vector3(32, 52, 32), Rotation = Quaternion.Euler(180, 0, 0), Width = 16, Depth = 16,
                   West = Side.Door, WestDoor = 5, East = Side.Door, EastDoor = 3, North = Side.Shoji, South = Side.Rail },
    };

    static readonly Color[] PairColors =
    {
        new Color(1f, 0.55f, 0.2f), new Color(0.95f, 0.25f, 0.3f), new Color(1f, 0.8f, 0.35f), new Color(0.7f, 0.35f, 1f),
        new Color(0.3f, 0.8f, 1f), new Color(0.4f, 1f, 0.6f), new Color(1f, 0.45f, 0.75f),
    };

    static void Layout()
    {
        CreateMaterials();
        Atmosphere();

        var rooms = Rooms();
        var doors = new Dictionary<int, List<Portal>>();
        foreach (var room in rooms) BuildRoom(room, doors);

        foreach (var pair in doors)
        {
            if (pair.Value.Count != 2)
            {
                Debug.LogError($"[EndlessPavilion] Door pair {pair.Key} has {pair.Value.Count} doors (needs 2).");
                continue;
            }
            pair.Value[0].color = pair.Value[1].color = PairColors[pair.Key % PairColors.Length];
            GravityArenaBuilder.Link(pair.Value[0], pair.Value[1]);
        }

        // The long staircase from the main hall up into the gallery.
        Staircase("Great Stair", bottom: new Vector3(38, 6, 22), run: Vector3.back, up: Vector3.up, rise: 10f, width: 3f);

        Spawns(rooms);
        WrapWalls();
    }

    /// <summary>
    /// Each face of the 64 m cube is an invisible portal to the opposite face, so the pavilion
    /// repeats forever in every direction: walk off one side and you come in from the other,
    /// fall through the floor and you drop from the ceiling. Look-only copies of the level fill
    /// the neighbouring cells (made at load time) so you can see it go on.
    /// </summary>
    static void WrapWalls()
    {
        float s = Size;
        var face = new Vector2(s, s);
        void Pair(string a, Vector3 aPos, Vector3 aNormal, string b, Vector3 bPos, Vector3 bNormal, Vector3 upHint)
        {
            var pa = GravityArenaBuilder.Make(a, aPos, aNormal, upHint, face, Color.white);
            var pb = GravityArenaBuilder.Make(b, bPos, bNormal, upHint, face, Color.white);
            pa.worldWrap = pb.worldWrap = true;
            GravityArenaBuilder.Link(pa, pb);
        }
        Pair("Wrap West", new Vector3(0, s / 2, s / 2), Vector3.right, "Wrap East", new Vector3(s, s / 2, s / 2), Vector3.left, Vector3.up);
        Pair("Wrap Floor", new Vector3(s / 2, 0, s / 2), Vector3.up, "Wrap Ceiling", new Vector3(s / 2, s, s / 2), Vector3.down, Vector3.forward);
        Pair("Wrap South", new Vector3(s / 2, s / 2, 0), Vector3.forward, "Wrap North", new Vector3(s / 2, s / 2, s), Vector3.back, Vector3.up);

        var copies = new GameObject("Wrap-Around Copies").AddComponent<WrapAroundCopies>();
        copies.source = GravityArenaBuilder.GeometryRoot;
        copies.period = Vector3.one * s;
        copies.range = 1;
    }

    const float Size = 64f; // the pavilion is one cell of an endless 64 m lattice

    static void CreateMaterials()
    {
        var grid = GravityArenaBuilder.GridTexture();
        tatami = GravityArenaBuilder.LitMaterial("Pavilion Tatami", new Color(0.66f, 0.62f, 0.42f), grid, new Vector2(7, 7));
        wood = GravityArenaBuilder.LitMaterial("Pavilion Wood", new Color(0.2f, 0.12f, 0.08f), null, Vector2.one);
        lacquer = GravityArenaBuilder.LitMaterial("Pavilion Lacquer", new Color(0.55f, 0.09f, 0.06f), null, Vector2.one);
        // Paper screens glow softly, as if lit from behind.
        paper = GravityArenaBuilder.EmissiveMaterial("Pavilion Paper", new Color(1f, 0.82f, 0.6f) * 0.55f);
        lantern = GravityArenaBuilder.EmissiveMaterial("Pavilion Lantern", new Color(1f, 0.5f, 0.18f) * 3f);
    }

    static void Atmosphere()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.012f; // thin enough to see a couple of repeats before it fades
        RenderSettings.fogColor = Fog;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.32f, 0.23f, 0.19f);

        var key = new GameObject("Key Light").AddComponent<Light>();
        key.transform.SetParent(GravityArenaBuilder.LightRoot, false);
        key.type = LightType.Directional;
        key.intensity = 0.35f;
        key.color = new Color(1f, 0.8f, 0.65f);
        key.shadows = LightShadows.None;
        key.transform.rotation = Quaternion.LookRotation(new Vector3(-0.4f, -1f, 0.3f));
    }

    // ------------------------------------------------------------------ rooms

    static void BuildRoom(Room r, Dictionary<int, List<Portal>> doors)
    {
        var root = new GameObject(r.Name).transform;
        root.SetParent(GravityArenaBuilder.GeometryRoot, false);
        float hw = r.Width * 0.5f, hd = r.Depth * 0.5f;
        const float height = 4.5f;

        Part(r, root, "Floor", new Vector3(0, -0.25f, 0), new Vector3(r.Width, 0.5f, r.Depth), tatami);
        // Dark wood border round the floor and a beam frame overhead.
        Part(r, root, "Floor Edge W", new Vector3(-hw + 0.15f, 0.03f, 0), new Vector3(0.3f, 0.06f, r.Depth), wood, collide: false);
        Part(r, root, "Floor Edge E", new Vector3(hw - 0.15f, 0.03f, 0), new Vector3(0.3f, 0.06f, r.Depth), wood, collide: false);
        Part(r, root, "Floor Edge N", new Vector3(0, 0.03f, hd - 0.15f), new Vector3(r.Width, 0.06f, 0.3f), wood, collide: false);
        Part(r, root, "Floor Edge S", new Vector3(0, 0.03f, -hd + 0.15f), new Vector3(r.Width, 0.06f, 0.3f), wood, collide: false);
        Part(r, root, "Beam W", new Vector3(-hw, height, 0), new Vector3(0.35f, 0.35f, r.Depth), wood);
        Part(r, root, "Beam E", new Vector3(hw, height, 0), new Vector3(0.35f, 0.35f, r.Depth), wood);
        Part(r, root, "Beam N", new Vector3(0, height, hd), new Vector3(r.Width, 0.35f, 0.35f), wood);
        Part(r, root, "Beam S", new Vector3(0, height, -hd), new Vector3(r.Width, 0.35f, 0.35f), wood);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Part(r, root, "Pillar", new Vector3(sx * hw, height * 0.5f, sz * hd), new Vector3(0.45f, height + 0.2f, 0.45f), lacquer);

        BuildSide(r, root, Vector3.left, Vector3.forward, hw, hd, r.West, r.WestDoor, doors);
        BuildSide(r, root, Vector3.right, Vector3.forward, hw, hd, r.East, r.EastDoor, doors);
        BuildSide(r, root, Vector3.forward, Vector3.right, hd, hw, r.North, r.NorthDoor, doors);
        BuildSide(r, root, Vector3.back, Vector3.right, hd, hw, r.South, r.SouthDoor, doors);

        // Two hanging lanterns and one warm light per room.
        Part(r, root, "Lantern", new Vector3(-hw + 1.2f, 3.5f, -hd + 1.2f), new Vector3(0.45f, 0.6f, 0.45f), lantern, collide: false);
        Part(r, root, "Lantern", new Vector3(hw - 1.2f, 3.5f, hd - 1.2f), new Vector3(0.45f, 0.6f, 0.45f), lantern, collide: false);
        var light = new GameObject($"{r.Name} Light").AddComponent<Light>();
        light.transform.SetParent(GravityArenaBuilder.LightRoot, false);
        light.transform.position = r.World(new Vector3(0, 3.4f, 0));
        light.type = LightType.Point;
        light.range = Mathf.Max(r.Width, r.Depth) + 8f;
        light.intensity = 2.6f;
        light.color = new Color(1f, 0.72f, 0.48f);
        light.shadows = LightShadows.None;
    }

    /// <summary>One side of a room. <paramref name="outward"/> points out of the room,
    /// <paramref name="along"/> runs along the side; both in the room's frame.</summary>
    static void BuildSide(Room r, Transform root, Vector3 outward, Vector3 along, float offset, float halfLength,
        Side kind, int pair, Dictionary<int, List<Portal>> doors)
    {
        const float wallHeight = 4.2f, thick = 0.12f, opening = 1.3f;
        Vector3 Size(float length, float h, float t) => along.x != 0 ? new Vector3(length, h, t) : new Vector3(t, h, length);
        Vector3 At(float a, float y, float inset = 0f) => outward * (offset - inset) + along * a + Vector3.up * y;

        switch (kind)
        {
            case Side.Shoji:
                Screen(r, root, At(0, wallHeight * 0.5f), halfLength * 2f, along, outward, Size);
                break;

            case Side.Door:
            {
                float segment = halfLength - opening;
                float mid = opening + segment * 0.5f;
                Screen(r, root, At(-mid, wallHeight * 0.5f), segment, along, outward, Size);
                Screen(r, root, At(mid, wallHeight * 0.5f), segment, along, outward, Size);
                Part(r, root, "Door Lintel", At(0, 3.95f), Size(opening * 2f, 0.5f, 0.3f), wood);
                Part(r, root, "Door Post", At(-opening - 0.05f, wallHeight * 0.5f), Size(0.2f, wallHeight, 0.28f), lacquer);
                Part(r, root, "Door Post", At(opening + 0.05f, wallHeight * 0.5f), Size(0.2f, wallHeight, 0.28f), lacquer);
                // The sliding door itself; the portal sits on its inner face, facing into the room.
                Part(r, root, "Sliding Door", At(0, 1.85f), Size(opening * 2f, 3.7f, 0.3f), wood);
                var portal = GravityArenaBuilder.Make($"{r.Name} Door ({pair})",
                    r.World(At(0, 1.85f, 0.16f)), r.Rotation * -outward, r.Up, new Vector2(2.4f, 3.6f), Color.white);
                portal.doorway = true;
                if (!doors.TryGetValue(pair, out var list)) doors[pair] = list = new List<Portal>();
                list.Add(portal);
                break;
            }

            case Side.Rail:
                Part(r, root, "Rail", At(0, 0.95f), Size(halfLength * 2f, 0.1f, 0.12f), wood);
                for (float a = -halfLength + 1f; a < halfLength; a += 2f)
                    Part(r, root, "Rail Post", At(a, 0.47f), Size(0.12f, 0.95f, 0.12f), wood);
                break;
        }
    }

    /// <summary>A paper screen wall with a dark wooden lattice.</summary>
    static void Screen(Room r, Transform root, Vector3 center, float length, Vector3 along, Vector3 outward,
        System.Func<float, float, float, Vector3> size)
    {
        const float wallHeight = 4.2f;
        Part(r, root, "Shoji Paper", center, size(length, wallHeight, 0.12f), paper);
        Vector3 inside = -outward * 0.08f;
        for (float a = -length * 0.5f; a <= length * 0.5f + 0.01f; a += 1.2f)
            Part(r, root, "Shoji Bar", center + along * a + inside, size(0.06f, wallHeight, 0.05f), wood, collide: false);
        foreach (float y in new[] { -wallHeight * 0.5f + 0.05f, -0.7f, 0.7f, wallHeight * 0.5f - 0.05f })
            Part(r, root, "Shoji Rail", center + Vector3.up * y + inside, size(length, 0.06f, 0.05f), wood, collide: false);
    }

    static GameObject Part(Room r, Transform root, string name, Vector3 localCenter, Vector3 localSize, Material mat, bool collide = true)
    {
        var go = GravityArenaBuilder.Block(name, r.World(localCenter), localSize, mat, r.Rotation);
        go.transform.SetParent(root, true);
        if (!collide) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // ------------------------------------------------------------------ stairs

    /// <summary>A 45-degree flight of real-proportion steps over a hidden walking ramp (see StairSteps).</summary>
    static void Staircase(string name, Vector3 bottom, Vector3 run, Vector3 up, float rise, float width)
    {
        const float step = 0.25f, thickness = 0.3f;
        int count = Mathf.RoundToInt(rise / step);
        var root = new GameObject(name).transform;
        root.SetParent(GravityArenaBuilder.GeometryRoot, false);
        var rot = Quaternion.LookRotation(run, up);

        for (int i = 0; i < count; i++)
        {
            float v0 = i * step - 0.4f, v1 = (i + 1) * step;
            Vector3 c = bottom + run * ((i + 0.5f) * step) + up * ((v0 + v1) * 0.5f);
            var go = GravityArenaBuilder.Block($"Step {i + 1}", c, new Vector3(width, v1 - v0, step), i % 2 == 0 ? wood : lacquer, rot);
            go.transform.SetParent(root, true);
            if (i < count - 1) Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        // Lanterns along the flight.
        for (int i = 8; i < count; i += 12)
        {
            Vector3 p = bottom + run * (i * step) + up * (i * step + 1.4f) + Vector3.Cross(up, run).normalized * (width * 0.5f + 0.3f);
            var l = GravityArenaBuilder.Block("Stair Lantern", p, new Vector3(0.35f, 0.5f, 0.35f), lantern, rot);
            l.transform.SetParent(root, true);
            Object.DestroyImmediate(l.GetComponent<Collider>());
        }

        Vector3 a = bottom - run * step;
        Vector3 b = bottom + run * (rise - step) + up * rise;
        Vector3 n = (up - run).normalized;
        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "Walkable Ramp (hidden)";
        ramp.transform.SetParent(root, false);
        ramp.transform.SetPositionAndRotation((a + b) * 0.5f - n * thickness * 0.5f, Quaternion.LookRotation(b - a, n));
        ramp.transform.localScale = new Vector3(width, thickness, (b - a).magnitude);
        Object.DestroyImmediate(ramp.GetComponent<MeshRenderer>());
        Object.DestroyImmediate(ramp.GetComponent<MeshFilter>());
        GameObjectUtility.SetStaticEditorFlags(ramp, (StaticEditorFlags)~0);
        var steps = ramp.AddComponent<StairSteps>();
        steps.origin = bottom;
        steps.stepSize = step;
    }

    // ------------------------------------------------------------------ spawns

    static void Spawns(List<Room> rooms)
    {
        void At(Room r, Vector3 local, Vector3 forward, string label) =>
            GravityArenaBuilder.AddSpawnPoint($"Spawn {label}", r.World(local), Quaternion.LookRotation(r.Rotation * forward, r.Up));

        var main = rooms[0];
        At(main, new Vector3(-5, 0.95f, -5), Vector3.forward, "Main Hall A");
        At(main, new Vector3(5, 0.95f, 5), Vector3.back, "Main Hall B");
        At(main, new Vector3(-5, 0.95f, 5), Vector3.right, "Main Hall C");
        At(main, new Vector3(5, 0.95f, -3), Vector3.left, "Main Hall D");
        for (int i = 2; i < rooms.Count - 1; i++) // the four side rooms
            At(rooms[i], new Vector3(0, 0.95f, 0), Vector3.forward, rooms[i].Name);
    }
}
