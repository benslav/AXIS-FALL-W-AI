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
            var local = GravityPlayerController.Local;
            bool passing = local != null && local.IsIgnoring(portal);
            float wanted = passing ? 0.9f : 0.02f;
            thickness = Mathf.MoveTowards(thickness, wanted, Time.deltaTime * 20f);
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
            bool visible = linked != null
                && Vector3.Dot(toCamera, portal.Normal) > -1f
                && toCamera.sqrMagnitude < maxDistance * maxDistance
                && GeometryUtility.TestPlanesAABB(frustum, surface.bounds);

            viewCamera.enabled = visible;
            material.SetFloat(HasViewId, visible ? 1f : 0f);
            if (!visible) return;

            EnsureTarget(main);

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

        void EnsureTarget(Camera main)
        {
            int w = Mathf.Max(64, Mathf.RoundToInt(main.pixelWidth * resolutionScale));
            int h = Mathf.Max(64, Mathf.RoundToInt(main.pixelHeight * resolutionScale));
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
