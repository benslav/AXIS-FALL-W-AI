using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Plasma bolt. Server-simulated: it flies under its own (light) gravity, goes through
    /// portals like everything else (its gravity switches too), and explodes on contact.
    /// Damage and knockback are worked out on the server.
    /// </summary>
    [RequireComponent(typeof(GravityBody))]
    public class Projectile : NetworkBehaviour
    {
        public float maxLifetime = 6f;

        [HideInInspector] public int weaponIndex = WeaponDefs.Launcher;

        // Synced so the shooter's client can hide this bolt (it shows its own preview instead).
        readonly NetworkVariable<ulong> shooter = new NetworkVariable<ulong>();

        GravityBody body;
        bool exploded;
        float spawnTime;

        public ulong ShooterClientId
        {
            get => shooter.Value;
            set => shooter.Value = value; // server, before Spawn
        }

        void Awake() => body = GetComponent<GravityBody>();

        public override void OnNetworkSpawn()
        {
            spawnTime = Time.time;
            if (!IsServer && shooter.Value == NetworkManager.LocalClientId)
            {
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!IsServer || exploded) return;
            var contact = collision.GetContact(0);
            Explode(contact.point, contact.normal);
        }

        void FixedUpdate()
        {
            if (!IsServer || exploded || !IsSpawned) return;
            if (Time.time - spawnTime > maxLifetime)
                Explode(transform.position, -body.Body.linearVelocity.normalized);
        }

        void Explode(Vector3 point, Vector3 normal)
        {
            exploded = true;
            body.Body.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

            PlayerCombat.ServerExplosion(point, normal, weaponIndex, shooter.Value);
            ExplodeFxRpc(point, normal);
            StartCoroutine(DespawnSoon());
        }

        IEnumerator DespawnSoon()
        {
            // Give the effect message a moment to go out before the object disappears.
            yield return new WaitForSeconds(0.1f);
            if (IsSpawned) NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ExplodeFxRpc(Vector3 point, Vector3 normal)
        {
            var def = WeaponDefs.Get(weaponIndex);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            CombatFx.Explosion(point + normal * 0.1f, def.SplashRadius, def.Accent);
            Sfx.Play3D(SoundBank.Explosion, point, 1f, Random.Range(0.9f, 1.1f), 80f);
        }
    }
}
