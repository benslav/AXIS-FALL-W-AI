using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>Spawns simple glowing combat effects. Purely visual; no gameplay effect.</summary>
    public static class CombatFx
    {
        static Material unlit;
        static MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public static Material Unlit
        {
            get
            {
                if (unlit == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                    unlit = new Material(shader) { name = "CombatFx (runtime)" };
                }
                return unlit;
            }
        }

        public static void SetColor(Renderer r, Color c)
        {
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }

        /// <summary>Glowing line along a shot path. Portal hops are drawn as separate pieces.</summary>
        public static void Tracer(Vector3[] points, int count, Color color)
        {
            // Points come in pairs per leg: start, end (portal entry), exit start, end...
            for (int i = 0; i + 1 < count; i += 2)
            {
                var go = new GameObject("Tracer");
                var line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.SetPosition(0, points[i]);
                line.SetPosition(1, points[i + 1]);
                line.widthMultiplier = 0.035f;
                line.numCapVertices = 2;
                line.sharedMaterial = Unlit;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                SetColor(line, color * 2.5f);
                var fade = go.AddComponent<FxFade>();
                fade.lifetime = 0.09f;
                fade.startScale = fade.endScale = Vector3.one;
            }
        }

        public static void Impact(Vector3 point, Vector3 normal, Color color)
        {
            var go = Sphere("Impact", point + normal * 0.03f, color * 3f);
            var fade = go.AddComponent<FxFade>();
            fade.lifetime = 0.14f;
            fade.startScale = Vector3.one * 0.22f;
            fade.endScale = Vector3.one * 0.02f;

            for (int i = 0; i < 4; i++)
            {
                var spark = Sphere("Spark", point + normal * 0.05f, color * 2f);
                var f = spark.AddComponent<FxFade>();
                f.lifetime = 0.22f;
                f.startScale = Vector3.one * 0.05f;
                f.endScale = Vector3.zero;
                f.velocity = (normal + Random.insideUnitSphere * 0.9f).normalized * Random.Range(2f, 5f);
            }
        }

        /// <summary>One glowing spark that drifts and shrinks.</summary>
        public static void Spark(Vector3 point, Vector3 velocity, Color color, float lifetime)
        {
            var spark = Sphere("Spark", point, color * 2.5f);
            var f = spark.AddComponent<FxFade>();
            f.lifetime = lifetime;
            f.startScale = Vector3.one * 0.07f;
            f.endScale = Vector3.zero;
            f.velocity = velocity;
        }

        /// <summary>Burst of sparks in every direction (deaths, respawns).</summary>
        public static void Burst(Vector3 point, Color color, int count, float speed)
        {
            for (int i = 0; i < count; i++)
                Spark(point + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * Random.Range(speed * 0.4f, speed), color, Random.Range(0.4f, 0.8f));
        }

        public static void Flash(Vector3 point, Color color, float size = 0.18f)
        {
            var go = Sphere("Flash", point, color * 3f);
            AddLight(go, color, 2.5f, 4f);
            var fade = go.AddComponent<FxFade>();
            fade.lifetime = 0.06f;
            fade.startScale = Vector3.one * size;
            fade.endScale = Vector3.one * size * 0.4f;
        }

        public static void Explosion(Vector3 point, float radius, Color color)
        {
            var core = Sphere("Explosion", point, color * 3f);
            AddLight(core, color, 8f, radius * 3f);
            var fade = core.AddComponent<FxFade>();
            fade.lifetime = 0.35f;
            fade.startScale = Vector3.one * 0.4f;
            fade.endScale = Vector3.one * radius * 1.4f;

            var shell = Sphere("ExplosionShell", point, Color.Lerp(color, Color.white, 0.5f) * 2f);
            var f2 = shell.AddComponent<FxFade>();
            f2.lifetime = 0.2f;
            f2.startScale = Vector3.one * radius * 0.6f;
            f2.endScale = Vector3.zero;

            for (int i = 0; i < 10; i++)
            {
                var ember = Sphere("Ember", point, color * 2.5f);
                var f = ember.AddComponent<FxFade>();
                f.lifetime = Random.Range(0.3f, 0.55f);
                f.startScale = Vector3.one * 0.12f;
                f.endScale = Vector3.zero;
                f.velocity = Random.onUnitSphere * Random.Range(4f, 10f);
            }
        }

        static GameObject Sphere(string name, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>();
            col.enabled = false; // Destroy is deferred; don't let shots hit the effect meanwhile
            Object.Destroy(col);
            go.transform.position = position;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Unlit;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            SetColor(r, color);
            return go;
        }

        static void AddLight(GameObject go, Color color, float intensity, float range)
        {
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }
    }
}
