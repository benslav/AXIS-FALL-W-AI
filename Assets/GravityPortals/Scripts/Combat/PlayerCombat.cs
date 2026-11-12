using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>A shot path sent to other clients so they can draw it.</summary>
    public struct ShotPath : INetworkSerializable
    {
        public byte Weapon;
        public byte Count;
        public Vector3[] Points;
        public bool HitWorld;
        public Vector3 HitPoint;
        public Vector3 HitNormal;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Weapon);
            s.SerializeValue(ref Count);
            if (s.IsReader) Points = new Vector3[Count];
            for (int i = 0; i < Count; i++) s.SerializeValue(ref Points[i]);
            s.SerializeValue(ref HitWorld);
            s.SerializeValue(ref HitPoint);
            s.SerializeValue(ref HitNormal);
        }
    }

    /// <summary>
    /// Health, weapons and scoring for one player.
    ///
    /// Server-authoritative: the owner fires instantly on their own screen (tracer,
    /// recoil, ammo), but only the server decides hits and damage. It checks fire rate,
    /// ammo and where the shot came from, then traces the shot against every player
    /// rewound to where the shooter saw them (lag compensation). Hit volumes are
    /// capsules along each player's own up axis, so floor, wall and ceiling players
    /// are all hit the same way. Rifle shots continue through portals.
    /// </summary>
    [RequireComponent(typeof(NetworkGravityPlayer))]
    public class PlayerCombat : NetworkBehaviour
    {
        public const int MaxHealth = 100;

        [Tooltip("Networked plasma bolt the server spawns.")]
        public NetworkObject projectilePrefab;
        public float respawnDelay = 3f;
        [Tooltip("Spawn protection after respawning (seconds).")]
        public float spawnProtection = 1.5f;
        [Tooltip("The furthest back the server will rewind players for a shot (seconds).")]
        public float maxRewind = 0.35f;

        static readonly List<PlayerCombat> all = new List<PlayerCombat>();
        static readonly List<(float time, string text)> killFeed = new List<(float, string)>();

        readonly NetworkVariable<int> health = new NetworkVariable<int>(MaxHealth);
        readonly NetworkVariable<bool> alive = new NetworkVariable<bool>(true);
        readonly NetworkVariable<int> kills = new NetworkVariable<int>();
        readonly NetworkVariable<int> deaths = new NetworkVariable<int>();
        readonly NetworkVariable<byte> weapon = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        NetworkGravityPlayer net;
        GravityPlayerController controller;

        // Owner (prediction + feel)
        int current;
        int[] ammo;
        float nextFireTime;
        float reloadEnd = -1f;
        float switchEnd;
        WeaponViewmodel viewmodel;
        float hitMarkerTime = -10f;
        bool hitMarkerKill, hitMarkerHead;
        float damageFlash;
        Vector3 damageFrom;
        string killedBy;

        // Server (authority)
        int[] serverAmmo;
        double[] serverReloadEnd;
        Queue<double>[] recentShots;
        float protectedUntil;

        // What other players see you holding
        GameObject[] worldGuns;

        public string DisplayName => LobbyState.NameOf(OwnerClientId);
        public bool IsAlive => alive.Value;
        public int Health => health.Value;
        public int Kills => kills.Value;
        public int Deaths => deaths.Value;
        public NetworkGravityPlayer Net => net;

        void Awake()
        {
            net = GetComponent<NetworkGravityPlayer>();
            controller = GetComponent<GravityPlayerController>();
            int n = WeaponDefs.All.Length;
            ammo = new int[n];
            serverAmmo = new int[n];
            serverReloadEnd = new double[n];
            recentShots = new Queue<double>[n];
            for (int i = 0; i < n; i++) recentShots[i] = new Queue<double>();
            Refill();
        }

        void Refill()
        {
            for (int i = 0; i < WeaponDefs.All.Length; i++)
            {
                ammo[i] = serverAmmo[i] = WeaponDefs.All[i].MagazineSize;
                serverReloadEnd[i] = -1;
            }
            reloadEnd = -1f;
        }

        public override void OnNetworkSpawn()
        {
            all.Add(this);
            alive.OnValueChanged += OnAliveChanged;
            weapon.OnValueChanged += OnWeaponChanged;
            health.OnValueChanged += OnHealthChanged;
            // Only matters when joining while this player is dead; alive is the default
            // (and touching it now would unfreeze a player the server hasn't placed yet).
            if (!alive.Value) controller.SetAlive(false);

            if (IsOwner)
            {
                var cam = Camera.main;
                if (cam != null) viewmodel = WeaponViewmodel.Create(cam.transform, controller);
                SwitchTo(0, instant: true);
            }
            else
            {
                BuildWorldGuns();
                ShowWorldGun(weapon.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            all.Remove(this);
            alive.OnValueChanged -= OnAliveChanged;
            weapon.OnValueChanged -= OnWeaponChanged;
            health.OnValueChanged -= OnHealthChanged;
            if (viewmodel != null) Destroy(viewmodel.gameObject);
        }

        void OnAliveChanged(bool previous, bool now)
        {
            if (previous && !now)
            {
                var c = Color.Lerp(WeaponDefs.Get(weapon.Value).Accent, Color.white, 0.3f);
                CombatFx.Burst(transform.position, c, 24, 7f);
                Sfx.Play3D(SoundBank.Death, transform.position, 0.9f);
            }
            controller.SetAlive(now);
            if (viewmodel != null) viewmodel.gameObject.SetActive(now);
            if (now && IsOwner)
            {
                killedBy = null;
                SwitchTo(current, instant: false);
            }
        }

        void OnWeaponChanged(byte previous, byte now)
        {
            if (!IsOwner) ShowWorldGun(now);
        }

        void OnHealthChanged(int previous, int now)
        {
            if (IsOwner && now < previous) damageFlash = Mathf.Clamp01(damageFlash + (previous - now) / 60f + 0.25f);
        }

        // ================================================================== owner input

        void Update()
        {
            if (!IsOwner || !IsSpawned) return;
            damageFlash = Mathf.MoveTowards(damageFlash, 0f, Time.deltaTime * 1.5f);
            if (!alive.Value) { SetScoped(false); return; }

            bool locked = Cursor.lockState == CursorLockMode.Locked;

            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchTo(WeaponDefs.Rifle, false);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchTo(WeaponDefs.Launcher, false);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchTo(WeaponDefs.Sniper, false);
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (locked && Mathf.Abs(wheel) > 0.01f)
                SwitchTo((current + (wheel > 0f ? 1 : WeaponDefs.All.Length - 1)) % WeaponDefs.All.Length, false);

            var def = WeaponDefs.Get(current);
            if (reloadEnd > 0f && Time.time >= reloadEnd)
            {
                ammo[current] = def.MagazineSize;
                reloadEnd = -1f;
                Sfx.Play2D(SoundBank.Click, 0.7f, 1.25f);
            }
            if (Input.GetKeyDown(KeyCode.R)) StartReload();
            if (Input.GetKeyDown(KeyCode.K)) SelfDestructRpc();

            // Aim down sights (right mouse): iron sights, or the full scope on the sniper.
            SetScoped(locked && def.ScopeFov > 0f && Input.GetMouseButton(1) && Time.time >= switchEnd && reloadEnd < 0f);
            if (Input.GetKeyDown(KeyCode.H) && TestMap.Active) net.RequestReturnRpc(); // test maps: back to start

            // Left mouse only (Unity's "Fire1" also fires on left Ctrl, which is crouch).
            bool wantsFire = locked && (def.Automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0));
            if (wantsFire && Time.time >= nextFireTime && Time.time >= switchEnd && reloadEnd < 0f)
            {
                if (ammo[current] <= 0)
                {
                    Sfx.Play2D(SoundBank.Empty, 0.6f);
                    nextFireTime = Time.time + 0.25f;
                    StartReload();
                }
                else Fire(def);
            }
        }

        void SwitchTo(int index, bool instant)
        {
            if (index == current && !instant && Time.time >= switchEnd) return;
            current = index;
            weapon.Value = (byte)index;
            reloadEnd = -1f;
            switchEnd = instant ? 0f : Time.time + 0.35f;
            if (viewmodel != null) viewmodel.SetWeapon(index, instant);
            if (!instant) Sfx.Play2D(SoundBank.Click, 0.5f, 0.7f);
        }

        bool scoped;

        void SetScoped(bool on)
        {
            if (on == scoped) return;
            scoped = on;
            var def = WeaponDefs.Get(current);
            controller.ScopeFov = on ? def.ScopeFov : 0f;
            controller.Aiming = on;
            if (viewmodel != null)
            {
                viewmodel.SetHidden(on && def.Scope);   // looking down a real scope
                viewmodel.SetAiming(on && !def.Scope);  // iron sights: the gun comes up to the centre
            }
            if (def.Scope) Sfx.Play2D(SoundBank.Click, 0.35f, on ? 1.5f : 1.2f);
        }

        /// <summary>Current spread in degrees: steadier standing still on the ground, and a
        /// scoped weapon is far tighter when scoped.</summary>
        float CurrentSpread(WeaponDef def)
        {
            bool steady = controller.IsGrounded && controller.Body.linearVelocity.magnitude < 1.5f;
            if (scoped) return steady ? def.ScopedSpread : def.ScopedMovingSpread;
            return steady ? def.Spread : def.MovingSpread;
        }

        void StartReload()
        {
            var def = WeaponDefs.Get(current);
            if (reloadEnd > 0f || ammo[current] >= def.MagazineSize || Time.time < switchEnd) return;
            reloadEnd = Time.time + def.ReloadTime;
            if (viewmodel != null) viewmodel.PlayReload(def.ReloadTime);
            Sfx.Play2D(SoundBank.Click, 0.7f, 0.8f);
            ReloadRpc((byte)current);
        }

        void Fire(WeaponDef def)
        {
            ammo[current]--;
            nextFireTime = Time.time + def.FireInterval;

            Vector3 origin = controller.EyePosition;
            Vector2 jitter = Random.insideUnitCircle * CurrentSpread(def);
            Vector3 direction = controller.AimRotation * Quaternion.Euler(jitter.y, jitter.x, 0f) * Vector3.forward;

            Vector3 muzzle = viewmodel != null ? viewmodel.MuzzlePosition : origin;
            if (viewmodel != null) { viewmodel.Kick(); viewmodel.Flash(def.Accent); }
            controller.AddRecoil(def.RecoilUp, Random.Range(-1f, 1f) * def.RecoilSide);
            Sfx.Play2D(ShotSound(def), def.Kind == WeaponKind.Hitscan ? (def.Scope ? 0.8f : 0.45f) : 0.75f,
                def.ShotPitch * Random.Range(0.95f, 1.05f));

            if (def.Kind == WeaponKind.Hitscan)
            {
                // Draw our own shot right away; the server decides what it actually hit.
                var trace = CombatTrace.Trace(origin, direction, def.Range, controller.IsOwnCollider, null, -1);
                trace.Points[0] = muzzle;
                CombatFx.Tracer(trace.Points, trace.PointCount, def.Accent);
                if (trace.HitWorld) CombatFx.Impact(trace.HitPoint, trace.HitNormal, def.Accent);
            }
            else if (!IsServer)
            {
                // Joining players see their bolt instantly; the server's real one is hidden from them.
                PreviewBolt.Launch(origin + direction * 0.6f, direction * def.ProjectileSpeed,
                    controller.GravityDirection, def.Accent, GetComponentsInChildren<Collider>());
            }

            FireRpc((byte)current, origin, direction);
        }

        // ================================================================== server: shooting

        [Rpc(SendTo.Server)]
        void ReloadRpc(byte weaponIndex, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || weaponIndex >= WeaponDefs.All.Length) return;
            if (serverReloadEnd[weaponIndex] > 0) return;
            serverReloadEnd[weaponIndex] = Time.timeAsDouble + WeaponDefs.All[weaponIndex].ReloadTime;
        }

        [Rpc(SendTo.Server)]
        void FireRpc(byte weaponIndex, Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !alive.Value) return;
            if (weaponIndex >= WeaponDefs.All.Length || direction.sqrMagnitude < 0.5f) return;
            var def = WeaponDefs.All[weaponIndex];
            double now = Time.timeAsDouble;

            // Fire rate: no more than the weapon allows in any one-second window (plus slack for network bunching).
            var shots = recentShots[weaponIndex];
            while (shots.Count > 0 && shots.Peek() < now - 1.0) shots.Dequeue();
            if (shots.Count >= Mathf.CeilToInt(def.ShotsPerSecond * 1.15f) + 2) return;

            // Ammo. The client finishes reloading slightly before we hear about it, so allow a little early.
            if (serverReloadEnd[weaponIndex] > 0 && now >= serverReloadEnd[weaponIndex] - 0.25)
            {
                serverAmmo[weaponIndex] = def.MagazineSize;
                serverReloadEnd[weaponIndex] = -1;
            }
            if (serverReloadEnd[weaponIndex] > 0 || serverAmmo[weaponIndex] <= 0) return;

            // The shot must come from where this player actually is.
            var me = net.AcceptedState;
            float allowedOffset = controller.eyeHeight + 2.5f + me.Velocity.magnitude * 0.2f;
            if (Vector3.Distance(origin, me.Position) > allowedOffset) return;

            serverAmmo[weaponIndex]--;
            shots.Enqueue(now);
            direction.Normalize();

            if (def.Kind == WeaponKind.Hitscan) ServerHitscan(def, weaponIndex, origin, direction, now);
            else ServerLaunch(def, weaponIndex, origin, direction);
        }

        void ServerHitscan(WeaponDef def, byte weaponIndex, Vector3 origin, Vector3 direction, double now)
        {
            // Rewind everyone to what this shooter was looking at:
            // their round trip plus the interpolation delay remote players are drawn with.
            double rtt = OwnerClientId == NetworkManager.ServerClientId
                ? 0.0
                : NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(OwnerClientId) / 1000.0;
            double rewind = System.Math.Min(rtt + net.interpolationDelay, maxRewind);

            var capsules = new List<HitCapsule>();
            var targets = new List<PlayerCombat>();
            foreach (var pc in all)
            {
                if (!pc.IsSpawned || !pc.alive.Value) continue;
                var s = pc == this ? pc.net.AcceptedState : pc.net.StateAt(now - rewind);
                capsules.Add(new HitCapsule
                {
                    Center = s.Position,
                    Up = s.Rotation * Vector3.up,
                    HalfHeight = pc.controller.CapsuleHalfHeight,
                    Radius = pc.controller.CapsuleRadius + 0.05f, // a little forgiveness
                    Index = targets.Count,
                });
                targets.Add(pc);
            }

            int self = targets.IndexOf(this);
            var trace = CombatTrace.Trace(origin, direction, def.Range, IsPlayerCollider, capsules, self);

            if (trace.CapsuleIndex >= 0)
            {
                var victim = targets[trace.CapsuleIndex];
                int damage = Mathf.RoundToInt(def.Damage * (trace.Headshot ? def.HeadshotMultiplier : 1f));
                victim.ServerDamage(damage, OwnerClientId, direction, trace.Headshot, weaponIndex);
            }
            else if (trace.HitWorld && trace.HitCollider != null)
            {
                var rb = trace.HitCollider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) rb.AddForceAtPosition(direction * 3f, trace.HitPoint, ForceMode.Impulse);
                var screen = BreakableScreen.Of(trace.HitCollider);
                if (screen != null && screen.ServerDamage(def.Damage)) ServerBreakScreen(screen.id);
            }

            ShotFxRpc(new ShotPath
            {
                Weapon = weaponIndex,
                Count = (byte)trace.PointCount,
                Points = trace.Points,
                HitWorld = trace.HitWorld,
                HitPoint = trace.HitPoint,
                HitNormal = trace.HitNormal,
            });
        }

        static bool IsPlayerCollider(Collider c) =>
            c.attachedRigidbody != null && c.attachedRigidbody.GetComponent<GravityPlayerController>() != null;

        void ServerLaunch(WeaponDef def, byte weaponIndex, Vector3 origin, Vector3 direction)
        {
            ShotFxRpc(new ShotPath { Weapon = weaponIndex, Count = 1, Points = new[] { origin + direction * 0.6f } });

            // Point-blank into a wall: explode right there instead of spawning inside it.
            if (Physics.Raycast(origin, direction, out var blocked, 0.8f, ~0, QueryTriggerInteraction.Ignore)
                && !IsPlayerCollider(blocked.collider))
            {
                ServerExplosion(blocked.point, blocked.normal, weaponIndex, OwnerClientId);
                ExplosionFxRpc(blocked.point + blocked.normal * 0.1f, weaponIndex);
                return;
            }

            if (projectilePrefab == null) return;
            var obj = Instantiate(projectilePrefab, origin + direction * 0.6f, Quaternion.LookRotation(direction));
            var body = obj.GetComponent<GravityBody>();
            body.GravityDirection = net.AcceptedState.Gravity;
            var projectile = obj.GetComponent<Projectile>();
            projectile.ShooterClientId = OwnerClientId;
            projectile.weaponIndex = weaponIndex;
            foreach (var own in GetComponentsInChildren<Collider>())
                foreach (var pc in obj.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(own, pc);
            obj.Spawn(true);
            body.Body.linearVelocity = direction * def.ProjectileSpeed;
        }

        /// <summary>Server: splash damage and knockback around a point. Walls block it.</summary>
        public static void ServerExplosion(Vector3 point, Vector3 normal, int weaponIndex, ulong shooterClientId)
        {
            var def = WeaponDefs.Get(weaponIndex);
            Vector3 origin = point + normal * 0.1f;

            foreach (var pc in all.ToArray())
            {
                if (!pc.IsSpawned || !pc.alive.Value) continue;
                var s = pc.net.AcceptedState;
                Vector3 up = s.Rotation * Vector3.up;
                float seg = pc.controller.CapsuleHalfHeight - pc.controller.CapsuleRadius;
                Vector3 closest = CombatTrace.ClosestPointOnSegment(s.Position - up * seg, s.Position + up * seg, origin);
                float distance = Mathf.Max(0f, Vector3.Distance(closest, origin) - pc.controller.CapsuleRadius);
                if (distance > def.SplashRadius) continue;
                if (Blocked(origin, closest)) continue;

                float falloff = 1f - distance / def.SplashRadius;
                float damage = Mathf.Lerp(def.MinSplashDamage, def.Damage, falloff);
                bool self = pc.OwnerClientId == shooterClientId;
                if (self) damage *= def.SelfDamageScale;

                Vector3 push = closest - origin;
                push = push.sqrMagnitude > 1e-4f ? push.normalized : normal;
                pc.net.KnockbackRpc(push * def.Knockback * Mathf.Lerp(0.35f, 1f, falloff));
                pc.ServerDamage(Mathf.RoundToInt(damage), shooterClientId, push, false, weaponIndex);
            }

            // Shove server-simulated props, and tear through screens in the blast.
            var shooter = Find(shooterClientId);
            foreach (var c in Physics.OverlapSphere(origin, def.SplashRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var screen = BreakableScreen.Of(c);
                if (screen != null)
                {
                    float d = Vector3.Distance(c.ClosestPoint(origin), origin);
                    float dmg = Mathf.Lerp(def.MinSplashDamage, def.Damage, 1f - d / def.SplashRadius);
                    if (screen.ServerDamage(dmg))
                    {
                        if (shooter != null) shooter.ServerBreakScreen(screen.id);
                        else if (all.Count > 0) all[0].ServerBreakScreen(screen.id);
                        else BreakableScreen.BreakLocal(screen.id);
                    }
                    continue;
                }
                var rb = c.attachedRigidbody;
                if (rb == null || rb.isKinematic || rb.GetComponent<GravityBody>() == null) continue;
                rb.AddExplosionForce(def.Knockback, origin, def.SplashRadius, 0f, ForceMode.VelocityChange);
            }
        }

        /// <summary>Server: a screen broke. Break it here and on every client.</summary>
        void ServerBreakScreen(int id)
        {
            BreakableScreen.BreakLocal(id);
            BreakScreenRpc(id);
        }

        [Rpc(SendTo.NotServer)]
        void BreakScreenRpc(int id) => BreakableScreen.BreakLocal(id);

        static bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 0.05f) return false;
            foreach (var h in Physics.RaycastAll(from, d / length, length, ~0, QueryTriggerInteraction.Ignore))
                if (!CombatTrace.IsDynamic(h.collider)) return true;
            return false;
        }

        // ================================================================== server: health

        public void ServerDamage(int amount, ulong attackerClientId, Vector3 direction, bool headshot, int weaponIndex)
        {
            if (!IsServer || !alive.Value || amount <= 0) return;
            if (Time.time < protectedUntil) return;

            health.Value = Mathf.Max(0, health.Value - amount);
            lastDamageTime = Time.time; // regen waits regenDelay after the latest hit
            regenCarry = 0f;
            DamagedRpc(direction);

            bool killed = health.Value == 0;
            var attacker = Find(attackerClientId);
            if (attacker != null && attacker != this)
            {
                attacker.HitConfirmRpc(killed, headshot);
                lastAttacker = attacker;
                lastAttackerWeapon = weaponIndex;
                lastHitTime = Time.time;
            }
            if (killed) ServerDie(attacker, weaponIndex, headshot);
        }

        // Who last hit us, so falling into the void after a knockback credits them.
        PlayerCombat lastAttacker;
        int lastAttackerWeapon;
        float lastHitTime = -100f;
        const float KnockOffCreditTime = 6f;

        [Header("Health regen")]
        [Tooltip("Seconds without taking damage before health starts coming back.")]
        public float regenDelay = 5f;
        [Tooltip("Health regained per second once regen starts.")]
        public float regenRate = 20f;
        float lastDamageTime = -100f;
        float regenCarry; // fractions of a point between physics steps

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || !alive.Value) return;

            if (health.Value < MaxHealth && Time.time - lastDamageTime >= regenDelay)
            {
                regenCarry += regenRate * Time.fixedDeltaTime;
                int gain = Mathf.FloorToInt(regenCarry);
                if (gain > 0)
                {
                    regenCarry -= gain;
                    health.Value = Mathf.Min(MaxHealth, health.Value + gain);
                }
            }

            var area = ArenaBounds.Current;
            if (area == null || area.Contains(net.AcceptedState.Position)) return;
            ServerFellIntoVoid();
        }

        void ServerFellIntoVoid()
        {
            bool pushed = lastAttacker != null && lastAttacker.IsSpawned && Time.time - lastHitTime < KnockOffCreditTime;
            health.Value = 0;
            fallWraps.Clear();
            ServerDie(pushed ? lastAttacker : this, pushed ? lastAttackerWeapon : current, false, fell: true);
        }

        // Endless fall on wrap-around maps: falling through the floor (or whatever is "down")
        // again and again means you've fallen into the void.
        readonly Queue<float> fallWraps = new Queue<float>();
        const int FallWrapsToDie = 3;
        const float FallWrapWindow = 10f;

        /// <summary>Server: called for every accepted portal pass.</summary>
        public void ServerNotePortalPass(Portal from, Vector3 gravity)
        {
            if (!IsServer || !alive.Value || from == null || !from.worldWrap) return;
            // Swinging on the grapple round the endless lattice isn't falling into the void.
            if (controller.IsGrappling) { fallWraps.Clear(); return; }
            // Only passes straight down through the surface below you count, not walking
            // sideways through a wall.
            if (Vector3.Angle(from.Normal, -gravity) > 30f) return;

            float now = Time.time;
            fallWraps.Enqueue(now);
            while (fallWraps.Count > 0 && fallWraps.Peek() < now - FallWrapWindow) fallWraps.Dequeue();
            if (fallWraps.Count >= FallWrapsToDie) ServerFellIntoVoid();
        }

        void ServerDie(PlayerCombat attacker, int weaponIndex, bool headshot, bool fell = false)
        {
            alive.Value = false;
            deaths.Value++;
            if (attacker != null && attacker != this) attacker.kills.Value++;
            KillFeedRpc(attacker != null ? attacker.OwnerClientId : OwnerClientId, OwnerClientId, (byte)weaponIndex, headshot, fell);
            lastAttacker = null;
            StartCoroutine(RespawnAfterDelay());
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);
            if (!IsSpawned) yield break;
            Refill();
            health.Value = MaxHealth;
            protectedUntil = Time.time + spawnProtection;
            net.ServerRespawn();
            alive.Value = true;
            RefillRpc();
        }

        [Rpc(SendTo.Server)]
        void SelfDestructRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !alive.Value) return;
            health.Value = 0;
            ServerDie(this, current, false);
        }

        static PlayerCombat Find(ulong clientId)
        {
            foreach (var pc in all) if (pc.OwnerClientId == clientId) return pc;
            return null;
        }

        // ================================================================== feedback RPCs

        static AudioClip ShotSound(WeaponDef def) =>
            def.Kind == WeaponKind.Hitscan ? SoundBank.RifleShot : SoundBank.LauncherShot;

        [Rpc(SendTo.NotOwner)]
        void ShotFxRpc(ShotPath path)
        {
            var def = WeaponDefs.Get(path.Weapon);
            if (path.Count >= 1) Sfx.Play3D(ShotSound(def), path.Points[0], 0.9f, def.ShotPitch * Random.Range(0.95f, 1.05f), def.Scope ? 120f : 60f);
            if (path.Count <= 1)
            {
                if (path.Count == 1) CombatFx.Flash(path.Points[0], def.Accent, 0.3f);
                return;
            }
            CombatFx.Tracer(path.Points, path.Count, def.Accent);
            CombatFx.Flash(path.Points[0], def.Accent);
            if (path.HitWorld) CombatFx.Impact(path.HitPoint, path.HitNormal, def.Accent);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ExplosionFxRpc(Vector3 point, byte weaponIndex)
        {
            var def = WeaponDefs.Get(weaponIndex);
            CombatFx.Explosion(point, def.SplashRadius, def.Accent);
            Sfx.Play3D(SoundBank.Explosion, point, 1f, Random.Range(0.9f, 1.1f), 80f);
        }

        [Rpc(SendTo.Owner)]
        void HitConfirmRpc(bool killed, bool headshot)
        {
            hitMarkerTime = Time.time;
            hitMarkerKill = killed;
            hitMarkerHead = headshot;
            if (killed) Sfx.Play2D(SoundBank.KillConfirm, 0.6f);
            else Sfx.Play2D(SoundBank.HitMarker, 0.45f, headshot ? 1.35f : 1f);
        }

        [Rpc(SendTo.Owner)]
        void DamagedRpc(Vector3 direction)
        {
            damageFrom = direction;
            Sfx.Play2D(SoundBank.Hurt, 0.6f, Random.Range(0.9f, 1.1f));
        }

        [Rpc(SendTo.Owner)]
        void RefillRpc()
        {
            Refill();
            if (viewmodel != null) viewmodel.SetWeapon(current, instant: false);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void KillFeedRpc(ulong killer, ulong victim, byte weaponIndex, bool headshot, bool fell)
        {
            string victimName = LobbyState.NameOf(victim);
            string text;
            if (fell) text = killer == victim
                ? $"{victimName} fell into the void"
                : $"{LobbyState.NameOf(killer)} knocked {victimName} into the void";
            else text = killer == victim
                ? $"{victimName} self-destructed"
                : $"{LobbyState.NameOf(killer)}  [{WeaponDefs.Get(weaponIndex).Name}{(headshot ? ", headshot" : "")}]  {victimName}";
            killFeed.Add((Time.time, text));
            if (killFeed.Count > 6) killFeed.RemoveAt(0);

            var local = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            var me = Find(local);
            if (me != null && victim == local) me.killedBy = killer == victim ? "yourself" : LobbyState.NameOf(killer);
        }

        // ================================================================== third-person guns

        void BuildWorldGuns()
        {
            if (controller.headPivot == null) return;
            worldGuns = new GameObject[WeaponDefs.All.Length];
            for (int i = 0; i < worldGuns.Length; i++)
            {
                var def = WeaponDefs.All[i];
                var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gun.name = def.Name;
                var col = gun.GetComponent<Collider>();
                col.enabled = false;
                Destroy(col);
                gun.transform.SetParent(controller.headPivot, false);
                gun.transform.localPosition = new Vector3(0.28f, -0.22f, 0.32f);
                gun.transform.localScale = i == WeaponDefs.Rifle ? new Vector3(0.07f, 0.09f, 0.6f)
                    : i == WeaponDefs.Sniper ? new Vector3(0.06f, 0.08f, 0.95f)
                    : new Vector3(0.15f, 0.15f, 0.55f);
                var r = gun.GetComponent<Renderer>();
                r.sharedMaterial = CombatFx.Unlit;
                CombatFx.SetColor(r, Color.Lerp(def.Accent, Color.black, 0.35f));
                worldGuns[i] = gun;
            }
        }

        void ShowWorldGun(int index)
        {
            if (worldGuns == null) return;
            for (int i = 0; i < worldGuns.Length; i++) worldGuns[i].SetActive(i == index);
        }

        // ================================================================== HUD

        void OnGUI()
        {
            if (!IsOwner || !IsSpawned) return;
            var white = Texture2D.whiteTexture;
            float w = Screen.width, h = Screen.height, cx = w * 0.5f, cy = h * 0.5f;
            var def = WeaponDefs.Get(current);

            if (damageFlash > 0f)
            {
                GUI.color = new Color(1f, 0f, 0f, damageFlash * 0.35f);
                GUI.DrawTexture(new Rect(0, 0, w, h), white);
                // Arrow toward whoever hit you, in screen space.
                var cam = Camera.main;
                if (cam != null && damageFrom.sqrMagnitude > 0f)
                {
                    Vector3 local = cam.transform.InverseTransformDirection(-damageFrom);
                    Vector2 dir = new Vector2(local.x, local.z);
                    if (dir.sqrMagnitude > 1e-4f)
                    {
                        dir.Normalize();
                        GUI.color = new Color(1f, 0.15f, 0.1f, damageFlash);
                        GUI.DrawTexture(new Rect(cx + dir.x * 130f - 9f, cy - dir.y * 130f - 9f, 18f, 18f), white);
                    }
                }
            }

            if (!alive.Value)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(0, 0, w, h), white);
                GUI.color = Color.white;
                var big = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, cy - 40, w, 40), killedBy != null ? $"Eliminated by {killedBy}" : "Eliminated", big);
                GUI.Label(new Rect(0, cy + 5, w, 30), "Respawning...", new GUIStyle(big) { fontSize = 18 });
            }
            else
            {
                if (scoped && !def.Scope)
                {
                    // Iron sights: just a small dot where the shot goes.
                    GUI.color = new Color(1f, 1f, 1f, 0.8f);
                    GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), white);
                }
                else if (scoped)
                {
                    // Scope: black all round a square sight with fine cross-hairs and a centre dot.
                    float size = h * 0.92f, x0 = cx - size * 0.5f, y0 = cy - size * 0.5f;
                    GUI.color = Color.black;
                    GUI.DrawTexture(new Rect(0, 0, x0, h), white);
                    GUI.DrawTexture(new Rect(x0 + size, 0, w - x0 - size, h), white);
                    GUI.DrawTexture(new Rect(x0, 0, size, y0), white);
                    GUI.DrawTexture(new Rect(x0, y0 + size, size, h - y0 - size), white);
                    GUI.color = new Color(0f, 0f, 0f, 0.85f);
                    GUI.DrawTexture(new Rect(x0, cy - 0.5f, size, 1f), white);
                    GUI.DrawTexture(new Rect(cx - 0.5f, y0, 1f, size), white);
                    GUI.DrawTexture(new Rect(x0, cy - 1.5f, size * 0.3f, 3f), white);
                    GUI.DrawTexture(new Rect(x0 + size * 0.7f, cy - 1.5f, size * 0.3f, 3f), white);
                    GUI.DrawTexture(new Rect(cx - 1.5f, y0 + size * 0.7f, 3f, size * 0.3f), white);
                    GUI.color = def.Accent;
                    GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), white);
                }
                else
                {
                    // Crosshair opens up with spread.
                    float gap = 4f + CurrentSpread(def) * 6f;
                    GUI.color = new Color(1f, 1f, 1f, 0.9f);
                    GUI.DrawTexture(new Rect(cx - gap - 8, cy - 1, 8, 2), white);
                    GUI.DrawTexture(new Rect(cx + gap, cy - 1, 8, 2), white);
                    GUI.DrawTexture(new Rect(cx - 1, cy - gap - 8, 2, 8), white);
                    GUI.DrawTexture(new Rect(cx - 1, cy + gap, 2, 8), white);
                }

                float since = Time.time - hitMarkerTime;
                if (since < 0.25f)
                {
                    GUI.color = hitMarkerKill ? new Color(1f, 0.2f, 0.2f) : hitMarkerHead ? new Color(1f, 0.85f, 0.2f) : Color.white;
                    var m = GUI.matrix;
                    for (int i = 0; i < 4; i++)
                    {
                        GUIUtility.RotateAroundPivot(45f + 90f * i, new Vector2(cx, cy));
                        GUI.DrawTexture(new Rect(cx + 6, cy - 1, 8, 2), white);
                        GUI.matrix = m;
                    }
                }
            }

            // Health
            float hp = health.Value / (float)MaxHealth;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(20, h - 46, 220, 26), white);
            GUI.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.3f, 1f, 0.6f), hp);
            GUI.DrawTexture(new Rect(23, h - 43, 214 * hp, 20), white);
            GUI.color = Color.white;
            var label = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(28, h - 46, 200, 26), $"{health.Value}", label);

            // Ammo
            var right = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontSize = 22 };
            GUI.Label(new Rect(w - 260, h - 56, 240, 30), $"{ammo[current]} / {def.MagazineSize}", right);
            GUI.Label(new Rect(w - 260, h - 82, 240, 26), def.Name, new GUIStyle(right) { fontSize = 14 });
            if (reloadEnd > 0f)
            {
                float p = 1f - (reloadEnd - Time.time) / def.ReloadTime;
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(cx - 60, cy + 40, 120, 6), white);
                GUI.color = def.Accent;
                GUI.DrawTexture(new Rect(cx - 60, cy + 40, 120 * Mathf.Clamp01(p), 6), white);
                GUI.color = Color.white;
            }

            // Kill feed
            float y = 280f;
            var feed = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
            foreach (var entry in killFeed)
            {
                if (Time.time - entry.time > 6f) continue;
                GUI.Label(new Rect(w - 420, y, 410, 22), entry.text, feed);
                y += 20f;
            }

            if (Input.GetKey(KeyCode.Tab)) DrawScoreboard();
        }

        void DrawScoreboard()
        {
            var players = new List<PlayerCombat>(all);
            players.Sort((a, b) => b.kills.Value != a.kills.Value
                ? b.kills.Value.CompareTo(a.kills.Value)
                : a.deaths.Value.CompareTo(b.deaths.Value));

            float w = 360f, rowH = 24f, hgt = 50f + rowH * players.Count;
            var area = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.25f, w, hgt);
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            var head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(area.x + 14, area.y + 10, 200, rowH), "Player", head);
            GUI.Label(new Rect(area.x + 220, area.y + 10, 60, rowH), "Kills", head);
            GUI.Label(new Rect(area.x + 285, area.y + 10, 60, rowH), "Deaths", head);
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                float y = area.y + 38 + rowH * i;
                GUI.color = p.IsOwner ? new Color(0.5f, 1f, 0.9f) : Color.white;
                GUI.Label(new Rect(area.x + 14, y, 200, rowH), p.DisplayName + (p.alive.Value ? "" : "  (dead)"));
                GUI.Label(new Rect(area.x + 220, y, 60, rowH), p.kills.Value.ToString());
                GUI.Label(new Rect(area.x + 285, y, 60, rowH), p.deaths.Value.ToString());
            }
            GUI.color = Color.white;
        }
    }
}
