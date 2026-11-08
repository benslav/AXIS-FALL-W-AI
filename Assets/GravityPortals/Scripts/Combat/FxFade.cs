using UnityEngine;

namespace GravityPortals
{
    /// <summary>Short-lived effect: scales from start to end, fades its light and line, then removes itself.</summary>
    public class FxFade : MonoBehaviour
    {
        public float lifetime = 0.15f;
        public Vector3 startScale = Vector3.one;
        public Vector3 endScale = Vector3.zero;
        public Vector3 velocity;

        float age;
        Light fxLight;
        float lightStart;
        LineRenderer line;
        float widthStart;

        void Start()
        {
            fxLight = GetComponentInChildren<Light>();
            if (fxLight != null) lightStart = fxLight.intensity;
            line = GetComponent<LineRenderer>();
            if (line != null) widthStart = line.widthMultiplier;
            transform.localScale = startScale;
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / lifetime);
            transform.localScale = Vector3.Lerp(startScale, endScale, t);
            transform.position += velocity * Time.deltaTime;
            if (fxLight != null) fxLight.intensity = lightStart * (1f - t);
            if (line != null) line.widthMultiplier = widthStart * (1f - t);
            if (age >= lifetime) Destroy(gameObject);
        }
    }
}
