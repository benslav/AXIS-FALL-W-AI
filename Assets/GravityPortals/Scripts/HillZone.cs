using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// The objective area of a king-of-the-hill map (The Tesseract: the whole Core room). Only a
    /// marker for now: there is no team or hill game mode yet, so nothing scores it. A future mode
    /// can find it through <see cref="Current"/> and ask <see cref="Contains"/>.
    /// </summary>
    public class HillZone : MonoBehaviour
    {
        public Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
        public Color color = new Color(1f, 0.82f, 0.35f);

        public static HillZone Current { get; private set; }

        public bool Contains(Vector3 p) => bounds.Contains(p);

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
