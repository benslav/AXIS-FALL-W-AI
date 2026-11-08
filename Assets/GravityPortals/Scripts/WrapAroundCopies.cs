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

        void Start()
        {
            if (source == null || Application.isBatchMode) return;

            var all = new GameObject("Wrap-around copies (visual only)");
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
