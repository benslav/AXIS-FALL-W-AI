using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>
    /// Makes a portal show the world on the other side.
    ///
    /// Each portal has its own camera. Before every frame it is placed where the player's
    /// camera would be if the two portals were one opening (behind the linked portal,
    /// looking out of it), and its near plane is tilted to lie exactly on the linked
    /// portal's surface (an oblique projection) so the wall that portal is mounted on
    /// doesn't block the view. The result goes to a render texture that the portal
    /// surface samples in screen space, so it lines up seamlessly with the real world.
    ///
    /// Only portals actually in view (and in front of the camera) render, at a reduced
    /// resolution, to keep the cost down.
    /// </summary>
    [RequireComponent(typeof(Portal))]
    public class PortalView : MonoBehaviour
    {
        public static float resolutionScale = 0.75f;
        public static float maxDistance = 80f;

        static readonly List<PortalView> views = new List<PortalView>();
        static readonly Plane[] frustum = new Plane[6];
        static bool hooked;
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int HasViewId = Shader.PropertyToID("_HasView");
        static readonly int ThickId = Shader.PropertyToID("_Thick");
        static readonly int ClarityId = Shader.PropertyToID("_Clarity");
        float currentScale = -1f;

        Portal portal;
        Camera viewCamera;
        RenderTexture target;
        Material material;
        Renderer surface;
        float thickness = 0.02f;

        public static int LayerPortalSurface => LayerMask.NameToLayer("PortalSurface");
        public static int LayerLocalPlayer => LayerMask.NameToLayer("LocalPlayer");

        public void Init(Portal owner, Renderer surfaceRenderer, Material instance)
        {
            portal = owner;
            surface = surfaceRenderer;
            material = instance;

            var go = new GameObject("PortalCamera");
            go.transform.SetParent(transform, false);
            viewCamera = go.AddComponent<Camera>();
            viewCamera.enabled = false;
            int surfaceLayer = LayerPortalSurface;
            // Don't draw portal surfaces inside portal views (no recursion).
            if (surfaceLayer >= 0) viewCamera.cullingMask = ~(1 << surfaceLayer);
        }

        void OnEnable()
        {
            views.Add(this);
            if (!hooked)
            {
                RenderPipelineManager.beginContextRendering += BeforeRender;
                hooked = true;
            }
        }

        void OnDisable() => views.Remove(this);

        void OnDestroy()
        {
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }
        }

        static void BeforeRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            var main = Camera.main;
            if (main == null) return;
            GeometryUtility.CalculateFrustumPlanes(main, frustum);
            for (int i = 0; i < views.Count; i++) views[i].UpdateView(main);
        }

        void Update()
        {
            if (surface == null) return;
            // While the local player is passing through, thicken the surface into a box
            // behind the opening, so the camera never clips past it into the wall.
            // Also when your camera is right at the opening: rendering runs faster than physics,
            // so the camera can reach the surface a frame or two before the pass registers.
            var local = GravityPlayerController.Local;
            bool passing = local != null && local.IsIgnoring(portal);
            var cam = Camera.main;
            if (!passing && cam != null)
            {
                Vector3 c = transform.InverseTransformPoint(cam.transform.position);
                passing = c.z < 0.8f && c.z > -0.9f
                    && Mathf.Abs(c.x) < portal.size.x * 0.5f + 0.1f && Mathf.Abs(c.y) < portal.size.y * 0.5f + 0.1f;
            }
            // Thicken instantly (any delay can show a clipped frame), thin out gently.
            thickness = passing ? 0.9f : Mathf.MoveTowards(thickness, 0.02f, Time.deltaTime * 4f);
            var t = surface.transform;
            t.localScale = new Vector3(portal.size.x, portal.size.y, thickness);
            t.localPosition = new Vector3(0f, 0f, 0.01f - thickness * 0.5f);
            material.SetFloat(ThickId, passing ? 1f : 0f);
        }

        void UpdateView(Camera main)
        {
            if (viewCamera == null || material == null) return;
            var linked = portal.linkedPortal;
            Vector3 toCamera = main.transform.position - transform.position;
            float distance = toCamera.magnitude;
            // Nearby portals keep rendering even when just out of view, so turning around or
            // stepping through never shows a stale frame (cameras switch on a frame late).
            bool visible = linked != null
                && Vector3.Dot(toCamera, portal.Normal) > -1f
                && distance < maxDistance
                && (distance < 6f || GeometryUtility.TestPlanesAABB(frustum, surface.bounds));

            viewCamera.enabled = visible;
            material.SetFloat(HasViewId, visible ? 1f : 0f);
            // Clean-look portals are always a clear opening; energy-look ones clear up as you approach.
            material.SetFloat(ClarityId, portal.CleanLook ? 1f : Mathf.Clamp01((6f - distance) / 4f));
            if (!visible) return;

            // Full resolution up close (or for doorways), cheaper further away. The hysteresis
            // keeps it from reallocating the texture every frame at the boundary.
            float wantedScale = portal.doorway || distance < (currentScale >= 1f ? 14f : 12f) ? 1f : resolutionScale;
            EnsureTarget(main, wantedScale);

            // Same view, seen through the linked portal.
            Matrix4x4 transfer = Portal.TransferMatrix(portal, linked);
            var ct = viewCamera.transform;
            ct.SetPositionAndRotation(transfer.MultiplyPoint3x4(main.transform.position),
                Portal.TransferRotation(portal, linked) * main.transform.rotation);

            viewCamera.fieldOfView = main.fieldOfView;
            viewCamera.nearClipPlane = main.nearClipPlane;
            viewCamera.farClipPlane = main.farClipPlane;
            viewCamera.aspect = main.aspect;
            viewCamera.clearFlags = main.clearFlags;
            viewCamera.backgroundColor = main.backgroundColor;
            viewCamera.depth = main.depth - 1f;

            // Tilt the near plane onto the linked portal's surface.
            Vector3 n = linked.Normal;
            Vector3 p = linked.transform.position;
            float side = Mathf.Sign(Vector3.Dot(n, p - ct.position));
            Matrix4x4 w2c = viewCamera.worldToCameraMatrix;
            Vector3 camPos = w2c.MultiplyPoint(p);
            Vector3 camNormal = w2c.MultiplyVector(n) * side;
            float camDist = -Vector3.Dot(camPos, camNormal) + 0.05f;
            viewCamera.projectionMatrix = Mathf.Abs(camDist) > 0.2f
                ? main.CalculateObliqueMatrix(new Vector4(camNormal.x, camNormal.y, camNormal.z, camDist))
                : main.projectionMatrix;
        }

        void EnsureTarget(Camera main, float scale)
        {
            currentScale = scale;
            int w = Mathf.Max(64, Mathf.RoundToInt(main.pixelWidth * scale));
            int h = Mathf.Max(64, Mathf.RoundToInt(main.pixelHeight * scale));
            if (target != null && target.width == w && target.height == h) return;

            if (target != null)
            {
                viewCamera.targetTexture = null;
                target.Release();
                Destroy(target);
            }
            target = new RenderTexture(w, h, 24, RenderTextureFormat.Default) { name = $"{portal.name} view" };
            target.Create();
            viewCamera.targetTexture = target;
            material.SetTexture(MainTexId, target);
        }
    }
}
