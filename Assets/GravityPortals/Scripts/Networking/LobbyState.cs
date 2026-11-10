using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GravityPortals
{
    public struct LobbyMember : INetworkSerializable, IEquatable<LobbyMember>
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public int Vote; // index into MapList.All, or -1

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref Name);
            s.SerializeValue(ref Vote);
        }

        public bool Equals(LobbyMember o) => ClientId == o.ClientId && Name.Equals(o.Name) && Vote == o.Vote;
    }

    /// <summary>
    /// The lobby everyone shares: who's in it and which map each of them voted for. Spawned by
    /// the host and kept across map changes, so names are available in-game too.
    /// </summary>
    public class LobbyState : NetworkBehaviour
    {
        public static LobbyState Instance { get; private set; }

        NetworkList<LobbyMember> members;
        public NetworkList<LobbyMember> Members => members;

        // Seed for maps that generate a new layout each match (the Shifting Pavilion). Rolled
        // while everyone is in the lobby, so it has reached every player long before a map loads.
        readonly NetworkVariable<int> mapSeed = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public int MapSeed => mapSeed.Value;

        /// <summary>Server: pick a new seed for the next match.</summary>
        public void RollMapSeed()
        {
            if (IsServer) mapSeed.Value = UnityEngine.Random.Range(1, int.MaxValue);
        }

        void Awake() => members = new NetworkList<LobbyMember>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            RollMapSeed();
            foreach (var id in NetworkManager.ConnectedClientsIds) AddMember(id);
            NetworkManager.OnClientConnectedCallback += AddMember;
            NetworkManager.OnClientDisconnectCallback += RemoveMember;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (NetworkManager == null) return;
            NetworkManager.OnClientConnectedCallback -= AddMember;
            NetworkManager.OnClientDisconnectCallback -= RemoveMember;
        }

        void AddMember(ulong clientId)
        {
            for (int i = 0; i < members.Count; i++) if (members[i].ClientId == clientId) return;
            members.Add(new LobbyMember { ClientId = clientId, Name = ConnectionApproval.NameOf(clientId), Vote = -1 });
        }

        void RemoveMember(ulong clientId)
        {
            for (int i = members.Count - 1; i >= 0; i--)
                if (members[i].ClientId == clientId) members.RemoveAt(i);
            ConnectionApproval.Forget(clientId);
        }

        /// <summary>Anyone: vote for a map (index into MapList.All). Voting again changes your vote.</summary>
        public void Vote(int mapIndex) => VoteRpc(mapIndex);

        [Rpc(SendTo.Server)]
        void VoteRpc(int mapIndex, RpcParams rpcParams = default)
        {
            if (mapIndex < -1 || mapIndex >= MapList.All.Count) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].ClientId != sender) continue;
                var m = members[i];
                m.Vote = mapIndex;
                members[i] = m;
            }
        }

        public int VotesFor(int mapIndex)
        {
            int n = 0;
            for (int i = 0; i < members.Count; i++) if (members[i].Vote == mapIndex) n++;
            return n;
        }

        public int VoteOf(ulong clientId)
        {
            for (int i = 0; i < members.Count; i++) if (members[i].ClientId == clientId) return members[i].Vote;
            return -1;
        }

        /// <summary>The winning map: most votes; ties go to the host's pick if it's among them,
        /// otherwise a random one of the tied maps. No votes at all: a random map.</summary>
        public int PickWinner()
        {
            int best = -1;
            var tied = new List<int>();
            for (int map = 0; map < MapList.All.Count; map++)
            {
                int v = VotesFor(map);
                if (v == 0) continue;
                if (v > best) { best = v; tied.Clear(); }
                if (v == best) tied.Add(map);
            }
            if (tied.Count == 0) return UnityEngine.Random.Range(0, MapList.All.Count);
            int hostVote = VoteOf(NetworkManager.ServerClientId);
            return tied.Contains(hostVote) ? hostVote : tied[UnityEngine.Random.Range(0, tied.Count)];
        }

        /// <summary>Server: clear everyone's vote (after a match starts).</summary>
        public void ClearVotes()
        {
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                m.Vote = -1;
                members[i] = m;
            }
        }

        /// <summary>Anyone: a player's display name (falls back to "Player N").</summary>
        public static string NameOf(ulong clientId)
        {
            var lobby = Instance;
            if (lobby != null)
                for (int i = 0; i < lobby.members.Count; i++)
                    if (lobby.members[i].ClientId == clientId) return lobby.members[i].Name.ToString();
            return $"Player {clientId + 1}";
        }
    }
}
