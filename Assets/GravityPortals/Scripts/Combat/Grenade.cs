using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// A thrown grenade. Server-simulated like the plasma bolt: it falls along the thrower's gravity
    /// (and follows any gravity field), bounces off walls, goes through portals with its speed and
    /// spin carried across like everything else, and explodes when its fuse runs out. Damage and
    /// knockback are worked out on the server (<see cref="PlayerCombat.ServerExplosion"/>).
    /// </summary>
    [RequireComponent(typeof(GravityBody))]
    public class Grenade : NetworkBehaviour
    {
        GravityBody body;
        bool exploded;
        float armedAt;
        float lastBounce = -1f;

        [HideInInspector] public ulong thrower;

        void Awake() => body = GetComponent<GravityBody>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) armedAt = Time.time;
        }

        void FixedUpdate()
        {
            if (!IsServer || exploded || !IsSpawned) return;
            if (Time.time - armedAt >= WeaponDefs.GrenadeFuse) Explode(transform.position);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!IsServer || exploded) return;
            // A clack on every real bounce (not while rolling).
            if (collision.relativeVelocity.magnitude < 2f || Time.time - lastBounce < 0.12f) return;
            lastBounce = Time.time;
            BounceRpc(collision.GetContact(0).point, Mathf.Clamp01(collision.relativeVelocity.magnitude / 12f));
        }

        void Explode(Vector3 point)
        {
            exploded = true;
            body.Body.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            // Lift the blast a little off the surface it's lying on (against its own gravity).
            Vector3 up = -body.GravityDirection;
            PlayerCombat.ServerExplosion(point, up, WeaponDefs.Grenade, thrower);
            ExplodeFxRpc(point + up * 0.1f);
            StartCoroutine(DespawnSoon());
        }

        IEnumerator DespawnSoon()
        {
            yield return new WaitForSeconds(0.1f);
            if (IsSpawned) NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BounceRpc(Vector3 point, float strength)
        {
            Sfx.Play3D(SoundBank.Click, point, 0.4f + 0.5f * strength, Random.Range(0.55f, 0.7f), 30f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ExplodeFxRpc(Vector3 point)
        {
            var def = WeaponDefs.GrenadeDef;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;
            CombatFx.Explosion(point, def.SplashRadius, def.Accent);
            Sfx.Play3D(SoundBank.Explosion, point, 1f, Random.Range(0.8f, 0.95f), 80f);
        }
    }
}
