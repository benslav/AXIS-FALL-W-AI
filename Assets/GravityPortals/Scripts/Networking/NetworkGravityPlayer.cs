using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>Everything other machines need to draw a player and check their moves.</summary>
    public struct PlayerNetState : INetworkSerializable, IEquatable<PlayerNetState>
    {
        public Vector3 Position;
        public Quaternion Rotation;   // body orientation: its up is the player's up
        public float Pitch;
        public Vector3 Gravity;
        public Vector3 Velocity;
        public byte TeleportCount;    // bumps every portal pass, so remotes snap instead of sliding
        public short FromPortal;      // last portal pair used (for server validation)
        public short ToPortal;
        public byte ResetCount;       // bumps every server respawn/correction
        public ushort Sequence;       // lets the server drop updates that arrive out of order

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Sequence);
            s.SerializeValue(ref Position);
            s.SerializeValue(ref Rotation);
            s.SerializeValue(ref Pitch);
            s.SerializeValue(ref Gravity);
            s.SerializeValue(ref Velocity);
            s.SerializeValue(ref TeleportCount);
            s.SerializeValue(ref FromPortal);
            s.SerializeValue(ref ToPortal);
            s.SerializeValue(ref ResetCount);
        }

        public bool Equals(PlayerNetState o) =>
            Position == o.Position && Rotation == o.Rotation && Pitch == o.Pitch && Gravity == o.Gravity &&
            Velocity == o.Velocity && TeleportCount == o.TeleportCount && FromPortal == o.FromPortal &&
            ToPortal == o.ToPortal && ResetCount == o.ResetCount && Sequence == o.Sequence;

        /// <summary>True if anything another player would notice changed.</summary>
        public bool DiffersNoticeablyFrom(PlayerNetState o) =>
            (Position - o.Position).sqrMagnitude > 0.002f * 0.002f ||
            Quaternion.Angle(Rotation, o.Rotation) > 0.3f ||
            Mathf.Abs(Pitch - o.Pitch) > 0.3f ||
            (Velocity - o.Velocity).sqrMagnitude > 0.05f * 0.05f ||
            Gravity != o.Gravity || TeleportCount != o.TeleportCount || ResetCount != o.ResetCount;

        public override bool Equals(object obj) => obj is PlayerNetState o && Equals(o);
        public override int GetHashCode() => Position.GetHashCode() ^ (TeleportCount << 8) ^ (ResetCount << 16);
    }

    /// <summary>
    /// Networks a <see cref="GravityPlayerController"/>.
    ///
    /// Model: the owning client simulates its own movement (so it feels instant) and
    /// sends its state every network tick. The server checks every update before
    /// anyone else sees it:
    ///   - no moving faster than physically possible,
    ///   - gravity can only change through a real portal pass,
    ///   - a portal pass must start at the entrance and end at its linked exit,
    ///   - respawns are decided by the server.
    /// A rejected update snaps the cheater/glitched client back to its last valid state.
    /// Accepted state goes out to everyone through a server-owned NetworkVariable.
    ///
    /// Remote players are drawn ~100 ms in the past, interpolated between updates,
    /// and snap (no sliding across the map) whenever they pass through a portal.
    /// </summary>
    [RequireComponent(typeof(GravityPlayerController))]
    public class NetworkGravityPlayer : NetworkBehaviour
    {
        [Tooltip("Remote players are drawn this far in the past so motion can be interpolated.")]
        public float interpolationDelay = 0.1f;
        [Tooltip("Fastest the server believes anyone can move (m/s).")]
        public float maxValidatedSpeed = 60f;

        static readonly Color[] Palette =
        {
            new Color(1f, 0.35f, 0.3f), new Color(0.3f, 0.6f, 1f), new Color(0.4f, 1f, 0.45f),
            new Color(1f, 0.85f, 0.25f), new Color(0.85f, 0.4f, 1f), new Color(0.3f, 1f, 0.95f),
        };

        readonly NetworkVariable<PlayerNetState> state = new NetworkVariable<PlayerNetState>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        GravityPlayerController controller;

        // Owner side
        byte teleportCount;
        short lastFrom = -1, lastTo = -1;
        byte resetCount;
        bool waitingForSpawn = true;
        ushort sequence;
        PlayerNetState lastSent;
        float lastSendTime = -10f;
        const float KeepAliveInterval = 0.25f; // resend this often even when standing still

        // Server: newest update seen, for dropping stale ones
        ushort lastSequence;
        bool hasSequence;

        // Server side
        PlayerNetState accepted;
        double acceptedTime;
        double lastCorrectionTime = -10;
        int rejections;
        struct HistoryEntry { public double Time; public PlayerNetState State; }
        readonly List<HistoryEntry> history = new List<HistoryEntry>();
        const double HistoryLength = 1.0;

        // Remote side
        struct Snapshot { public double Time; public PlayerNetState State; }
        readonly List<Snapshot> snapshots = new List<Snapshot>();
        bool snapNext = true;

        public PlayerNetState CurrentState => state.Value;
        public int Rejections => rejections;
        public GravityPlayerController Controller => controller;

        void Awake() => controller = GetComponent<GravityPlayerController>();

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                controller.SetLocalControl(true, Camera.main);
                GravityPlayerController.Local = controller;
                controller.Body.isKinematic = true; // frozen until the server places us
                controller.PortalTravelled += OnLocalPortal;
                NetworkManager.NetworkTickSystem.Tick += OwnerTick;
            }
            else
            {
                controller.SetLocalControl(false, null);
                Tint(Palette[(int)(OwnerClientId % (ulong)Palette.Length)]);
                state.OnValueChanged += OnRemoteState;
                if (!state.Value.Equals(default(PlayerNetState))) OnRemoteState(default, state.Value);
            }

            if (IsServer) ServerRespawn();
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                if (GravityPlayerController.Local == controller) GravityPlayerController.Local = null;
                controller.PortalTravelled -= OnLocalPortal;
                if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
                    NetworkManager.NetworkTickSystem.Tick -= OwnerTick;
            }
            state.OnValueChanged -= OnRemoteState;
        }

        void Tint(Color c)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
        }

        // ------------------------------------------------------------------ owner

        void OnLocalPortal(Portal from, Portal to)
        {
            teleportCount++;
            lastFrom = (short)from.portalId;
            lastTo = (short)to.portalId;
        }

        void OwnerTick()
        {
            if (waitingForSpawn) return; // server hasn't placed us yet
            var body = controller.Body;
            var s = new PlayerNetState
            {
                Position = body.position,
                Rotation = controller.BodyRotation,
                Pitch = controller.Pitch,
                Gravity = controller.GravityDirection,
                Velocity = body.linearVelocity,
                TeleportCount = teleportCount,
                FromPortal = lastFrom,
                ToPortal = lastTo,
                ResetCount = resetCount,
            };

            // Bandwidth: skip ticks where nothing visible changed (with a keep-alive).
            if (!s.DiffersNoticeablyFrom(lastSent) && Time.time - lastSendTime < KeepAliveInterval) return;
            s.Sequence = ++sequence;
            lastSent = s;
            lastSendTime = Time.time;
            SubmitStateRpc(s);
        }

        /// <summary>Server → owner: jump to this state (spawn, respawn, correction).</summary>
        [Rpc(SendTo.Owner)]
        void ApplyResetRpc(PlayerNetState s)
        {
            resetCount = s.ResetCount;
            teleportCount = s.TeleportCount;
            waitingForSpawn = false;
            controller.Body.isKinematic = !controller.IsAlive; // dead players stay frozen until revived
            controller.ForceState(s.Position, s.Rotation, s.Gravity, s.Velocity, s.Pitch);
        }

        /// <summary>Server → owner: add velocity (explosion knockback). The owner simulates
        /// movement, so the push has to be applied on their machine.</summary>
        [Rpc(SendTo.Owner)]
        public void KnockbackRpc(Vector3 velocityChange) => controller.AddVelocity(velocityChange);

        /// <summary>Owner → server: on a test map, go back to the start (no death counted).</summary>
        [Rpc(SendTo.Server)]
        public void RequestReturnRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !TestMap.Active) return;
            ServerRespawn();
        }

        // ------------------------------------------------------------------ server

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void SubmitStateRpc(PlayerNetState s, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            // Updates are unreliable and can arrive out of order: keep only newer ones.
            if (hasSequence && (short)(s.Sequence - lastSequence) <= 0) return;
            lastSequence = s.Sequence;
            hasSequence = true;
            if (s.ResetCount != accepted.ResetCount) return; // sent before our last reset reached them

            double now = Time.timeAsDouble;
            float dt = (float)(now - acceptedTime);
            if (Validate(s, dt))
            {
                if (s.TeleportCount != accepted.TeleportCount && TryGetComponent(out PlayerCombat combat))
                    combat.ServerNotePortalPass(Portal.FindById(s.FromPortal), s.Gravity);
                accepted = s;
                acceptedTime = now;
                state.Value = s;
                Record(now, s);
            }
            else
            {
                rejections++;
                // Don't spam corrections while one is still in flight.
                if (now - lastCorrectionTime > 0.25) ServerCorrect();
            }
        }

        bool Validate(PlayerNetState s, float dt)
        {
            if (!Finite(s.Position) || !Finite(s.Velocity) || s.Gravity.sqrMagnitude < 0.5f) return false;

            float allowed = maxValidatedSpeed * Mathf.Clamp(dt, 0.02f, 1f) + 1f;
            int teleports = (byte)(s.TeleportCount - accepted.TeleportCount);

            if (teleports == 0)
            {
                if (Vector3.Distance(s.Position, accepted.Position) > allowed) return false;
                // Gravity only ever changes by going through a portal.
                return Vector3.Angle(s.Gravity, accepted.Gravity) < 1f;
            }

            var from = Portal.FindById(s.FromPortal);
            var to = Portal.FindById(s.ToPortal);
            if (from == null || to == null || from.linkedPortal != to) return false;
            // Gravity after the pass must follow the portal rule. With one pass we know the
            // gravity they went in with, so the expected result is exact. (Several passes in
            // one tick are rare; the position checks below still apply to those.)
            if (teleports == 1)
            {
                Vector3 expected = Portal.GravityAfterPass(from, to, accepted.Gravity);
                if (Vector3.Angle(s.Gravity, expected) > 1f) return false;
            }
            // Must have been at the entrance (skip if several passes landed in one tick)...
            if (teleports == 1 && from.DistanceOutsideZone(accepted.Position) > allowed + 1f) return false;
            // ...and must now be at the exit.
            return to.DistanceOutsideZone(s.Position) <= allowed + 1f;
        }

        static bool Finite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        void ServerCorrect()
        {
            var s = accepted;
            s.Velocity = Vector3.zero;
            ServerReset(s);
        }

        void Record(double time, PlayerNetState s)
        {
            history.Add(new HistoryEntry { Time = time, State = s });
            while (history.Count > 2 && history[0].Time < time - HistoryLength) history.RemoveAt(0);
        }

        /// <summary>Server: where this player was at a past server time (lag compensation).
        /// Positions are interpolated, except across a portal pass or respawn.</summary>
        public PlayerNetState StateAt(double time)
        {
            if (history.Count == 0) return accepted;
            if (time <= history[0].Time) return history[0].State;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Time > time) continue;
                if (i == history.Count - 1) return history[i].State;
                var a = history[i];
                var b = history[i + 1];
                if (a.State.TeleportCount != b.State.TeleportCount || a.State.ResetCount != b.State.ResetCount)
                    return time - a.Time < b.Time - time ? a.State : b.State;
                float t = (float)((time - a.Time) / System.Math.Max(b.Time - a.Time, 1e-5));
                var s = b.State;
                s.Position = Vector3.Lerp(a.State.Position, b.State.Position, t);
                s.Rotation = Quaternion.Slerp(a.State.Rotation, b.State.Rotation, t);
                return s;
            }
            return accepted;
        }

        /// <summary>Server: the latest validated state (where this player really is now).</summary>
        public PlayerNetState AcceptedState => accepted;

        /// <summary>Puts this player at the spawn point farthest from everyone else.</summary>
        public void ServerRespawn()
        {
            var others = new List<Vector3>();
            foreach (var p in FindObjectsByType<NetworkGravityPlayer>(FindObjectsSortMode.None))
                if (p != this && p.IsSpawned) others.Add(p.CurrentState.Position);

            var spawn = SpawnPoint.PickFarthestFrom(others);
            Vector3 pos = spawn != null ? spawn.transform.position : transform.position;
            Quaternion rot = spawn != null ? spawn.transform.rotation : Quaternion.identity;

            ServerReset(new PlayerNetState
            {
                Position = pos,
                Rotation = rot,
                Pitch = 0f,
                Gravity = -(rot * Vector3.up),
                Velocity = Vector3.zero,
                TeleportCount = accepted.TeleportCount,
                FromPortal = -1,
                ToPortal = -1,
            });
        }

        void ServerReset(PlayerNetState s)
        {
            s.ResetCount = (byte)(accepted.ResetCount + 1);
            accepted = s;
            acceptedTime = Time.timeAsDouble;
            lastCorrectionTime = acceptedTime;
            state.Value = s;
            history.Clear();
            Record(acceptedTime, s);
            ApplyResetRpc(s);
        }

        // ------------------------------------------------------------------ remote

        void OnRemoteState(PlayerNetState previous, PlayerNetState current)
        {
            if (current.TeleportCount != previous.TeleportCount || current.ResetCount != previous.ResetCount)
            {
                snapshots.Clear();   // went through a portal or respawned: jump, don't slide
                snapNext = true;
                if (current.TeleportCount != previous.TeleportCount && current.ResetCount == previous.ResetCount)
                {
                    // Show the pass on everyone else's screen too.
                    var to = Portal.FindById(current.ToPortal);
                    var from = Portal.FindById(current.FromPortal);
                    if (from != null) from.PlayPassFx(from.transform.position);
                    if (to != null) to.PlayPassFx(current.Position);
                }
            }
            snapshots.Add(new Snapshot { Time = Time.timeAsDouble, State = current });
            if (snapshots.Count > 32) snapshots.RemoveAt(0);
        }

        void Update()
        {
            if (!IsSpawned || IsOwner || snapshots.Count == 0) return;

            double renderTime = Time.timeAsDouble - interpolationDelay;
            PlayerNetState a = snapshots[0].State, b = a;
            float t = 0f;

            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (snapshots[i].Time <= renderTime)
                {
                    a = snapshots[i].State;
                    if (i + 1 < snapshots.Count)
                    {
                        b = snapshots[i + 1].State;
                        double span = snapshots[i + 1].Time - snapshots[i].Time;
                        t = span > 1e-5 ? (float)((renderTime - snapshots[i].Time) / span) : 1f;
                    }
                    else
                    {
                        b = a;
                    }
                    // Older snapshots are no longer needed.
                    if (i > 1) snapshots.RemoveRange(0, i - 1);
                    break;
                }
            }

            t = Mathf.Clamp01(t);
            controller.ApplyRemoteState(
                Vector3.Lerp(a.Position, b.Position, t),
                Quaternion.Slerp(a.Rotation, b.Rotation, t),
                Mathf.Lerp(a.Pitch, b.Pitch, t),
                b.Gravity,
                snapNext);
            snapNext = false;
        }
    }
}
