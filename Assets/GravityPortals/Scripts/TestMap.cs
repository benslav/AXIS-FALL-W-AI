using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Marks a testing map. While one is loaded: H takes you back to the start without
    /// counting a death, and portal views only render up close (test labs pack lots of
    /// portals into a small space).
    /// </summary>
    public class TestMap : MonoBehaviour
    {
        [Tooltip("Portals further than this don't render their view (m).")]
        public float portalViewDistance = 18f;

        public static bool Active { get; private set; }
        float previousViewDistance;

        void OnEnable()
        {
            Active = true;
            previousViewDistance = PortalView.maxDistance;
            PortalView.maxDistance = portalViewDistance;
        }

        void OnDisable()
        {
            Active = false;
            PortalView.maxDistance = previousViewDistance;
        }
    }
}
