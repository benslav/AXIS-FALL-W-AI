using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>Marks a place players can (re)spawn. Its up axis is the spawned player's up.</summary>
    public class SpawnPoint : MonoBehaviour
    {
        static readonly List<SpawnPoint> all = new List<SpawnPoint>();

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>The spawn point farthest from every position in <paramref name="avoid"/>.</summary>
        public static SpawnPoint PickFarthestFrom(IReadOnlyList<Vector3> avoid)
        {
            SpawnPoint best = null;
            float bestScore = float.MinValue;
            foreach (var sp in all)
            {
                float nearest = 1000f;
                foreach (var p in avoid) nearest = Mathf.Min(nearest, Vector3.Distance(p, sp.transform.position));
                float score = nearest + Random.value * 2f; // a little randomness between similar choices
                if (score > bestScore) { bestScore = score; best = sp; }
            }
            return best;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward);
        }
    }
}
