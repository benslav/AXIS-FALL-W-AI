using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>
    /// Turns a <see cref="PavilionLayout"/> into GameObjects: geometry, portals, lights, spawn
    /// points and crate spots. Works in the editor (the Endless Pavilion is baked into its scene)
    /// and at runtime (the Shifting Pavilion builds a new layout every match).
    /// </summary>
    public static class PavilionBuilder
    {
        public struct Result
        {
            public Transform Root;
            public List<GameObject> Boxes; // visible level geometry (for static flags in the editor)
        }

        /// <param name="materials">One material per <see cref="PavilionMat"/>, in enum order.</param>
        public static Result Build(PavilionLayout layout, Transform parent, Material[] materials,
            Material portalMaterial, Material portalViewMaterial)
        {
            var root = new GameObject("Pavilion Geometry").transform;
            root.SetParent(parent, false);
            var portalRoot = new GameObject("Portals").transform;
            portalRoot.SetParent(parent, false);
            var lightRoot = new GameObject("Lights").transform;
            lightRoot.SetParent(parent, false);
            var spawnRoot = new GameObject("Spawn Points").transform;
            spawnRoot.SetParent(parent, false);
            var propRoot = new GameObject("Crate Spots").transform;
            propRoot.SetParent(parent, false);

            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var boxes = new List<GameObject>();
            foreach (var b in layout.Boxes)
            {
                var go = new GameObject(b.Name);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(b.Center, b.Rotation);
                go.transform.localScale = b.Size;
                if (b.Visible)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = cube;
                    go.AddComponent<MeshRenderer>().sharedMaterial = materials[(int)b.Material];
                    boxes.Add(go);
                }
                if (b.Collide) go.AddComponent<BoxCollider>();
                if (b.StairRamp) go.AddComponent<StairSteps>().origin = b.StairOrigin;
            }

            // Portals: created inactive so their id and settings are in place before they wake up.
            var portals = new Portal[layout.Portals.Count];
            for (int i = 0; i < layout.Portals.Count; i++)
            {
                var spec = layout.Portals[i];
                var go = new GameObject(spec.Name);
                go.SetActive(false);
                go.transform.SetParent(portalRoot, false);
                go.transform.SetPositionAndRotation(spec.Position, PavilionLayout.Basis(spec.Normal, spec.Up));
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                var p = go.AddComponent<Portal>();
                p.portalId = i;
                p.size = spec.Size;
                p.color = spec.Color;
                p.doorway = spec.Doorway;
                p.worldWrap = spec.WorldWrap;
                p.visualMaterial = portalMaterial;
                p.viewMaterial = portalViewMaterial;
                box.size = new Vector3(spec.Size.x, spec.Size.y, p.triggerFront + p.triggerBack);
                box.center = new Vector3(0f, 0f, (p.triggerFront - p.triggerBack) * 0.5f);
                portals[i] = p;
            }
            for (int i = 0; i < portals.Length; i++)
            {
                int link = layout.Portals[i].Link;
                if (link >= 0) portals[i].linkedPortal = portals[link];
            }
            foreach (var p in portals) p.gameObject.SetActive(true);

            foreach (var l in layout.Lights)
            {
                var light = new GameObject("Lantern Light").AddComponent<Light>();
                light.transform.SetParent(lightRoot, false);
                light.transform.position = l.Position;
                light.type = LightType.Point;
                light.range = l.Range;
                light.intensity = l.Intensity;
                light.color = l.Color;
                light.shadows = LightShadows.None;
            }

            for (int i = 0; i < layout.Spawns.Count; i++)
            {
                var go = new GameObject($"Spawn {i + 1}");
                go.transform.SetParent(spawnRoot, false);
                go.transform.SetPositionAndRotation(layout.Spawns[i].Position, layout.Spawns[i].Rotation);
                go.AddComponent<SpawnPoint>();
            }
            for (int i = 0; i < layout.Props.Count; i++)
            {
                var go = new GameObject($"Crate Spot {i + 1}");
                go.transform.SetParent(propRoot, false);
                go.transform.SetPositionAndRotation(layout.Props[i].Position, layout.Props[i].Rotation);
                go.AddComponent<PropSpawnPoint>();
            }

            return new Result { Root = root, Boxes = boxes };
        }

        /// <summary>The whole level as one mesh, one submesh per material in use, for the
        /// look-only copies that fill the neighbouring cells. Built straight from the layout's
        /// boxes, so it works at runtime too.</summary>
        public static Mesh LatticeMesh(PavilionLayout layout, Material[] materials, out Material[] used)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var perMaterial = new Dictionary<int, List<int>>();
            foreach (var b in layout.Boxes)
            {
                if (!b.Visible) continue;
                int m = (int)b.Material;
                if (!perMaterial.TryGetValue(m, out var tris)) perMaterial[m] = tris = new List<int>();
                AddBox(b, vertices, normals, uvs, tris);
            }

            var mesh = new Mesh { name = $"Pavilion Lattice ({layout.Seed})", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            var keys = new List<int>(perMaterial.Keys);
            keys.Sort();
            mesh.subMeshCount = keys.Count;
            used = new Material[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                mesh.SetTriangles(perMaterial[keys[i]], i);
                used[i] = materials[keys[i]];
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        static readonly Vector3[] FaceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        static void AddBox(PavilionBox b, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            Vector3 half = b.Size * 0.5f;
            foreach (var n in FaceNormals)
            {
                // Two axes across the face; with v = n x u, the order 0-1-2 / 0-2-3 winds
                // clockwise seen from outside (Unity's front face).
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int start = vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    float su = i == 1 || i == 2 ? 1f : -1f, sv = i >= 2 ? 1f : -1f;
                    Vector3 local = Vector3.Scale(n + u * su + v * sv, half);
                    vertices.Add(b.Center + b.Rotation * local);
                    normals.Add(b.Rotation * n);
                    uvs.Add(new Vector2((su + 1f) * 0.5f, (sv + 1f) * 0.5f));
                }
                tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
            }
        }
    }
}
