using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// For maps whose walls wrap around (each wall is a portal to the opposite one): fills the
    /// neighbouring cells with look-only copies of the level, so looking through any wall
    /// shows the space repeating forever (the fog fades the far copies out).
    ///
    /// Copies are made at load time, so the scene file stays small. They have no colliders
    /// and no gameplay; the wall portals handle actually moving between cells. Skipped on
    /// headless servers.
    /// </summary>
    public class WrapAroundCopies : MonoBehaviour
    {
        [Tooltip("The level geometry to repeat.")]
        public Transform source;
        [Tooltip("Size of one cell (the distance between opposite walls).")]
        public Vector3 period = new Vector3(64f, 64f, 64f);
        [Tooltip("How many cells out in each direction (1 = the 26 neighbours).")]
        public int range = 1;
        [Tooltip("The level merged into one mesh (one submesh per material), baked by the map builder. " +
                 "Each copy is then a single renderer instead of hundreds of parts.")]
        public Mesh mesh;
        public Material[] materials;

        [Tooltip("Off for maps that generate their level at runtime and call Build() themselves.")]
        public bool buildOnStart = true;

        void Start()
        {
            if (buildOnStart) Build();
        }

        /// <summary>Makes the copies (once the level and, if baked, its mesh are in place).</summary>
        public void Build()
        {
            if (source == null || Application.isBatchMode) return;

            var all = new GameObject("Wrap-around copies (visual only)");
            if (mesh != null && materials != null && materials.Length == mesh.subMeshCount)
            {
                for (int x = -range; x <= range; x++)
                    for (int y = -range; y <= range; y++)
                        for (int z = -range; z <= range; z++)
                        {
                            if (x == 0 && y == 0 && z == 0) continue;
                            var copy = new GameObject($"Copy {x},{y},{z}");
                            copy.transform.SetParent(all.transform, false);
                            copy.transform.SetPositionAndRotation(
                                source.position + Vector3.Scale(new Vector3(x, y, z), period), source.rotation);
                            copy.transform.localScale = source.lossyScale;
                            copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var r = copy.AddComponent<MeshRenderer>();
                            r.sharedMaterials = materials;
                            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            r.receiveShadows = false;
                        }
                return;
            }

            // Not baked: copy the level part by part (slow to draw on big maps).
            for (int x = -range; x <= range; x++)
                for (int y = -range; y <= range; y++)
                    for (int z = -range; z <= range; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        var copy = Instantiate(source.gameObject, source.position + Vector3.Scale(new Vector3(x, y, z), period),
                            source.rotation, all.transform);
                        copy.name = $"Copy {x},{y},{z}";
                        StripGameplay(copy);
                    }

            // Merge into big static batches so thousands of copied parts stay cheap to draw.
            StaticBatchingUtility.Combine(all);
        }

        static void StripGameplay(GameObject copy)
        {
            foreach (var c in copy.GetComponentsInChildren<Collider>())
            {
                c.enabled = false;
                Destroy(c);
            }
            foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>())
                Destroy(b); // e.g. stair markers
            foreach (var r in copy.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
