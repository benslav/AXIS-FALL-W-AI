using System.Collections.Generic;
using GravityPortals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "The Turning Cube": builds the scene from <see cref="CubeLayout"/>. A 3x3x3 cube of 14 m pieces,
/// hollow, walkable all over outside and inside, with one door pair per face between the two and
/// the machinery (core, axles, gearboxes) in the middle.
///
/// Every piece is its own object at the cube centre (a kinematic Rigidbody with a
/// <see cref="CubePiece"/>), so turning that object turns the piece round the centre. Nothing on
/// the pieces is static. Doors, spawns and ring gears are children of their piece.
/// </summary>
[InitializeOnLoad]
public static class TurningCubeBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/TheTurningCube.unity";
    const string VersionKey = "ClaudeTest.TheTurningCube.Version";
    const int Version = 3; // 1: still cube (pieces, doors, machinery), no turns yet; 2: layers turn, gears, warning lamps, sounds; 3: hollow lights pulse, on-screen turn warning
    const string MeshPath = "Assets/GravityPortals/Meshes/TheTurningCubeGeometry.asset";

    static readonly Color Background = new Color(0.035f, 0.045f, 0.08f);

    static TurningCubeBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build The Turning Cube")]
    public static void Build()
    {
        var config = new CubeConfig();
        var map = CubeLayout.Generate(config);
        GravityArenaBuilder.CameraBackground = Background;
        float h = config.Half;
        GravityArenaBuilder.BuildScene(ScenePath, () => Layout(config, map), new Vector3(h * 2.2f, h * 1.6f, -h * 2.6f), Vector3.zero);
    }

    static Material[] Materials()
    {
        var grid = GravityArenaBuilder.GridTexture();
        var m = new Material[CubeLayout.MatCount];
        m[CubeLayout.MatBody] = GravityArenaBuilder.LitMaterial("Cube Body", new Color(0.07f, 0.07f, 0.08f), grid, Vector2.one * 0.5f);
        for (int f = 0; f < 6; f++)
            m[CubeLayout.MatSticker + f] = GravityArenaBuilder.LitMaterial($"Cube Sticker {CubeLayout.FaceNames[f]}", CubeLayout.StickerColors[f], grid, Vector2.one * 0.5f);
        m[CubeLayout.MatInner] = GravityArenaBuilder.LitMaterial("Cube Inner Wall", new Color(0.32f, 0.33f, 0.36f), grid, Vector2.one * 0.5f);
        m[CubeLayout.MatMachine] = GravityArenaBuilder.LitMaterial("Cube Machine Steel", new Color(0.42f, 0.44f, 0.48f), null, Vector2.one);
        m[CubeLayout.MatBrass] = GravityArenaBuilder.LitMaterial("Cube Brass", new Color(0.78f, 0.6f, 0.25f), null, Vector2.one);
        m[CubeLayout.MatGlow] = GravityArenaBuilder.EmissiveMaterial("Cube Warning Glow", new Color(1f, 0.55f, 0.1f) * 3f);
        foreach (var mat in m) if (mat != null) mat.SetFloat("_Smoothness", 0.45f);
        return m;
    }

    static void Layout(CubeConfig config, CubeMap map)
    {
        var materials = Materials();
        var meshes = new List<Mesh>();

        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.004f;
        RenderSettings.fogColor = Background;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.4f);

        // Two suns from different sides, so every face of the cube is lit (players stand on all six).
        var lights = GravityArenaBuilder.LightRoot;
        AddSun(lights, "Sun", new Vector3(0.5f, -0.7f, 0.5f), 0.9f, new Color(1f, 0.96f, 0.9f));
        AddSun(lights, "Fill", new Vector3(-0.6f, 0.5f, -0.6f), 0.45f, new Color(0.75f, 0.85f, 1f));

        // The pieces: each its own moving object.
        var piecesRoot = new GameObject("Cube Pieces").transform;
        piecesRoot.SetParent(GravityArenaBuilder.GeometryRoot, false);
        var pieces = new Transform[27];
        for (int i = 0; i < 27; i++)
        {
            if (i == CubeSchedule.Core) continue;
            var h = CubeSchedule.Home(i);
            var go = new GameObject($"Piece {h.x},{h.y},{h.z}");
            go.transform.SetParent(piecesRoot, false);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            go.AddComponent<CubePiece>().index = i;
            Geometry(map.Pieces[i], go.transform, materials, meshes, go.name);
            pieces[i] = go.transform;
        }

        // The machinery that never moves.
        var machinery = new GameObject("Machinery").transform;
        machinery.SetParent(GravityArenaBuilder.GeometryRoot, false);
        var fixedParts = Geometry(map.Fixed, machinery, materials, meshes, "Machinery");
        foreach (var go in fixedParts) GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);

        // Gears: ring gears ride on their centre piece; the rest spin in place while their face turns.
        foreach (var g in map.Gears)
        {
            var mesh = GearMesh(g);
            meshes.Add(mesh);
            var go = new GameObject(g.Name);
            go.transform.SetParent(g.Piece >= 0 ? pieces[g.Piece] : machinery, false);
            go.transform.SetPositionAndRotation(g.Centre, Quaternion.LookRotation(g.Axis, Perpendicular(g.Axis)));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = materials[CubeLayout.MatBrass];
            if (g.Ratio != 0f)
            {
                var spin = go.AddComponent<CubeGearSpin>();
                spin.face = g.Face;
                spin.ratio = g.Ratio;
            }
        }

        // Doors: children of their centre piece (they spin with it). Created inactive so the id and
        // link are in place before they wake up.
        var portals = new Portal[map.Doors.Count];
        for (int i = 0; i < map.Doors.Count; i++)
        {
            var d = map.Doors[i];
            var spec = d.Portal;
            var go = new GameObject(spec.Name);
            go.SetActive(false);
            go.transform.SetParent(pieces[d.Piece], false);
            go.transform.SetPositionAndRotation(spec.Position, PavilionLayout.Basis(spec.Normal, spec.Up));
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var p = go.AddComponent<Portal>();
            p.portalId = i;
            p.size = spec.Size;
            p.color = spec.Color;
            p.doorway = spec.Doorway;
            p.visualMaterial = GravityArenaBuilder.PortalMaterial;
            p.viewMaterial = GravityArenaBuilder.PortalViewMaterialAsset;
            box.size = new Vector3(spec.Size.x, spec.Size.y, p.triggerFront + p.triggerBack);
            box.center = new Vector3(0f, 0f, (p.triggerFront - p.triggerBack) * 0.5f);
            portals[i] = p;
        }
        for (int i = 0; i < portals.Length; i++) portals[i].linkedPortal = portals[map.Doors[i].Portal.Link];
        foreach (var p in portals) p.gameObject.SetActive(true);

        // Spawns ride on their centre piece too (it spins in place, so they stay on their face).
        for (int i = 0; i < map.Spawns.Count; i++)
        {
            var s = map.Spawns[i];
            var go = new GameObject($"Spawn {i + 1} ({(s.Inside ? "inside" : "outside")})");
            go.transform.SetParent(pieces[s.Piece], false);
            go.transform.SetPositionAndRotation(s.Marker.Position, s.Marker.Rotation);
            go.AddComponent<SpawnPoint>();
        }

        // Lights in the hollow, one per face (between the core and the wall), in face order.
        var faceLights = new List<Light>();
        foreach (var l in map.Lights)
        {
            var light = new GameObject("Hollow Light").AddComponent<Light>();
            faceLights.Add(light);
            light.transform.SetParent(lights, false);
            light.transform.position = l.Position;
            light.type = LightType.Point;
            light.range = l.Range;
            light.intensity = l.Intensity;
            light.color = l.Color;
            light.shadows = LightShadows.None;
        }

        GravityArenaBuilder.EnsureFolder("Assets/GravityPortals", "Meshes");
        AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(meshes[0], MeshPath);
        for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], MeshPath);
        AssetDatabase.SaveAssets();

        // Gravity: into the cube outside, along the walls inside.
        var gravity = new GameObject("Cube Gravity").AddComponent<SpinGravity>();
        gravity.axisPoint = Vector3.zero;
        gravity.cube = CubeLayout.Field(config);

        // The turns: a pure function of the seed and the shared clock, run on every machine.
        var turns = new GameObject("Cube Turns").AddComponent<CubeTurns>();
        turns.schedule = CubeLayout.Schedule(config, 1);
        turns.fallbackSeed = 1;
        turns.warningMaterial = materials[CubeLayout.MatGlow];
        turns.outerHalf = config.Half;
        turns.faceLights = faceLights.ToArray();
        // "Hold on" / "stand clear" on screen before and during a turn.
        turns.gameObject.AddComponent<CubeTurnHud>();

        var options = new GameObject("Map Options").AddComponent<MapOptions>();
        // Every door keeps your gravity relative to you, so the "gravity changes" warning would only mislead.
        options.gravityWarnings = false;
        // Same easing as The Tesseract's walk-anywhere rooms.
        options.gravityTurnRate = 12f;
        options.gravityTurnMinSpeed = 300f;
        options.curveViewEase = 16f;

        Debug.Log("[Turning Cube] " + CubeLayout.Report(map));
    }

    static void AddSun(Transform parent, string name, Vector3 direction, float intensity, Color color)
    {
        var sun = new GameObject(name).AddComponent<Light>();
        sun.transform.SetParent(parent, false);
        sun.type = LightType.Directional;
        sun.intensity = intensity;
        sun.color = color;
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.LookRotation(direction);
    }

    static Vector3 Perpendicular(Vector3 axis) => Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;

    // ------------------------------------------------------------------ geometry

    /// <summary>Builds one layout's geometry under <paramref name="parent"/>: visible boxes merged
    /// into one mesh per material, curved surfaces as their own meshes (with mesh colliders), and
    /// box colliders. Returns the objects made.</summary>
    static List<GameObject> Geometry(PavilionLayout layout, Transform parent, Material[] materials, List<Mesh> meshes, string label)
    {
        var made = new List<GameObject>();
        var groups = new Dictionary<int, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)>();
        foreach (var b in layout.Boxes)
        {
            if (!b.Visible) continue;
            int key = (int)b.Material;
            if (!groups.TryGetValue(key, out var g))
                groups[key] = g = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
            AddBox(b, g.v, g.n, g.uv, g.t);
        }
        foreach (var kv in groups)
        {
            var mesh = new Mesh { name = $"{label} mat {kv.Key}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(kv.Value.v);
            mesh.SetNormals(kv.Value.n);
            mesh.SetUVs(0, kv.Value.uv);
            mesh.SetTriangles(kv.Value.t, 0);
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = materials[kv.Key];
            made.Add(go);
        }
        foreach (var s in layout.Surfaces)
        {
            var mesh = new Mesh { name = s.Name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(s.Vertices);
            mesh.SetNormals(s.Normals);
            if (s.UVs.Count == s.Vertices.Count) mesh.SetUVs(0, s.UVs);
            mesh.SetTriangles(s.Triangles, 0);
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var go = new GameObject(s.Name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = materials[(int)s.Material];
            if (s.Collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            made.Add(go);
        }
        var colliders = new GameObject($"{label} colliders").transform;
        colliders.SetParent(parent, false);
        made.Add(colliders.gameObject);
        foreach (var b in layout.Boxes)
        {
            if (!b.Collide) continue;
            var go = new GameObject(b.Name);
            go.transform.SetParent(colliders, false);
            go.transform.SetPositionAndRotation(b.Center, b.Rotation);
            go.transform.localScale = b.Size;
            go.AddComponent<BoxCollider>();
            made.Add(go);
        }
        return made;
    }

    static readonly Vector3[] FaceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

    /// <summary>A box's six faces, with UVs in metres (so the grid texture keeps the same size everywhere).</summary>
    static void AddBox(PavilionBox b, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
    {
        Vector3 half = b.Size * 0.5f;
        foreach (var n in FaceNormals)
        {
            Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 v = Vector3.Cross(n, u);
            int start = vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                float su = i == 1 || i == 2 ? 1f : -1f, sv = i >= 2 ? 1f : -1f;
                Vector3 local = Vector3.Scale(n + u * su + v * sv, half);
                Vector3 p = b.Center + b.Rotation * local;
                vertices.Add(p);
                normals.Add(b.Rotation * n);
                uvs.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
            }
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
        }
    }

    // ------------------------------------------------------------------ gears

    /// <summary>A gear's mesh in its own space (axis = +Z, centred): teeth round the rim (or
    /// inside it, for a ring gear), flat shaded. A bevel gear narrows toward -Z.</summary>
    static Mesh GearMesh(CubeGear g)
    {
        var m = new MeshParts();
        float module = 2f * g.Radius / g.Teeth;
        float tip = g.Ring ? g.Radius - module : g.Radius + module;
        float root = g.Ring ? g.Radius + 1.25f * module : g.Radius - 1.25f * module;
        float rim = g.Radius + 1.25f * module + 0.25f; // ring gears: the outer edge
        float z0 = -g.Thickness * 0.5f, z1 = g.Thickness * 0.5f;
        float backScale = g.Bevel ? 0.6f : 1f;

        // Tooth profile: four points per tooth, root - tip - tip - root.
        var profile = new List<(float angle, float r)>();
        float step = Mathf.PI * 2f / g.Teeth;
        for (int k = 0; k < g.Teeth; k++)
        {
            float a = k * step;
            profile.Add((a, root));
            profile.Add((a + step * 0.2f, tip));
            profile.Add((a + step * 0.45f, tip));
            profile.Add((a + step * 0.65f, root));
        }
        int count = profile.Count;
        Vector3 P(int i, float z, float scale)
        {
            var (a, r) = profile[(i % count + count) % count];
            return new Vector3(Mathf.Cos(a) * r * scale, Mathf.Sin(a) * r * scale, z);
        }
        Vector3 Rim(int i, float z)
        {
            float a = profile[(i % count + count) % count].angle;
            return new Vector3(Mathf.Cos(a) * rim, Mathf.Sin(a) * rim, z);
        }

        for (int i = 0; i < count; i++)
        {
            // The toothed edge (faces away from the gear's solid part).
            Vector3 a0 = P(i, z0, backScale), a1 = P(i + 1, z0, backScale), b0 = P(i, z1, 1f), b1 = P(i + 1, z1, 1f);
            Vector3 mid = (a0 + a1 + b0 + b1) * 0.25f;
            Vector3 outward = new Vector3(mid.x, mid.y, 0f);
            m.Quad(a0, a1, b1, b0, g.Ring ? -outward : outward);
            if (g.Ring)
            {
                // Faces: annulus between the teeth and the rim; and the rim's outside.
                m.Quad(P(i, z1, 1f), P(i + 1, z1, 1f), Rim(i + 1, z1), Rim(i, z1), Vector3.forward);
                m.Quad(P(i, z0, 1f), P(i + 1, z0, 1f), Rim(i + 1, z0), Rim(i, z0), Vector3.back);
                Vector3 r0 = Rim(i, z0), r1 = Rim(i + 1, z0), r2 = Rim(i + 1, z1), r3 = Rim(i, z1);
                m.Quad(r0, r1, r2, r3, new Vector3(r0.x + r1.x, r0.y + r1.y, 0f));
            }
            else
            {
                // Faces: fans from the hub.
                m.Tri(new Vector3(0f, 0f, z1), P(i, z1, 1f), P(i + 1, z1, 1f), Vector3.forward);
                m.Tri(new Vector3(0f, 0f, z0), P(i, z0, backScale), P(i + 1, z0, backScale), Vector3.back);
            }
        }
        return m.ToMesh(g.Name);
    }

    /// <summary>Flat-shaded triangles, each wound to face the given direction.</summary>
    sealed class MeshParts
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<int> t = new List<int>();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(normal, facing) < 0f) { var tmp = b; b = c; c = tmp; normal = -normal; }
            normal.Normalize();
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(normal); n.Add(normal); n.Add(normal);
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            Tri(a, b, c, facing);
            Tri(a, c, d, facing);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
