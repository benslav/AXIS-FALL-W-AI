using UnityEngine;

namespace GravityPortals
{
    /// <summary>Marks a place the server drops a networked crate when a match starts.</summary>
    public class PropSpawnPoint : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
        }
    }
}
