using System.IO;
using GravityPortals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "Tri-Gravity Atrium": a map inspired by the idea behind M. C. Escher's Relativity:
/// three gravity directions at right angles share one stone hall.
///
///   Family Y stands on the floor      (gravity -Y)
///   Family X stands on the west wall  (gravity -X)
///   Family Z stands on the south wall (gravity -Z)
///
/// The layout is designed once for family Y and copied twice by rotating 120 degrees
/// around the cube's diagonal (x,y,z) -> (y,z,x), so every family gets the same
/// ground, landing, staircase and doors, interlocking with the others.
///
/// Staircases look stepped but are physically 45-degree ramps. A 45-degree slope is
/// walkable for two families at once: the treads are steps for one, the risers for the
/// other.
///
/// Portals chain the families in a ring (Y -> X -> Z -> Y):
///   ground doorway -> the next family's landing
///   landing doorway -> the next family's face of the central cube
///   cube-top floor portal and ground floor portal -> drop out of the vault onto the next family's ground
/// </summary>
[InitializeOnLoad]
public static class TriGravityAtriumBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/TriGravityAtrium.unity";
    const string LargeScenePath = "Assets/GravityPortals/Scenes/TriGravityAtriumLarge.unity";
    const string VersionKey = "ClaudeTest.TriGravityAtrium.Version";
    const string LargeVersionKey = "ClaudeTest.TriGravityAtriumLarge.Version";
    const int Version = 5; // 2 upright doors, 3 vault arrivals, 4 ground floor portals, 5 stair steps
    const int LargeVersion = 1;

    const float Wall = 1f;

    // Current build's dimensions (set by Build / BuildLarge). The hall is the cube [0, Size]^3;
    // the central cube sits on the hall's diagonal at (CubeCenter, CubeCenter, CubeCenter).
    static float Size = 32f;
    static float CubeCenter = 20f;
    static float CubeHalf = 3f;

    static Material stone, stoneDark, ink, lamp;
    static Material[] grounds, skies;

    static TriGravityAtriumBuilder()
    {
        // Build on first compile, and rebuild once whenever a layout version goes up.
        bool small = EditorPrefs.GetInt(VersionKey, 0) < Version;
        bool large = EditorPrefs.GetInt(LargeVersionKey, 0) < LargeVersion;
        if (!small && !large) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorPrefs.SetInt(LargeVersionKey, LargeVersion);
        EditorApplication.delayCall += () =>
        {
            if (small) Build();
            if (large) BuildLarge();
        };
    }

    [MenuItem("Tools/Gravity Portals/Build Tri-Gravity Atrium")]
    public static void Build()
    {
        Size = 32f; CubeCenter = 20f; CubeHalf = 3f;
        GravityArenaBuilder.BuildScene(ScenePath, Layout, new Vector3(30f, 14f, 30f), new Vector3(10f, 10f, 10f));
    }

    /// <summary>Bigger hall (44 m) with the central cube in the exact middle of it.</summary>
    [MenuItem("Tools/Gravity Portals/Build Tri-Gravity Atrium (Large)")]
    public static void BuildLarge()
    {
        Size = 44f; CubeCenter = 22f; CubeHalf = 4f;
        GravityArenaBuilder.BuildScene(LargeScenePath, Layout, new Vector3(40f, 20f, 40f), new Vector3(16f, 12f, 16f));
    }

    /// <summary>A point relative to the central cube, in family Y's frame (y = up from its top face).</summary>
    static Vector3 Cube(float x, float y, float z) => new Vector3(CubeCenter + x, CubeCenter + y, CubeCenter + z);

    // ------------------------------------------------------------------ symmetry

    /// <summary>Rotate k times by 120 degrees about (1,1,1): (x,y,z) -> (y,z,x).
    /// Maps +Y -> +X -> +Z -> +Y, i.e. family Y -> X -> Z. Works for points, directions and box sizes.</summary>
    static Vector3 P(Vector3 v, int k)
    {
        for (int i = 0; i < ((k % 3) + 3) % 3; i++) v = new Vector3(v.y, v.z, v.x);
        return v;
    }

    static readonly string[] FamilyNames = { "Y (floor)", "X (west wall)", "Z (south wall)" };

    // ------------------------------------------------------------------ layout

    static void Layout()
    {
        CreateMaterials();
        Lighting();
        Shell();

        // The central cube: one face per family (top for Y, +X face for X, +Z face for Z).
        GravityArenaBuilder.Block("Central Cube", Cube(0f, 0f, 0f), Vector3.one * CubeHalf * 2f, stone);

        var portals = new Portal[3, PortalSlots];
        for (int k = 0; k < 3; k++) FamilyGeometry(k);
        for (int k = 0; k < 3; k++) FamilyPortals(k, portals);
        LinkRing(portals);

        for (int k = 0; k < 3; k++) FamilySpawns(k);
    }

    static void CreateMaterials()
    {
        var grid = GravityArenaBuilder.GridTexture();
        // A pale, warm, lithograph-like palette; each family's ground is tinted so you can
        // always tell which way "down" is for whom.
        stone = GravityArenaBuilder.LitMaterial("Atrium Stone", new Color(0.86f, 0.82f, 0.74f), grid, new Vector2(2, 2));
        stoneDark = GravityArenaBuilder.LitMaterial("Atrium Stone Dark", new Color(0.52f, 0.49f, 0.45f), grid, new Vector2(1, 1));
        ink = GravityArenaBuilder.LitMaterial("Atrium Ink", new Color(0.07f, 0.07f, 0.08f), null, Vector2.one);
        lamp = GravityArenaBuilder.EmissiveMaterial("Atrium Lamp", new Color(1f, 0.85f, 0.6f) * 2.5f);
        grounds = new[]
        {
            GravityArenaBuilder.LitMaterial("Atrium Ground Y", new Color(0.80f, 0.72f, 0.58f), grid, new Vector2(16, 16)),
            GravityArenaBuilder.LitMaterial("Atrium Ground X", new Color(0.62f, 0.72f, 0.60f), grid, new Vector2(16, 16)),
            GravityArenaBuilder.LitMaterial("Atrium Ground Z", new Color(0.62f, 0.66f, 0.78f), grid, new Vector2(16, 16)),
        };
        skies = new[]
        {
            GravityArenaBuilder.LitMaterial("Atrium Vault Y", new Color(0.30f, 0.28f, 0.26f), grid, new Vector2(8, 8)),
            GravityArenaBuilder.LitMaterial("Atrium Vault X", new Color(0.26f, 0.30f, 0.26f), grid, new Vector2(8, 8)),
            GravityArenaBuilder.LitMaterial("Atrium Vault Z", new Color(0.26f, 0.27f, 0.32f), grid, new Vector2(8, 8)),
        };
    }

    static void Lighting()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.46f, 0.44f, 0.41f);

        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.transform.SetParent(GravityArenaBuilder.LightRoot, false);
        sun.type = LightType.Directional;
        sun.intensity = 0.6f;
        sun.shadows = LightShadows.None; // closed room: shadows would black everything out
        sun.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -1.3f, -0.8f));

        // Lamps: one in the middle, two per family (copied by the symmetry).
        AddLamp("Lamp Center", new Vector3(11f, 11f, 11f), 30f, 3.5f);
        if (Size > 36f) AddLamp("Lamp Far", Vector3.one * (Size - 9f), 32f, 3.5f); // light the extra space in the large hall
        for (int k = 0; k < 3; k++)
        {
            AddLamp($"Lamp {k}a", P(new Vector3(16f, 7f, 6f), k), 20f, 3f);
            AddLamp($"Lamp {k}b", P(new Vector3(8f, 27f, 26f), k), 20f, 3f);
        }
    }

    static void AddLamp(string name, Vector3 position, float range, float intensity)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.transform.SetParent(GravityArenaBuilder.LightRoot, false);
        l.transform.position = position;
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = new Color(1f, 0.9f, 0.75f);
        l.shadows = LightShadows.None;
        GravityArenaBuilder.Trim(position, Vector3.one * 0.35f, lamp);
    }

    static void Shell()
    {
        for (int k = 0; k < 3; k++)
        {
            // Ground of family k, and the vault opposite it.
            Box($"Ground {FamilyNames[k]}", k, new Vector3(Size / 2, -Wall / 2, Size / 2), new Vector3(Size + 2 * Wall, Wall, Size + 2 * Wall), grounds[k]);
            Box($"Vault opposite {FamilyNames[k]}", k, new Vector3(Size / 2, Size + Wall / 2, Size / 2), new Vector3(Size + 2 * Wall, Wall, Size + 2 * Wall), skies[k]);
        }

        // Ink lines along all 12 edges of the hall, like a drawn outline.
        const float e = 0.15f;
        for (int k = 0; k < 3; k++)
            foreach (var (a, b) in new[] { (0f, 0f), (Size, 0f), (0f, Size), (Size, Size) })
                GravityArenaBuilder.Trim(P(new Vector3(Size / 2, a == 0 ? e / 2 : Size - e / 2, b == 0 ? e / 2 : Size - e / 2), k),
                    P(new Vector3(Size, e, e), k), ink);
    }

    /// <summary>Everything one family owns, defined for family Y and rotated into place for k.</summary>
    static void FamilyGeometry(int k)
    {
        string f = FamilyNames[k];

        // Landing (top at height 10) on a column, with low parapets as cover.
        Box($"Landing {f}", k, new Vector3(8f, 9.75f, 24f), new Vector3(12f, 0.5f, 12f), stone);
        Box($"Landing Column {f}", k, new Vector3(8f, 4.75f, 24f), new Vector3(1.6f, 9.5f, 1.6f), stoneDark);
        Box($"Parapet A {f}", k, new Vector3(8f, 10.5f, 18.25f), new Vector3(12f, 1f, 0.3f), stoneDark);
        Box($"Parapet B {f}", k, new Vector3(8f, 10.5f, 29.75f), new Vector3(12f, 1f, 0.3f), stoneDark);

        // Staircase from the ground up to the landing. Its risers face +X, so the X family
        // can walk on the same flight sideways-on.
        Staircase($"Stair {f}", k, bottomX: 24f, topX: 14f, z: 22f, width: 4f);

        // Doorway at the far end of the landing (a portal inside an arch).
        Box($"Landing Door Panel {f}", k, new Vector3(2.5f, 12f, 24f), new Vector3(0.5f, 4f, 3f), stoneDark);
        Box($"Landing Door Pillar A {f}", k, new Vector3(2.5f, 12f, 22.2f), new Vector3(0.7f, 4f, 0.7f), stone);
        Box($"Landing Door Pillar B {f}", k, new Vector3(2.5f, 12f, 25.8f), new Vector3(0.7f, 4f, 0.7f), stone);
        Box($"Landing Door Lintel {f}", k, new Vector3(2.5f, 14.35f, 24f), new Vector3(0.9f, 0.7f, 4.6f), stone);

        // Arrival doorway on the landing: where the previous family's ground door leads.
        Box($"Landing Arrival Panel {f}", k, new Vector3(9f, 12f, 28.5f), new Vector3(3f, 4f, 0.5f), stoneDark);
        Box($"Landing Arrival Pillar A {f}", k, new Vector3(7.3f, 12f, 28.5f), new Vector3(0.7f, 4f, 0.7f), stone);
        Box($"Landing Arrival Pillar B {f}", k, new Vector3(10.7f, 12f, 28.5f), new Vector3(0.7f, 4f, 0.7f), stone);
        Box($"Landing Arrival Lintel {f}", k, new Vector3(9f, 14.35f, 28.5f), new Vector3(4.6f, 0.7f, 0.9f), stone);

        // Doorway standing on this family's face of the central cube: where the previous
        // family's landing door leads.
        Box($"Cube Door Panel {f}", k, Cube(0.4f * CubeHalf, CubeHalf + 2f, CubeHalf - 0.25f), new Vector3(3f, 4f, 0.5f), stoneDark);
        Box($"Cube Door Lintel {f}", k, Cube(0.4f * CubeHalf, CubeHalf + 4.3f, CubeHalf - 0.25f), new Vector3(3.4f, 0.6f, 0.5f), stone);

        // Doorway standing on the ground.
        Box($"Ground Door {f}", k, new Vector3(20f, 2f, 4f), new Vector3(6f, 4f, 1f), stoneDark);
        Box($"Ground Door Lintel {f}", k, new Vector3(20f, 4.3f, 4.6f), new Vector3(6.4f, 0.6f, 1.4f), stone);

        // Cover on the ground.
        Box($"Cover A {f}", k, new Vector3(12f, 0.75f, 9f), new Vector3(3f, 1.5f, 1.2f), stoneDark);
        Box($"Cover B {f}", k, new Vector3(28f, 0.75f, 9f), new Vector3(1.2f, 1.5f, 3f), stoneDark);
        Box($"Cover C {f}", k, new Vector3(5f, 0.75f, 12f), new Vector3(1.2f, 1.5f, 3f), stoneDark);
    }

    /// <summary>
    /// A flight climbing from (bottomX, 0) to (topX, 10) in family Y's frame (rising toward -X),
    /// so rise equals run (45 degrees). Visible steps have no colliders; a hidden ramp along the
    /// step noses is what you actually walk on. The top step is solid so it meets the landing flush.
    /// </summary>
    static void Staircase(string name, int k, float bottomX, float topX, float z, float width)
    {
        float run = bottomX - topX;
        const float step = 0.25f; // close to real stair proportions
        int count = Mathf.RoundToInt(run / step);

        var root = new GameObject(name).transform;
        root.SetParent(GravityArenaBuilder.GeometryRoot, false);

        for (int i = 0; i < count; i++)
        {
            // Step i: x from bottomX-(i+1)*step to bottomX-i*step, top at (i+1)*step, hanging 0.4 below.
            float x0 = bottomX - (i + 1) * step, x1 = bottomX - i * step;
            float y1 = (i + 1) * step, y0 = i * step - 0.4f;
            var center = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z);
            var size = new Vector3(step, y1 - y0, width);
            var go = Box($"Step {i + 1}", k, center, size, i % 2 == 0 ? stone : stoneDark);
            go.transform.SetParent(root, true);
            if (i < count - 1) Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        // Hidden ramp through the outer corners of the steps (x + y = bottomX + step).
        Vector3 a = new Vector3(bottomX + step, 0f, z);
        Vector3 b = new Vector3(topX + step, run, z);
        Vector3 normalY = new Vector3(1f, 1f, 0f).normalized;
        const float thickness = 0.3f;
        Vector3 dir = P(b - a, k);
        Vector3 n = P(normalY, k);
        Vector3 mid = P((a + b) * 0.5f, k) - n * thickness * 0.5f;
        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "Walkable Ramp (hidden)";
        ramp.transform.SetParent(root, false);
        ramp.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(dir, n));
        ramp.transform.localScale = new Vector3(width, thickness, dir.magnitude);
        Object.DestroyImmediate(ramp.GetComponent<MeshRenderer>());
        Object.DestroyImmediate(ramp.GetComponent<MeshFilter>());
        GameObjectUtility.SetStaticEditorFlags(ramp, (StaticEditorFlags)~0);

        // Tell the player camera where the treads are, so walking it feels like steps.
        var steps = ramp.AddComponent<StairSteps>();
        steps.origin = P(new Vector3(bottomX, 0f, z), k);
        steps.stepSize = step;
    }

    // Portal slots per family (pairs are 0-1, 2-3, 4-5, 6-7):
    //   0 landing door         -> 1 door on the next family's cube face
    //   2 ground door          -> 3 door on the next family's landing
    //   4 cube floor portal    -> 5 vault portal above the next family's ground
    //   6 ground floor portal  -> 7 second vault portal above the next family's ground
    const int PortalSlots = 8;

    static void FamilyPortals(int k, Portal[,] portals)
    {
        string f = FamilyNames[k];
        var door = new Vector2(2.4f, 3.6f);
        var pad = new Vector2(2.6f, 2.6f);
        var small = new Vector2(2.4f, 2.4f);

        portals[k, 0] = MakePortal($"Landing Door {f}", k, new Vector3(2.76f, 11.9f, 24f), Vector3.right, Vector3.up, door);
        portals[k, 2] = MakePortal($"Ground Door {f}", k, new Vector3(20f, 1.85f, 4.51f), Vector3.forward, Vector3.up, door);
        portals[k, 4] = MakePortal($"Cube Floor Portal {f}", k, Cube(-0.47f * CubeHalf, CubeHalf, 0f), Vector3.up, Vector3.forward, small);

        // Arrivals in family k+1's world. The doors are upright for that family, so walking
        // through family k's door upright brings you out upright as family k+1 (portals keep
        // your orientation when you walk through like a door).
        portals[k, 1] = MakePortal($"Cube Door (from {f})", k + 1, Cube(0.4f * CubeHalf, CubeHalf + 1.9f, CubeHalf - 0.51f), Vector3.back, Vector3.up, door);
        portals[k, 3] = MakePortal($"Landing Arrival Door (from {f})", k + 1, new Vector3(9f, 11.9f, 28.24f), Vector3.back, Vector3.up, door);
        // Dropping into the cube's floor portal brings you out of the vault above the next
        // family's ground, facing down onto it: your fall carries through and you land there.
        // (Placed so you land on open floor, not on that family's own floor portal.)
        portals[k, 5] = MakePortal($"Vault Arrival (from {f} cube)", k + 1, new Vector3(17f, Size, 10f), Vector3.down, Vector3.forward, pad);

        // Floor portal on the ground: drop in and you fall out of the vault above the next
        // family's ground, landing on it.
        portals[k, 6] = MakePortal($"Ground Floor Portal {f}", k, new Vector3(26f, 0f, 26f), Vector3.up, Vector3.forward, pad);
        portals[k, 7] = MakePortal($"Vault Arrival (from {f} ground)", k + 1, new Vector3(14f, Size, 4f), Vector3.down, Vector3.forward, pad);

        // The four door portals are walk-through doorways: clean openings, no frame posts.
        for (int slot = 0; slot < 4; slot++) portals[k, slot].doorway = true;
    }

    static readonly Color[,] PairColors =
    {
        { new Color(0.0f, 0.85f, 1f), new Color(1f, 0.25f, 0.75f), new Color(1f, 0.85f, 0.15f), new Color(1f, 1f, 1f) },
        { new Color(0.25f, 0.5f, 1f), new Color(0.75f, 0.3f, 1f), new Color(1f, 0.55f, 0.1f), new Color(0.55f, 0.45f, 1f) },
        { new Color(0.35f, 1f, 0.85f), new Color(1f, 0.4f, 0.4f), new Color(0.6f, 1f, 0.25f), new Color(1f, 0.7f, 0.85f) },
    };

    static void LinkRing(Portal[,] portals)
    {
        for (int k = 0; k < 3; k++)
            for (int pair = 0; pair < PortalSlots / 2; pair++)
            {
                var a = portals[k, pair * 2];
                var b = portals[k, pair * 2 + 1];
                a.color = b.color = PairColors[k, pair];
                GravityArenaBuilder.Link(a, b);
            }
    }

    static void FamilySpawns(int k)
    {
        Vector3 up = P(Vector3.up, k);
        GravityArenaBuilder.AddSpawnPoint($"Spawn {FamilyNames[k]} A", P(new Vector3(6f, 0.95f, 6f), k),
            Quaternion.LookRotation(P(new Vector3(1f, 0f, 1f).normalized, k), up));
        GravityArenaBuilder.AddSpawnPoint($"Spawn {FamilyNames[k]} B", P(new Vector3(28f, 0.95f, 16f), k),
            Quaternion.LookRotation(P(Vector3.left, k), up));
        GravityArenaBuilder.AddPropSpawn($"Crate {FamilyNames[k]}", P(new Vector3(14f, 0.4f, 14f), k),
            Quaternion.LookRotation(P(Vector3.forward, k), up));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Axis-aligned box defined in family Y's frame, rotated into family k's.</summary>
    static GameObject Box(string name, int k, Vector3 center, Vector3 size, Material mat) =>
        GravityArenaBuilder.Block(name, P(center, k), P(size, k), mat);

    static Portal MakePortal(string name, int k, Vector3 position, Vector3 normal, Vector3 upHint, Vector2 size) =>
        GravityArenaBuilder.Make(name, P(position, k), P(normal, k), P(upHint, k), size, Color.white);
}
