using GravityPortals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "Portal Test Lab": a corridor lined with 24 numbered test rooms, each isolating one portal
/// combination. Rooms 1-11 are entered by dropping into a floor portal, rooms 13-23 by walking
/// through a door; across those the exit sits on every surface and at several angles. Room 12
/// is an infinite fall (floor portal under a ceiling portal) and room 24 has two floor portals
/// side by side. The sign over each doorway says what the exit is and where you should land,
/// worked out with the game's own portal rule. H takes you back to the start.
/// </summary>
[InitializeOnLoad]
public static class PortalTestLabBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/PortalTestLab.unity";
    const string VersionKey = "ClaudeTest.PortalTestLab.Version";
    const int Version = 2; // 2 = corridor of test rooms

    // Each room's interior, in its own frame: x -6..6 (left..right), y 0..12, z -6..6 (front..back).
    // The front wall has a doorway onto the corridor.
    const float Half = 6f, Height = 12f, Thick = 0.5f;
    const float Spacing = 14f, FirstX = 8f;
    const int PerRow = 12;

    static Material floorMat, ceilingMat, backMat, frontMat, leftMat, rightMat, panelMat, corridorMat;
    static Font font;

    enum Entry { FloorPortal, Door }

    struct Exit
    {
        public string Label;
        public Vector3 Position, Normal, Up;   // room frame
        public bool Door;                       // door-sized exit
        public bool Slope;                      // mounted on a 45-degree panel
    }

    static readonly Vector3 SlopeNormal = new Vector3(-1f, 1f, 0f).normalized; // faces up and to the left
    static readonly Vector3 SlopeUphill = new Vector3(1f, 1f, 0f).normalized;

    static readonly Exit[] Exits =
    {
        new Exit { Label = "Floor",                Position = new Vector3(3, 0, 2),     Normal = Vector3.up,      Up = Vector3.forward },
        new Exit { Label = "Ceiling",              Position = new Vector3(3, Height, 2), Normal = Vector3.down,   Up = Vector3.forward },
        new Exit { Label = "Back wall",            Position = new Vector3(3, 6, Half),  Normal = Vector3.back,    Up = Vector3.up },
        new Exit { Label = "Front wall",           Position = new Vector3(3, 7.5f, -Half), Normal = Vector3.forward, Up = Vector3.up },
        new Exit { Label = "Right wall",           Position = new Vector3(Half, 6, 2),  Normal = Vector3.left,    Up = Vector3.up },
        new Exit { Label = "Left wall",            Position = new Vector3(-Half, 6, 3), Normal = Vector3.right,   Up = Vector3.up },
        new Exit { Label = "Door, upright",        Position = new Vector3(Half, 1.85f, 2), Normal = Vector3.left, Up = Vector3.up, Door = true },
        new Exit { Label = "Door, rolled 90",      Position = new Vector3(Half, 3f, 2),  Normal = Vector3.left,   Up = Vector3.forward, Door = true },
        new Exit { Label = "Door, upside down",    Position = new Vector3(Half, 1.85f, 2), Normal = Vector3.left, Up = Vector3.down, Door = true },
        new Exit { Label = "45 degree slope",      Position = new Vector3(4.2f, 2.5f, 2), Normal = SlopeNormal,  Up = SlopeUphill, Slope = true },
        new Exit { Label = "Floor, turned 90",     Position = new Vector3(3, 0, 2),     Normal = Vector3.up,      Up = Vector3.right },
    };

    static PortalTestLabBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build Portal Test Lab")]
    public static void Build() =>
        GravityArenaBuilder.BuildScene(ScenePath, Layout, new Vector3(-6f, 6f, 0f), new Vector3(30f, 3f, 0f));

    // ------------------------------------------------------------------ layout

    static void Layout()
    {
        var grid = GravityArenaBuilder.GridTexture();
        floorMat = GravityArenaBuilder.LitMaterial("Lab Floor", new Color(0.42f, 0.42f, 0.45f), grid, new Vector2(6, 6));
        ceilingMat = GravityArenaBuilder.LitMaterial("Lab Ceiling", new Color(0.3f, 0.33f, 0.42f), grid, new Vector2(6, 6));
        backMat = GravityArenaBuilder.LitMaterial("Lab Back", new Color(0.5f, 0.38f, 0.3f), grid, new Vector2(6, 6));
        frontMat = GravityArenaBuilder.LitMaterial("Lab Front", new Color(0.3f, 0.45f, 0.32f), grid, new Vector2(6, 6));
        leftMat = GravityArenaBuilder.LitMaterial("Lab Left", new Color(0.28f, 0.44f, 0.46f), grid, new Vector2(6, 6));
        rightMat = GravityArenaBuilder.LitMaterial("Lab Right", new Color(0.42f, 0.32f, 0.5f), grid, new Vector2(6, 6));
        panelMat = GravityArenaBuilder.LitMaterial("Lab Panel", new Color(0.16f, 0.17f, 0.2f), null, Vector2.one);
        corridorMat = GravityArenaBuilder.LitMaterial("Lab Corridor", new Color(0.36f, 0.36f, 0.38f), grid, new Vector2(40, 2));
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);

        // Corridor between the two rows of rooms.
        float lastX = FirstX + (PerRow - 1) * Spacing;
        GravityArenaBuilder.Block("Corridor Floor", new Vector3((lastX - 12f) / 2f, -0.25f, 0f), new Vector3(lastX + 24f, 0.5f, 7.5f), corridorMat);
        for (float x = 0f; x <= lastX; x += 28f) Lamp("Corridor Light", new Vector3(x, 8f, 0f), 22f, 2f);

        int number = 1;
        // North row: drop in through a floor portal.
        for (int i = 0; i < Exits.Length; i++) Room(number++, i, Entry.FloorPortal, Exits[i]);
        InfiniteFallRoom(number++, PerRow - 1);
        // South row: walk in through a door.
        for (int i = 0; i < Exits.Length; i++) Room(number++, PerRow + i, Entry.Door, Exits[i]);
        SideBySideRoom(number++, PerRow * 2 - 1);

        for (int i = 0; i < 3; i++)
            GravityArenaBuilder.AddSpawnPoint($"Start {i + 1}", new Vector3(-6f, 0.95f, -2f + i * 2f), Quaternion.LookRotation(Vector3.right));
        Label("PORTAL TEST LAB\nWalk along the corridor. H = back here.", new Vector3(-9f, 4f, 0f), Quaternion.LookRotation(Vector3.left), 0.12f, Color.white);

        new GameObject("Test Map Rules").AddComponent<TestMap>().portalViewDistance = 18f;
    }

    // ------------------------------------------------------------------ rooms

    /// <summary>Room slot: 0..11 north row (doorway faces the corridor to the south), 12..23 south row.</summary>
    static (Vector3 center, Quaternion rot) Slot(int slot)
    {
        bool south = slot >= PerRow;
        float x = FirstX + (slot % PerRow) * Spacing;
        return south
            ? (new Vector3(x, 0f, -10f), Quaternion.Euler(0f, 180f, 0f))
            : (new Vector3(x, 0f, 10f), Quaternion.identity);
    }

    static Transform Shell(int number, int slot, string title)
    {
        var (center, rot) = Slot(slot);
        var root = new GameObject($"Room {number}: {title}").transform;
        root.SetParent(GravityArenaBuilder.GeometryRoot, false);

        void Part(string n, Vector3 c, Vector3 s, Material m) =>
            GravityArenaBuilder.Block(n, center + rot * c, s, m, rot).transform.SetParent(root, true);

        float o = Half + Thick / 2f, span = Half * 2f + Thick * 2f;
        Part("Floor", new Vector3(0, -Thick / 2f, 0), new Vector3(span, Thick, span), floorMat);
        Part("Ceiling", new Vector3(0, Height + Thick / 2f, 0), new Vector3(span, Thick, span), ceilingMat);
        Part("Back Wall", new Vector3(0, Height / 2f, o), new Vector3(span, Height, Thick), backMat);
        Part("Left Wall", new Vector3(-o, Height / 2f, 0), new Vector3(Thick, Height, Half * 2f), leftMat);
        Part("Right Wall", new Vector3(o, Height / 2f, 0), new Vector3(Thick, Height, Half * 2f), rightMat);
        // Front wall with a 2 m x 3 m doorway onto the corridor.
        float side = (span - 2f) / 2f;
        Part("Front Wall L", new Vector3(-1f - side / 2f, Height / 2f, -o), new Vector3(side, Height, Thick), frontMat);
        Part("Front Wall R", new Vector3(1f + side / 2f, Height / 2f, -o), new Vector3(side, Height, Thick), frontMat);
        Part("Front Wall Lintel", new Vector3(0, (Height + 3f) / 2f, -o), new Vector3(2f, Height - 3f, Thick), frontMat);

        Lamp($"Room {number} Light", center + rot * new Vector3(0, Height - 1.5f, 0), 15f, 2.2f);
        return root;
    }

    static void Room(int number, int slot, Entry entry, Exit exit)
    {
        string entryName = entry == Entry.FloorPortal ? "Floor portal" : "Door";
        var root = Shell(number, slot, $"{entryName} -> {exit.Label}");
        var (center, rot) = Slot(slot);
        Color color = Color.HSVToRGB((number * 0.13f) % 1f, 0.75f, 1f);

        Portal a = entry == Entry.FloorPortal
            ? MakePortal(center, rot, $"Room {number} entrance", new Vector3(-3, 0, -2), Vector3.up, Vector3.forward, Pad, color)
            : MakePortal(center, rot, $"Room {number} entrance", new Vector3(-Half, 1.85f, -2), Vector3.right, Vector3.up, DoorSize, color);
        if (entry == Entry.Door) a.doorway = true;

        if (exit.Slope)
        {
            // A 45-degree panel leaning against the right wall, with the exit on its face.
            var panelRot = rot * Quaternion.LookRotation(Vector3.forward, exit.Normal);
            GravityArenaBuilder.Block("Slope Panel", center + rot * (exit.Position - exit.Normal * 0.2f), new Vector3(4f, 0.4f, 4f), panelMat, panelRot)
                .transform.SetParent(root, true);
        }
        Portal b = MakePortal(center, rot, $"Room {number} exit", exit.Position + exit.Normal * (exit.Slope ? 0.01f : 0f),
            exit.Normal, exit.Up, exit.Door ? DoorSize : Pad, color);
        if (exit.Door) b.doorway = true;
        GravityArenaBuilder.Link(a, b);

        string expect = Describe(Portal.GravityAfterPass(a, b, Vector3.down), rot);
        Signs(number, center, rot, $"{entryName}  >  {exit.Label}", expect);
    }

    static void InfiniteFallRoom(int number, int slot)
    {
        Shell(number, slot, "Infinite fall");
        var (center, rot) = Slot(slot);
        Color color = Color.HSVToRGB((number * 0.13f) % 1f, 0.75f, 1f);
        var a = MakePortal(center, rot, $"Room {number} floor", new Vector3(0, 0, 1), Vector3.up, Vector3.forward, Pad, color);
        var b = MakePortal(center, rot, $"Room {number} ceiling", new Vector3(0, Height, 1), Vector3.down, Vector3.forward, Pad, color);
        GravityArenaBuilder.Link(a, b);
        Signs(number, center, rot, "Infinite fall (speed test)", "fall forever; steer out sideways, or H");
    }

    static void SideBySideRoom(int number, int slot)
    {
        Shell(number, slot, "Two floor portals side by side");
        var (center, rot) = Slot(slot);
        Color color = Color.HSVToRGB((number * 0.13f) % 1f, 0.75f, 1f);
        var a = MakePortal(center, rot, $"Room {number} floor A", new Vector3(-1.7f, 0, 1), Vector3.up, Vector3.forward, Pad, color);
        var b = MakePortal(center, rot, $"Room {number} floor B", new Vector3(1.7f, 0, 1), Vector3.up, Vector3.forward, Pad, color);
        GravityArenaBuilder.Link(a, b);
        string expect = Describe(Portal.GravityAfterPass(a, b, Vector3.down), rot);
        Signs(number, center, rot, "Floor portal  >  floor portal beside it", expect);
    }

    static readonly Vector2 Pad = new Vector2(2.6f, 2.6f);
    static readonly Vector2 DoorSize = new Vector2(2.4f, 3.6f);

    static Portal MakePortal(Vector3 center, Quaternion rot, string name, Vector3 local, Vector3 normal, Vector3 up, Vector2 size, Color color) =>
        GravityArenaBuilder.Make(name, center + rot * local, rot * normal, rot * up, size, color);

    // ------------------------------------------------------------------ signs

    /// <summary>One sign over the doorway (read from the corridor), one on the back wall inside.</summary>
    static void Signs(int number, Vector3 center, Quaternion rot, string title, string expect)
    {
        string text = $"{number}.  {title}\nexpected: {expect}";
        Label(text, center + rot * new Vector3(0, 4.2f, -Half - Thick - 0.05f), rot, 0.06f, Color.white);
        Label(text, center + rot * new Vector3(0, Height - 2.2f, Half - 0.05f), rot, 0.06f, Color.white);
    }

    /// <summary>Plain-words landing for a gravity direction, in the room's own terms.</summary>
    static string Describe(Vector3 worldGravity, Quaternion rot)
    {
        Vector3 g = Quaternion.Inverse(rot) * worldGravity;
        (Vector3 axis, string name)[] surfaces =
        {
            (Vector3.down, "floor"), (Vector3.up, "ceiling"), (Vector3.forward, "back wall"),
            (Vector3.back, "front wall"), (Vector3.right, "right wall"), (Vector3.left, "left wall"),
        };
        string nearest = "";
        float best = 999f;
        foreach (var (axis, n) in surfaces)
        {
            float a = Vector3.Angle(g, axis);
            if (a < best) { best = a; nearest = n; }
        }
        return best < 0.5f ? $"land on the {nearest}" : $"tilted gravity, {best:0} deg off the {nearest}";
    }

    static void Label(string text, Vector3 position, Quaternion rotation, float size, Color color)
    {
        var go = new GameObject($"Sign: {text.Split('\n')[0]}");
        go.transform.SetParent(GravityArenaBuilder.GeometryRoot, false);
        go.transform.SetPositionAndRotation(position, rotation);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = size;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    static void Lamp(string name, Vector3 pos, float range, float intensity)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.transform.SetParent(GravityArenaBuilder.LightRoot, false);
        l.transform.position = pos;
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
    }
}
