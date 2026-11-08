using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace GravityPortals
{
    /// <summary>
    /// Online play through Unity Relay: the host gets a short join code, friends type it in,
    /// and Unity's relay servers pass the traffic along. No port forwarding or VPN needed,
    /// on any network. Uses the Unity Cloud project this Unity project is linked to.
    /// </summary>
    public static class RelayConnector
    {
        /// <summary>Players besides the host (so up to 8 in a match).</summary>
        public const int MaxConnections = 7;
        const string Protocol = "dtls"; // encrypted UDP, right for desktop builds

        static Task signIn;

        /// <summary>Starts a host on Relay and returns the join code to share.</summary>
        public static async Task<string> HostAsync(NetworkManager manager)
        {
            await EnsureSignedIn();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            manager.GetComponent<UnityTransport>().SetRelayServerData(allocation.ToRelayServerData(Protocol));
            if (!manager.StartHost()) throw new Exception("Could not start hosting.");
            return code;
        }

        /// <summary>Joins a Relay host by its join code.</summary>
        public static async Task JoinAsync(NetworkManager manager, string joinCode)
        {
            await EnsureSignedIn();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim().ToUpperInvariant());

            manager.GetComponent<UnityTransport>().SetRelayServerData(allocation.ToRelayServerData(Protocol));
            if (!manager.StartClient()) throw new Exception("Could not start the client.");
        }

        static Task EnsureSignedIn() => signIn ??= SignIn();

        static async Task SignIn()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    // A separate anonymous identity for each running copy of the game, so two
                    // copies on the same PC (Multiplayer Play Mode, or two builds) can both use Relay.
                    var options = new InitializationOptions().SetProfile($"p{UnityEngine.Random.Range(0, int.MaxValue)}");
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            catch
            {
                signIn = null; // let the next attempt retry from scratch
                throw;
            }
        }

        /// <summary>Turns service errors into something a player can act on.</summary>
        public static string Explain(Exception e)
        {
            string m = e.Message ?? e.GetType().Name;
            if (m.IndexOf("join code", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                return "That join code didn't work. Check it, or ask the host for a new one.";
            if (m.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Relay isn't set up for this game's Unity Cloud project (enable Relay in the Unity Cloud dashboard).";
            if (m.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Couldn't reach Unity's servers. Check your internet connection.";
            return m;
        }
    }
}
