using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Marks a solid collider that shots pass through (like the Möbius Spin hub's glass): players
    /// and bolts still bump into it, but hitscan shots and portal sight checks ignore it.
    /// </summary>
    public class ShootThrough : MonoBehaviour
    {
        public static bool Is(Collider c) => c != null && c.GetComponent<ShootThrough>() != null;
    }
}
