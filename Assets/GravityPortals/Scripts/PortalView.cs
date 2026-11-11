using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
    /// Keeping it cheap, so every portal in view can show its view:
    /// - only portals in view, in range and not hidden behind walls render;
    /// - each renders only the rectangle of the screen its portal covers (the projection is
    ///   narrowed to it, so culling skips everything outside), into a texture just that size,
    ///   at lower resolution when it's small or far away;
    /// - portal cameras skip shadows, depth/colour copies and post-processing, and small
    ///   portals skip anti-aliasing.
    /// </summary>
    [RequireComponent(typeof(Portal))]
    public class PortalView : MonoBehaviour
    {
        public static float resolutionScale = 0.7f; // for small or distant portals
        public static float maxDistance = 80f;

        static readonly List<PortalView> views = new List<PortalView>();
        static readonly Plane[] frustum = new Plane[6];
        static bool hooked;
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int HasViewId = Shader.PropertyToID("_HasView");
        static readonly int ThickId = Shader.PropertyToID("_Thick");
        static readonly int ClarityId = Shader.PropertyToID("_Clarity");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        static readonly int ViewRectId = Shader.PropertyToID("_ViewRect");

        Portal portal;
        Camera viewCamera;
        RenderTexture target;   // temporary texture, re-fetched each frame at the size needed
        Material material;
        Renderer surface;
        float thickness = 0.02f;
        int surfaceLayer = -1;
        bool drawOnTop;

        bool wanted;            // should render this frame
        float coverage;         // share of the screen the portal covers (1 = up close)
        Rect screenRect;        // the part of the screen it covers (viewport coordinates)
        float distance;
        float lastSeenTime = -100f; // last time a line of sight to the portal was clear

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
            surfaceLayer = LayerPortalSurface;
            // Don't draw portal surfaces inside portal views (no recursion).
            if (surfaceLayer >= 0) viewCamera.cullingMask = ~(1 << surfaceLayer);

            var data = viewCamera.GetUniversalAdditionalCameraData();
            // No post-processing here: the main camera applies it to the portal surface along
            // with everything else, so the view through a portal looks exactly like the real
            // thing once you step through (no colour or brightness pop).
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            // Nothing a portal view needs: skip the work.
            data.renderShadows = false;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.volumeLayerMask = 0;
            data.stopNaN = false;
            data.dithering = false;
            viewCamera.useOcclusionCulling = false;
            material.SetVector(ViewRectId, new Vector4(0f, 0f, 1f, 1f));
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

        void OnDisable()
        {
            views.Remove(this);
            ReleaseTarget();
        }

        void OnDestroy() => ReleaseTarget();

        static void BeforeRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            var main = Camera.main;
            if (main == null) return;
            GeometryUtility.CalculateFrustumPlanes(main, frustum);
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                v.UpdateView(main);
                v.SetRendering(v.wanted, main);
            }
        }

        void SetRendering(bool on, Camera main)
        {
            // Last frame's texture goes back to the pool; a fresh one sized for this frame
            // (often the very same texture) is taken below.
            ReleaseTarget();
            if (on)
            {
                // Smaller texture for small or far portals; full detail up close and for
                // anything big on screen. Sizes are rounded up so the same pooled texture
                // keeps getting reused as you move.
                float scale = distance < 14f || coverage > 0.12f ? 1f : resolutionScale;
                int w = RoundUp(main.pixelWidth * screenRect.width * scale);
                int h = RoundUp(main.pixelHeight * screenRect.height * scale);
                var format = main.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
                target = RenderTexture.GetTemporary(w, h, 24, format);
                target.name = $"{portal.name} view";
                viewCamera.targetTexture = target;
                // Anti-aliasing only where it shows.
                viewCamera.allowMSAA = main.allowMSAA && coverage > 0.04f;
                material.SetTexture(MainTexId, target);
                material.SetVector(ViewRectId, new Vector4(screenRect.x, screenRect.y, screenRect.width, screenRect.height));
            }

            // A camera switched on now first renders next frame, so until then show the plain
            // surface rather than an empty texture.
            bool renderedBefore = viewCamera.enabled;
            viewCamera.enabled = on;
            material.SetFloat(HasViewId, on && renderedBefore ? 1f : 0f);
        }

        static int RoundUp(float pixels) => Mathf.Max(32, Mathf.CeilToInt(pixels / 32f) * 32);

        void ReleaseTarget()
        {
            if (target == null) return;
            if (viewCamera != null) viewCamera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            target = null;
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
            wanted = false;
            if (viewCamera == null || material == null) return;
            var linked = portal.linkedPortal;
            Vector3 toCamera = main.transform.position - transform.position;
            distance = toCamera.magnitude;
            // Nearby portals keep rendering even when just out of view, so turning around or
            // stepping through never shows a stale frame (cameras switch on a frame late).
            bool visible = linked != null
                && Vector3.Dot(toCamera, portal.Normal) > -1f
                && distance < maxDistance
                && (distance < 6f || (GeometryUtility.TestPlanesAABB(frustum, surface.bounds) && InSight(main)));
            wanted = visible;
            if (visible) MeasureOnScreen(main);

            // The last few centimetres before you cross: your camera can't draw anything closer
            // than its near clip distance, so the portal surface gets clipped a moment before
            // the wall or door it's mounted on does, and that wall flashes into view. While your
            // eye is right at the opening, draw the portal on top of whatever is behind it.
            // (Done here, with the camera's final position for this frame.)
            Vector3 eye = transform.InverseTransformPoint(main.transform.position);
            bool atOpening = eye.z < 0.15f && eye.z > -0.9f
                && Mathf.Abs(eye.x) < portal.size.x * 0.5f + 0.1f && Mathf.Abs(eye.y) < portal.size.y * 0.5f + 0.1f;
            if (atOpening != drawOnTop)
            {
                drawOnTop = atOpening;
                material.SetFloat(ZTestId, (float)(atOpening ? CompareFunction.Always : CompareFunction.LessEqual));
            }
            // Clean-look portals are always a clear opening; energy-look ones clear up as you approach.
            material.SetFloat(ClarityId, portal.CleanLook ? 1f : Mathf.Clamp01((6f - distance) / 4f));
            if (!visible) return;

            // Same view, seen through the linked portal.
            Matrix4x4 transfer = Portal.TransferMatrix(portal, linked);
            var ct = viewCamera.transform;
            ct.SetPositionAndRotation(transfer.MultiplyPoint3x4(main.transform.position),
                Portal.TransferRotation(portal, linked) * main.transform.rotation);

            // Portal views show your own body (so you can see yourself through a portal), except
            // when this camera is right where your head is: e.g. just after dropping through a
            // floor portal, looking back through the entrance, it would see your body from inside.
            int mask = surfaceLayer >= 0 ? ~(1 << surfaceLayer) : ~0;
            var me = GravityPlayerController.Local;
            int localLayer = LayerLocalPlayer;
            if (me != null && localLayer >= 0 && (ct.position - me.transform.position).sqrMagnitude < 1.5f * 1.5f)
                mask &= ~(1 << localLayer);
            viewCamera.cullingMask = mask;

            viewCamera.fieldOfView = main.fieldOfView;
            viewCamera.nearClipPlane = main.nearClipPlane;
            viewCamera.farClipPlane = main.farClipPlane;
            viewCamera.aspect = main.aspect;
            viewCamera.clearFlags = main.clearFlags;
            viewCamera.allowHDR = main.allowHDR;
            viewCamera.backgroundColor = main.backgroundColor;
            viewCamera.depth = main.depth - 1f;
            viewCamera.rect = new Rect(0f, 0f, 1f, 1f); // the texture is exactly the portal's rectangle

            // Tilt the near plane onto the linked portal's surface, so nothing behind that surface
            // (the wall it's on, door posts, screens) can ever show in the view.
            Vector3 n = linked.Normal;                 // points out of the exit, into its room
            Vector3 p = linked.transform.position;
            float behind = Vector3.Dot(n, p - ct.position);
            Matrix4x4 projection;
            if (behind <= 0f)
            {
                // You've already crossed the entrance (the pass registers on the next physics
                // step), so this camera is in the exit's room: nothing to cut away.
                projection = main.projectionMatrix;
            }
            else
            {
                // Cut exactly at the surface, nudged a few millimetres into the room so the wall face
                // itself never peeks through. Never switch the cut off up close (that let the wall
                // behind the exit show for a frame as you stepped in): just keep it at least 3 cm
                // from the camera, where the projection stays well behaved.
                float push = Mathf.Max(0.004f, 0.03f - behind);
                Matrix4x4 w2c = viewCamera.worldToCameraMatrix;
                Vector3 camPoint = w2c.MultiplyPoint(p + n * push);
                Vector3 camNormal = w2c.MultiplyVector(n);
                projection = main.CalculateObliqueMatrix(
                    new Vector4(camNormal.x, camNormal.y, camNormal.z, -Vector3.Dot(camPoint, camNormal)));
            }

            // Narrow the projection to the portal's rectangle of the screen, stretched over the
            // whole texture. The surface shader maps screen positions into that rectangle, so it
            // still lines up exactly; and culling skips everything outside it.
            Rect r = screenRect;
            if (r.width < 0.999f || r.height < 0.999f)
            {
                float cx = (r.xMin + r.xMax) - 1f, cy = (r.yMin + r.yMax) - 1f; // centre in NDC
                var crop = Matrix4x4.identity;
                crop.m00 = 1f / r.width;
                crop.m11 = 1f / r.height;
                crop.m03 = -cx / r.width;
                crop.m13 = -cy / r.height;
                projection = crop * projection;
            }
            viewCamera.projectionMatrix = projection;
        }

        /// <summary>Is any part of the opening in clear sight of the camera? Five quick line
        /// checks (centre and corners). A portal that's just been in sight stays "in sight" a
        /// moment longer, so it never pops in late as you come round a corner.</summary>
        bool InSight(Camera main)
        {
            Vector3 from = main.transform.position;
            float hx = portal.size.x * 0.42f, hy = portal.size.y * 0.42f;
            for (int i = 0; i < 5; i++)
            {
                Vector3 local = i == 0 ? Vector3.zero : new Vector3(i % 2 == 0 ? -hx : hx, i < 3 ? -hy : hy, 0f);
                Vector3 to = transform.TransformPoint(local) + portal.Normal * 0.05f;
                if (!Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                    || hit.collider.attachedRigidbody != null) // players and crates don't hide a portal
                {
                    lastSeenTime = Time.time;
                    return true;
                }
            }
            return Time.time - lastSeenTime < 0.3f;
        }

        /// <summary>How much of the screen this portal covers, and where. Up close (or when a
        /// corner is behind the camera) it's treated as the whole screen.</summary>
        void MeasureOnScreen(Camera main)
        {
            screenRect = new Rect(0f, 0f, 1f, 1f);
            coverage = 1f;
            if (distance < 4f) return; // close enough to fill the view (and the surface may be thickened)

            float hx = portal.size.x * 0.5f, hy = portal.size.y * 0.5f;
            float xMin = 1f, yMin = 1f, xMax = 0f, yMax = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = transform.TransformPoint(new Vector3(i % 2 == 0 ? -hx : hx, i < 2 ? -hy : hy, 0f));
                Vector3 v = main.WorldToViewportPoint(corner);
                if (v.z <= main.nearClipPlane) return; // a corner behind us: just use the whole screen
                xMin = Mathf.Min(xMin, v.x); xMax = Mathf.Max(xMax, v.x);
                yMin = Mathf.Min(yMin, v.y); yMax = Mathf.Max(yMax, v.y);
            }
            const float margin = 0.02f;
            xMin = Mathf.Clamp01(xMin - margin); yMin = Mathf.Clamp01(yMin - margin);
            xMax = Mathf.Clamp01(xMax + margin); yMax = Mathf.Clamp01(yMax + margin);
            xMax = Mathf.Max(xMax, xMin + 0.01f); yMax = Mathf.Max(yMax, yMin + 0.01f);
            if (xMax > 1f) { xMin -= xMax - 1f; xMax = 1f; }
            if (yMax > 1f) { yMin -= yMax - 1f; yMax = 1f; }
            screenRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            coverage = screenRect.width * screenRect.height;
        }
    }
}
