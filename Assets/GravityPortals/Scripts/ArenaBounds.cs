using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// The playable space of a map with no outer walls. Leaving it (falling into the void)
    /// kills you; the server credits whoever last hit you, so knocking someone off counts.
    /// </summary>
    public class ArenaBounds : MonoBehaviour
    {
        public Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 100f);

        public static ArenaBounds Current { get; private set; }

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        public bool Contains(Vector3 point) => bounds.Contains(point);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
