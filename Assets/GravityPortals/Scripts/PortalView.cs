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
    /// Only portals actually in view (and in front of the camera) render, at a reduced
    /// resolution, to keep the cost down.
    /// </summary>
    [RequireComponent(typeof(Portal))]
    public class PortalView : MonoBehaviour
    {
        public static float resolutionScale = 0.75f;
        public static float maxDistance = 80f;
        /// <summary>Most portal views rendered in one frame; the biggest on screen win, the rest
        /// show the plain portal surface (as portals beyond maxDistance do).</summary>
        public static int maxViews = 8;
        /// <summary>A view's texture is freed after this long out of sight (seconds).</summary>
        const float FreeAfter = 3f;

        static readonly List<PortalView> candidates = new List<PortalView>();
        bool wanted;            // in view this frame (before the budget)
        bool rendering;         // got a camera this frame
        float coverage;         // share of the screen the portal covers (1 = up close)
        Rect screenRect;        // the part of the screen it covers (viewport coordinates)
        float lastVisibleTime = -100f;
        float pendingScale = 1f;

        static readonly List<PortalView> views = new List<PortalView>();
        static readonly Plane[] frustum = new Plane[6];
        static bool hooked;
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int HasViewId = Shader.PropertyToID("_HasView");
        static readonly int ThickId = Shader.PropertyToID("_Thick");
        static readonly int ClarityId = Shader.PropertyToID("_Clarity");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        bool drawOnTop;
        float currentScale = -1f;
        bool targetIsHdr;

        Portal portal;
        Camera viewCamera;
        RenderTexture target;
        Material material;
        Renderer surface;
        float thickness = 0.02f;
        int surfaceLayer = -1;

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
            // No post-processing here: the main camera applies it to the portal surface along
            // with everything else, so the view through a portal looks exactly like the real
            // thing once you step through (no colour or brightness pop).
            viewCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
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
            candidates.Clear();
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                v.UpdateView(main);
                if (v.wanted) candidates.Add(v);
            }

            // Budget: only the portals that matter most on screen get a camera. Ones already
            // rendering get a head start, so two similar portals don't swap back and forth.
            candidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            for (int i = 0; i < candidates.Count; i++)
            {
                bool on = i < maxViews;
                if (on) candidates[i].EnsureTarget(main, candidates[i].pendingScale);
                candidates[i].SetRendering(on);
            }
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (!v.wanted) v.SetRendering(false);
                if (!v.rendering && v.target != null && Time.time - v.lastVisibleTime > FreeAfter) v.FreeTarget();
            }
        }

        float Priority => coverage * (rendering ? 1.5f : 1f);

        void SetRendering(bool on)
        {
            rendering = on;
            // A camera switched on now first renders next frame, so until then show the plain
            // surface rather than an empty or stale texture.
            bool renderedBefore = viewCamera.enabled;
            viewCamera.enabled = on;
            material.SetFloat(HasViewId, on && renderedBefore && target != null ? 1f : 0f);
            if (on) lastVisibleTime = Time.time;
        }

        void FreeTarget()
        {
            viewCamera.targetTexture = null;
            material.SetTexture(MainTexId, null);
            target.Release();
            Destroy(target);
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
            float distance = toCamera.magnitude;
            // Nearby portals keep rendering even when just out of view, so turning around or
            // stepping through never shows a stale frame (cameras switch on a frame late).
            bool visible = linked != null
                && Vector3.Dot(toCamera, portal.Normal) > -1f
                && distance < maxDistance
                && (distance < 6f || GeometryUtility.TestPlanesAABB(frustum, surface.bounds));

            wanted = visible;
            if (visible) MeasureOnScreen(main, distance);

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

            // Full resolution up close (or for doorways), cheaper further away. The hysteresis
            // keeps it from reallocating the texture every frame at the boundary.
            // (The texture itself is made only if this portal wins a camera; see BeforeRender.)
            pendingScale = portal.doorway || distance < (currentScale >= 1f ? 14f : 12f) ? 1f : resolutionScale;

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
            viewCamera.allowMSAA = main.allowMSAA;
            viewCamera.backgroundColor = main.backgroundColor;
            viewCamera.depth = main.depth - 1f;

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

            // Render only the part of the screen the portal covers: the projection is narrowed
            // to that rectangle (so culling skips everything outside it) and drawn into the same
            // pixels, so it still lines up exactly with the screen-space surface.
            Rect r = screenRect;
            viewCamera.rect = r;
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

        /// <summary>How much of the screen this portal covers, and where. Up close (or when a
        /// corner is behind the camera) it's treated as the whole screen.</summary>
        void MeasureOnScreen(Camera main, float distance)
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
            if (xMax - xMin < 0.005f || yMax - yMin < 0.005f) { coverage = 0f; screenRect = new Rect(xMin, yMin, 0.005f, 0.005f); return; }
            screenRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            coverage = screenRect.width * screenRect.height;
        }

        void EnsureTarget(Camera main, float scale)
        {
            currentScale = scale;
            int w = Mathf.Max(64, Mathf.RoundToInt(main.pixelWidth * scale));
            int h = Mathf.Max(64, Mathf.RoundToInt(main.pixelHeight * scale));
            // Match the main camera's colour range so the view through the portal and the view
            // after stepping through are indistinguishable. (Anti-aliasing comes from the camera
            // itself, allowMSAA above; URP resolves it before writing into this texture.)
            bool hdr = main.allowHDR;
            // Compare with what we asked for last time: Unity reports the concrete format it
            // picked (e.g. ARGBHalf), never "DefaultHDR", so comparing target.format would
            // recreate the texture every frame and the portal would only ever show black.
            if (target != null && target.width == w && target.height == h && targetIsHdr == hdr) return;

            if (target != null)
            {
                viewCamera.targetTexture = null;
                target.Release();
                Destroy(target);
            }
            var format = hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
            target = new RenderTexture(w, h, 24, format) { name = $"{portal.name} view" };
            target.Create();
            targetIsHdr = hdr;
            viewCamera.targetTexture = target;
            material.SetTexture(MainTexId, target);
        }
    }
}
