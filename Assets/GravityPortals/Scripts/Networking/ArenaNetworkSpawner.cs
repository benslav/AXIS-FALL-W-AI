using Unity.Netcode;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>When a server starts, drops a networked crate at every <see cref="PropSpawnPoint"/>.
    /// Crates are server-simulated, so every client sees the same crate go through the same portal.</summary>
    [RequireComponent(typeof(NetworkManager))]
    public class ArenaNetworkSpawner : MonoBehaviour
    {
        public NetworkObject cratePrefab;

        NetworkManager manager;

        void Awake()
        {
            manager = GetComponent<NetworkManager>();
            manager.OnServerStarted += SpawnProps;
        }

        void OnDestroy()
        {
            if (manager != null) manager.OnServerStarted -= SpawnProps;
        }

        void SpawnProps()
        {
            if (cratePrefab == null) return;
            foreach (var point in FindObjectsByType<PropSpawnPoint>(FindObjectsSortMode.None))
            {
                var crate = Instantiate(cratePrefab, point.transform.position, point.transform.rotation);
                // The marker's up is the crate's up, so crates can start on walls or ceilings.
                if (crate.TryGetComponent(out GravityBody body)) body.GravityDirection = -point.transform.up;
                crate.Spawn(true);
            }
        }
    }
}
