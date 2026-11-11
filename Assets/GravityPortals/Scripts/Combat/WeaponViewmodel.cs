using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>
    /// The first-person weapons, built from primitives and attached to the camera.
    /// Handles sway, walk bob, fire kick, muzzle flash, reload and switch animations.
    /// All motion is in camera space, so it looks the same on floors, walls and ceilings.
    /// </summary>
    public class WeaponViewmodel : MonoBehaviour
    {
        static readonly Vector3 HipPosition = new Vector3(0.2f, -0.19f, 0.38f);
        const float SwitchTime = 0.35f;

        GravityPlayerController controller;
        Transform pivot;
        readonly Transform[] models = new Transform[WeaponDefs.All.Length];
        readonly Transform[] muzzles = new Transform[WeaponDefs.All.Length];
        Renderer flash;
        Light flashLight;
        float flashUntil;

        int current;
        float kick;
        Vector2 sway;
        float bobPhase;
        float reloadStart = -10f, reloadDuration = 1f;
        float switchStart = -10f;

        public Vector3 MuzzlePosition => muzzles[current].position;

        public static WeaponViewmodel Create(Transform cameraTransform, GravityPlayerController controller)
        {
            var go = new GameObject("Viewmodel");
            go.transform.SetParent(cameraTransform, false);
            var vm = go.AddComponent<WeaponViewmodel>();
            vm.controller = controller;
            vm.Build();
            return vm;
        }

        void Build()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var metal = new Material(lit) { name = "Gunmetal (runtime)" };
            metal.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.2f));
            metal.SetFloat("_Smoothness", 0.6f);
            metal.SetFloat("_Metallic", 0.7f);
            var light = new Material(lit) { name = "GunLight (runtime)" };
            light.SetColor("_BaseColor", new Color(0.55f, 0.58f, 0.64f));
            light.SetFloat("_Smoothness", 0.5f);

            // Draw the weapon after portal surfaces (queue 2010): while you step through a
            // portal, its surface is drawn on top of everything behind it, and the gun must stay
            // on top of that.
            glow = new Material(CombatFx.Unlit) { name = "GunGlow (runtime)" };
            metal.renderQueue = light.renderQueue = glow.renderQueue = ViewmodelQueue;

            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(transform, false);

            // Pulse Rifle
            var rifle = Group("Pulse Rifle", WeaponDefs.Rifle);
            Color cyan = WeaponDefs.Get(WeaponDefs.Rifle).Accent;
            Box(rifle, metal, new Vector3(0, 0, 0.05f), new Vector3(0.07f, 0.09f, 0.42f));
            Box(rifle, light, new Vector3(0, -0.01f, -0.2f), new Vector3(0.06f, 0.08f, 0.14f));
            Box(rifle, metal, new Vector3(0, -0.09f, 0.04f), new Vector3(0.045f, 0.12f, 0.07f), Quaternion.Euler(12, 0, 0));
            Box(rifle, metal, new Vector3(0, 0.065f, 0f), new Vector3(0.03f, 0.03f, 0.09f));
            Glow(rifle, PrimitiveType.Cube, new Vector3(0, 0.03f, 0.08f), new Vector3(0.074f, 0.012f, 0.3f), Quaternion.identity, cyan);
            Glow(rifle, PrimitiveType.Cube, new Vector3(0, 0.082f, -0.03f), new Vector3(0.012f, 0.012f, 0.02f), Quaternion.identity, cyan);
            Part(rifle, PrimitiveType.Cylinder, light, new Vector3(0, 0.015f, 0.33f), new Vector3(0.026f, 0.1f, 0.026f), Quaternion.Euler(90, 0, 0));
            muzzles[WeaponDefs.Rifle] = Marker(rifle, new Vector3(0, 0.015f, 0.44f));

            // Plasma Launcher
            var launcher = Group("Plasma Launcher", WeaponDefs.Launcher);
            Color orange = WeaponDefs.Get(WeaponDefs.Launcher).Accent;
            Box(launcher, metal, new Vector3(0, 0, 0.02f), new Vector3(0.12f, 0.12f, 0.38f));
            Part(launcher, PrimitiveType.Cylinder, light, new Vector3(0, 0.02f, 0.26f), new Vector3(0.075f, 0.16f, 0.075f), Quaternion.Euler(90, 0, 0));
            Glow(launcher, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.16f), new Vector3(0.09f, 0.006f, 0.09f), Quaternion.Euler(90, 0, 0), orange);
            Glow(launcher, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.3f), new Vector3(0.09f, 0.006f, 0.09f), Quaternion.Euler(90, 0, 0), orange);
            Glow(launcher, PrimitiveType.Sphere, new Vector3(0, 0.075f, -0.05f), new Vector3(0.06f, 0.035f, 0.1f), Quaternion.identity, orange);
            Box(launcher, light, new Vector3(0, -0.1f, -0.04f), new Vector3(0.04f, 0.1f, 0.05f), Quaternion.Euler(-10, 0, 0));
            muzzles[WeaponDefs.Launcher] = Marker(launcher, new Vector3(0, 0.02f, 0.43f));

            // Rail Sniper: long barrel, scope on top, glowing rails.
            var sniper = Group("Rail Sniper", WeaponDefs.Sniper);
            Color violet = WeaponDefs.Get(WeaponDefs.Sniper).Accent;
            Box(sniper, metal, new Vector3(0, 0, 0.02f), new Vector3(0.065f, 0.085f, 0.4f));
            Box(sniper, light, new Vector3(0, -0.015f, -0.25f), new Vector3(0.055f, 0.1f, 0.18f));
            Box(sniper, metal, new Vector3(0, -0.09f, -0.02f), new Vector3(0.04f, 0.11f, 0.06f), Quaternion.Euler(12, 0, 0));
            Part(sniper, PrimitiveType.Cylinder, metal, new Vector3(0, 0.01f, 0.42f), new Vector3(0.022f, 0.22f, 0.022f), Quaternion.Euler(90, 0, 0));
            Part(sniper, PrimitiveType.Cylinder, light, new Vector3(0, 0.085f, 0.0f), new Vector3(0.045f, 0.11f, 0.045f), Quaternion.Euler(90, 0, 0));
            Glow(sniper, PrimitiveType.Cylinder, new Vector3(0, 0.085f, -0.11f), new Vector3(0.035f, 0.004f, 0.035f), Quaternion.Euler(90, 0, 0), violet);
            Glow(sniper, PrimitiveType.Cube, new Vector3(0.036f, 0.0f, 0.14f), new Vector3(0.008f, 0.012f, 0.3f), Quaternion.identity, violet);
            Glow(sniper, PrimitiveType.Cube, new Vector3(-0.036f, 0.0f, 0.14f), new Vector3(0.008f, 0.012f, 0.3f), Quaternion.identity, violet);
            muzzles[WeaponDefs.Sniper] = Marker(sniper, new Vector3(0, 0.01f, 0.65f));

            // Muzzle flash (moved to the active muzzle when it fires).
            var flashGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flashGo.name = "MuzzleFlash";
            Strip(flashGo);
            flashGo.transform.SetParent(pivot, false);
            flashGo.transform.localScale = Vector3.one * 0.07f;
            flash = flashGo.GetComponent<Renderer>();
            flash.sharedMaterial = glow;
            flashLight = flashGo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = 5f;
            flashLight.intensity = 3f;
            flashLight.shadows = LightShadows.None;
            flashGo.SetActive(false);

            SetWeapon(WeaponDefs.Rifle, true);
        }

        Transform Group(string name, int index)
        {
            var t = new GameObject(name).transform;
            t.SetParent(pivot, false);
            models[index] = t;
            return t;
        }

        static Transform Marker(Transform parent, Vector3 pos)
        {
            var t = new GameObject("Muzzle").transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static void Box(Transform parent, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null) =>
            Part(parent, PrimitiveType.Cube, mat, pos, scale, rot ?? Quaternion.identity);

        const int ViewmodelQueue = 2050;
        static Material glow;

        static void Glow(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Color color)
        {
            var r = Part(parent, type, glow, pos, scale, rot);
            CombatFx.SetColor(r, color * 2.2f);
        }

        static Renderer Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(type);
            Strip(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static void Strip(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            c.enabled = false;
            Destroy(c);
        }

        // ------------------------------------------------------------------ API

        public void SetWeapon(int index, bool instant)
        {
            current = index;
            for (int i = 0; i < models.Length; i++) models[i].gameObject.SetActive(i == index);
            switchStart = instant ? -10f : Time.time;
            reloadStart = -10f;
        }

        public void Kick() => kick = Mathf.Min(kick + (current == WeaponDefs.Rifle ? 0.45f : 1f), 1.4f);

        /// <summary>Hides the gun (looking down a scope).</summary>
        public void SetHidden(bool hidden) => pivot.gameObject.SetActive(!hidden);

        public void Flash(Color color)
        {
            flash.transform.position = MuzzlePosition;
            CombatFx.SetColor(flash, color * 3f);
            flashLight.color = color;
            flash.gameObject.SetActive(true);
            flashUntil = Time.time + 0.045f;
        }

        public void PlayReload(float duration)
        {
            reloadStart = Time.time;
            reloadDuration = duration;
        }

        // ------------------------------------------------------------------ animation

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (flash.gameObject.activeSelf && Time.time > flashUntil) flash.gameObject.SetActive(false);

            kick = Mathf.MoveTowards(kick, 0f, dt * 6f) * Mathf.Exp(-8f * dt);

            // Sway lags behind the mouse.
            Vector2 look = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"))
                : Vector2.zero;
            sway = Vector2.Lerp(sway, Vector2.ClampMagnitude(-look * 0.012f, 0.04f), 1f - Mathf.Exp(-10f * dt));

            // Walk bob, relative to the player's own up.
            Vector3 up = controller.Up;
            Vector3 lateral = Vector3.ProjectOnPlane(controller.Body.linearVelocity, up);
            float speed = controller.IsGrounded ? lateral.magnitude : 0f;
            bobPhase += dt * speed * 1.5f;
            float bobAmount = Mathf.Clamp01(speed / 7f);
            Vector3 bob = new Vector3(Mathf.Cos(bobPhase) * 0.009f, Mathf.Abs(Mathf.Sin(bobPhase)) * 0.011f, 0f) * bobAmount;

            Vector3 pos = HipPosition + new Vector3(sway.x, sway.y, 0f) + bob + new Vector3(0f, 0f, -0.07f * kick);
            Vector3 euler = new Vector3(-7f * kick, sway.x * 120f, -sway.x * 80f);

            float reload = (Time.time - reloadStart) / reloadDuration;
            if (reload >= 0f && reload <= 1f)
            {
                float dip = Mathf.Sin(reload * Mathf.PI);
                pos += new Vector3(-0.03f, -0.12f, 0f) * dip;
                euler += new Vector3(18f, 0f, 35f) * dip;
            }

            float raise = (Time.time - switchStart) / SwitchTime;
            if (raise >= 0f && raise < 1f)
            {
                float lower = 1f - Mathf.SmoothStep(0f, 1f, raise);
                pos += new Vector3(0f, -0.25f, 0f) * lower;
                euler += new Vector3(35f, 0f, 0f) * lower;
            }

            pivot.localPosition = pos;
            pivot.localRotation = Quaternion.Euler(euler);
        }
    }
}
