using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>
    /// Shooter-only stand-in for a plasma bolt, so a client sees their shot leave the
    /// barrel instantly instead of one round trip later. It flies with the same physics
    /// (portals and gravity switching included) and simply vanishes on impact; the
    /// server's real bolt, hidden from the shooter, does the damage and the explosion.
    /// </summary>
    public class PreviewBolt : MonoBehaviour
    {
        float dieAt;

        public static void Launch(Vector3 position, Vector3 velocity, Vector3 gravity, Color color, Collider[] ignore)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PreviewBolt";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.28f;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = CombatFx.Unlit;
            r.shadowCastingMode = ShadowCastingMode.Off;
            CombatFx.SetColor(r, color * 3f);

            var col = go.GetComponent<Collider>();
            foreach (var c in ignore) if (c != null) Physics.IgnoreCollision(col, c);

            var body = go.AddComponent<GravityBody>(); // adds the Rigidbody
            body.GravityDirection = gravity;
            body.gravityStrength = 4f;
            body.Body.mass = 1f;
            body.Body.interpolation = RigidbodyInterpolation.Interpolate;
            body.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.Body.linearVelocity = velocity;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 5f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;

            go.AddComponent<PreviewBolt>().dieAt = Time.time + 6f;
        }

        void Update()
        {
            if (Time.time > dieAt) Destroy(gameObject);
        }

        void OnCollisionEnter(Collision collision) => Destroy(gameObject);
    }
}
