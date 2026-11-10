using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// The Shifting Pavilion: builds a new random pavilion layout every match. The lobby host
    /// rolls a seed (<see cref="LobbyState.MapSeed"/>, re-rolled each time everyone goes back to
    /// the lobby) and every machine generates the same layout from it when the map loads, so
    /// only one number crosses the network.
    ///
    /// Someone who joins mid-match loads the scene before the lobby state reaches them; the
    /// layout is built as soon as it does. Opening this scene directly (its own NetworkManager,
    /// no lobby) uses <see cref="fallbackSeed"/>, so a direct host and client still match.
    /// </summary>
    public class ShiftingPavilion : MonoBehaviour
    {
        [Tooltip("One material per PavilionMat, in order: Tatami, Wood, Lacquer, Paper, Lantern, Stone.")]
        public Material[] materials;
        public Material portalMaterial;
        public Material portalViewMaterial;
        [Tooltip("Fills the neighbouring cells with look-only copies of the generated level.")]
        public WrapAroundCopies copies;
        [Tooltip("Layout used when the scene is opened directly, without a lobby.")]
        public int fallbackSeed = 1;

        bool built;

        /// <summary>The seed of the layout that was built, or -1 before it's built.</summary>
        public int Seed { get; private set; } = -1;

        void Awake() => TryBuild();

        void Update()
        {
            if (!built) TryBuild();
        }

        void TryBuild()
        {
            int seed;
            var lobby = LobbyState.Instance;
            if (lobby != null && lobby.IsSpawned) seed = lobby.MapSeed;
            else if (SessionFlow.Instance != null && SessionFlow.Instance.InSession) return; // lobby seed still on its way
            else seed = fallbackSeed;

            built = true;
            Seed = seed;
            var layout = PavilionLayout.Generate(seed);
            var result = PavilionBuilder.Build(layout, transform, materials, portalMaterial, portalViewMaterial);
            Debug.Log($"[ShiftingPavilion] Layout {seed}: {layout.Rooms.Count} rooms, {layout.Portals.Count - 6} doors.");

            if (copies != null && !Application.isBatchMode)
            {
                copies.source = result.Root;
                copies.mesh = PavilionBuilder.LatticeMesh(layout, materials, out var used);
                copies.materials = used;
                copies.Build();
            }
        }
    }
}
