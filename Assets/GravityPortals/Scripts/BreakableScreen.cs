using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// A shoji screen that shots and explosions tear through. The server keeps its health and
    /// decides when it breaks, then tells everyone (<see cref="PlayerCombat"/> sends the break by
    /// id); every machine hides it and grows it back after <see cref="regrowTime"/>, waiting
    /// while anyone is standing in its way.
    /// </summary>
    public class BreakableScreen : MonoBehaviour
    {
        public int id = -1;
        public float health = 30f;
        public float regrowTime = 30f;

        static readonly Dictionary<int, BreakableScreen> all = new Dictionary<int, BreakableScreen>();
        static readonly Collider[] overlap = new Collider[8];

        Renderer rend;
        Collider col;
        float hp;
        bool broken;
        float regrowAt;

        public bool IsBroken => broken;

        void Awake()
        {
            rend = GetComponent<Renderer>();
            col = GetComponent<Collider>();
            hp = health;
        }

        void OnEnable()
        {
            if (id >= 0) all[id] = this;
        }

        void OnDisable()
        {
            if (id >= 0 && all.TryGetValue(id, out var s) && s == this) all.Remove(id);
        }

        public static BreakableScreen Of(Collider c) => c != null ? c.GetComponent<BreakableScreen>() : null;

        /// <summary>Server: take damage; true if this hit broke it.</summary>
        public bool ServerDamage(float amount)
        {
            if (broken || amount <= 0f) return false;
            hp -= amount;
            return hp <= 0f;
        }

        /// <summary>Breaks the screen with this id on this machine.</summary>
        public static void BreakLocal(int id)
        {
            if (all.TryGetValue(id, out var s)) s.Break();
        }

        void Break()
        {
            if (broken) return;
            broken = true;
            col.enabled = false;
            rend.enabled = false;
            regrowAt = Time.time + regrowTime;

            if (Application.isBatchMode) return;
            // Shreds of paper burst out both sides.
            var b = rend.bounds;
            Color paper = new Color(1f, 0.92f, 0.8f);
            for (int i = 0; i < 14; i++)
            {
                Vector3 p = b.center + Vector3.Scale(Random.insideUnitSphere * 0.5f, b.size);
                CombatFx.Spark(p, Random.insideUnitSphere * 3f, paper, Random.Range(0.4f, 0.8f));
            }
            Sfx.Play3D(SoundBank.Land, b.center, 0.6f, Random.Range(1.6f, 1.9f));
        }

        void Update()
        {
            if (!broken || Time.time < regrowAt) return;
            // Don't grow back through someone standing in the gap: try again shortly.
            var t = transform;
            int n = Physics.OverlapBoxNonAlloc(t.position, t.lossyScale * 0.5f, overlap, t.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (overlap[i].attachedRigidbody != null)
                {
                    regrowAt = Time.time + 1f;
                    return;
                }
            }
            broken = false;
            hp = health;
            col.enabled = true;
            rend.enabled = true;
        }
    }
}
