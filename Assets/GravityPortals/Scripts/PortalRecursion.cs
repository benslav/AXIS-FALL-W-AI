using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>
    /// Portals seen through portals (maps with <see cref="MapOptions.portalRecursion"/> above 1, The
    /// Tesseract): a door seen through a door shows its own room too, two or three levels deep.
    ///
    /// Every portal view (<see cref="PortalView"/>) is level 1. On top of that:
    ///  - Level 2: inside the biggest portal view on screen, every door that view can see gets its
    ///    own camera, placed where the level-1 camera would be if that door were an opening (the
    ///    same transfer again), with its near plane on the door's exit and its projection narrowed
    ///    to the door's rectangle of the level-1 view. Its picture goes on a second copy of the door's
    ///    surface, on its own layer, that only that level-1 camera draws.
    ///  - Level 3: the same again inside the biggest level-2 view.
    /// Deeper than that (or inside the smaller views, which don't recurse to keep it cheap), a door
    /// shows a flat fill in the colour of the room behind it, on a layer only portal cameras draw.
    ///
    /// The main camera draws none of the extra layers. Cameras render deepest first (by depth), so
    /// each level's picture is ready when the level above draws it.
    /// </summary>
    public static class PortalRecursion
    {
        public const string Layer2Name = "PortalSurface2", Layer3Name = "PortalSurface3", FillLayerName = "PortalFill";
        /// <summary>Views smaller than this share of the screen don't show deeper views (not worth the cost).</summary>
        const float MinCoverage = 0.01f;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int HasViewId = Shader.PropertyToID("_HasView");
        static readonly int ThickId = Shader.PropertyToID("_Thick");
        static readonly int ClarityId = Shader.PropertyToID("_Clarity");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        static readonly int ViewRectId = Shader.PropertyToID("_ViewRect");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>One portal's view at one deeper level.</summary>
        sealed class Nested
        {
            public Portal Portal;
            public int Level;
            public Camera Camera;
            public Renderer Surface;
            public Material Material;
            public RenderTexture Target;
            public float Coverage;      // share of the main screen
            public Matrix4x4 Crop;      // projection narrowing, all levels above included
            public Rect Rect;           // its rectangle of the parent's view
        }

        static readonly Dictionary<Portal, Nested[]> nested = new Dictionary<Portal, Nested[]>();
        static readonly HashSet<Portal> filled = new HashSet<Portal>();
        static readonly Plane[] planes = new Plane[6];
        static readonly List<Portal> gone = new List<Portal>();
        static bool anyOn;

        static int Layer(int level) => LayerMask.NameToLayer(level == 2 ? Layer2Name : Layer3Name);

        internal static void Update(Camera main, List<PortalView> views)
        {
            int levels = MapOptions.PortalRecursion;
            int l2 = Layer(2), l3 = Layer(3), fill = LayerMask.NameToLayer(FillLayerName);
            if (levels <= 1 || l2 < 0 || l3 < 0 || fill < 0)
            {
                if (anyOn) AllOff();
                return;
            }
            Forget();
            int extra = (1 << l2) | (1 << l3);
            main.cullingMask &= ~(extra | (1 << fill));
            foreach (var v in views) EnsureFill(v, fill);

            // Level 1: every view hides the deeper layers, except the biggest, which shows level 2.
            PortalView parent = null;
            float best = MinCoverage;
            foreach (var v in views)
            {
                if (v.ViewCamera == null) continue;
                v.ViewCamera.cullingMask &= ~extra;
                if (v.Wanted && v.Target != null && v.Coverage > best) { best = v.Coverage; parent = v; }
            }
            anyOn = true;
            if (parent == null) { LevelOff(2); LevelOff(3); return; }
            parent.ViewCamera.cullingMask |= 1 << l2;

            var sub = RenderLevel(main, 2, parent.ViewCamera, parent.Crop, parent.Target, parent.Owner.linkedPortal, parent.Coverage, levels >= 3);
            if (sub == null || levels < 3) { LevelOff(3); return; }
            sub.Camera.cullingMask |= 1 << l3;
            RenderLevel(main, 3, sub.Camera, sub.Crop, sub.Target, sub.Portal.linkedPortal, sub.Coverage, false);
        }

        /// <summary>Renders every portal the parent camera sees at this level; returns the biggest
        /// (the one to go a level deeper in), if asked for.</summary>
        static Nested RenderLevel(Camera main, int level, Camera parentCam, Matrix4x4 parentCrop, RenderTexture parentTarget,
            Portal parentExit, float parentCoverage, bool pickDeeper)
        {
            // What the parent sees: its narrowed view (without the tilted near plane, which skews the far plane).
            GeometryUtility.CalculateFrustumPlanes(parentCrop * main.projectionMatrix * parentCam.worldToCameraMatrix, planes);
            Vector3 eye = parentCam.transform.position;
            Nested deeper = null;
            float best = MinCoverage;
            foreach (var p in Portal.All)
            {
                var view = p != null ? p.GetComponent<PortalView>() : null;
                if (view == null || view.Surface == null) continue;
                var n = Get(p, view, level);
                bool on = p != parentExit && p.linkedPortal != null && !p.worldWrap
                    && Vector3.Dot(eye - p.transform.position, p.Normal) > 0f                       // facing the parent camera
                    && (parentExit == null || Vector3.Dot(p.transform.position - parentExit.transform.position, parentExit.Normal) > 0f) // beyond the parent's door
                    && Vector3.Distance(eye, p.transform.position) < PortalView.maxDistance
                    && GeometryUtility.TestPlanesAABB(planes, view.Surface.bounds);
                if (on)
                {
                    Measure(n, parentCam, parentCrop * main.projectionMatrix);
                    n.Coverage = parentCoverage * n.Rect.width * n.Rect.height;
                    Place(n, main, parentCam, parentCrop, level);
                }
                SetRendering(n, on, main, parentTarget);
                if (on && pickDeeper && n.Coverage > best) { best = n.Coverage; deeper = n; }
            }
            return deeper;
        }

        static Nested Get(Portal p, PortalView view, int level)
        {
            if (!nested.TryGetValue(p, out var pair)) nested[p] = pair = new Nested[2];
            ref var n = ref pair[level - 2];
            if (n != null && n.Camera != null) return n;

            n = new Nested { Portal = p, Level = level };
            // A second copy of the door's surface, on this level's layer.
            var src = view.Surface;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = $"Surface (level {level})";
            go.layer = Layer(level);
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(p.size.x, p.size.y, 0.02f);
            n.Material = new Material(view.SurfaceMaterial) { name = $"{p.name} surface (level {level})" };
            n.Material.SetFloat(ZTestId, (float)CompareFunction.LessEqual);
            n.Material.SetFloat(ThickId, 0f);
            n.Material.SetFloat(ClarityId, 1f);
            n.Material.SetFloat(HasViewId, 0f);
            n.Material.SetColor(BaseColorId, p.color);
            n.Material.SetColor(ColorId, p.color);
            n.Surface = go.GetComponent<Renderer>();
            n.Surface.sharedMaterial = n.Material;
            n.Surface.shadowCastingMode = ShadowCastingMode.Off;
            n.Surface.receiveShadows = false;
            n.Surface.enabled = false;

            var camGo = new GameObject($"PortalCamera (level {level})");
            camGo.transform.SetParent(p.transform, false);
            n.Camera = camGo.AddComponent<Camera>();
            n.Camera.enabled = false;
            PortalView.ConfigureCamera(n.Camera);
            n.Camera.allowMSAA = false;
            return n;
        }

        /// <summary>The door's rectangle of the parent camera's view.</summary>
        static void Measure(Nested n, Camera parentCam, Matrix4x4 parentProjection)
        {
            n.Rect = new Rect(0f, 0f, 1f, 1f);
            var p = n.Portal;
            if (Vector3.Distance(parentCam.transform.position, p.transform.position) < 4f) return;
            Matrix4x4 m = parentProjection * parentCam.worldToCameraMatrix;
            float hx = p.size.x * 0.5f, hy = p.size.y * 0.5f;
            float xMin = 1f, yMin = 1f, xMax = 0f, yMax = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = p.transform.TransformPoint(new Vector3(i % 2 == 0 ? -hx : hx, i < 2 ? -hy : hy, 0f));
                Vector4 clip = m * new Vector4(corner.x, corner.y, corner.z, 1f);
                if (clip.w <= 0.01f) return; // a corner behind the camera: use the whole view
                float x = (clip.x / clip.w + 1f) * 0.5f, y = (clip.y / clip.w + 1f) * 0.5f;
                xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x);
                yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
            }
            const float margin = 0.02f;
            xMin = Mathf.Clamp01(xMin - margin); yMin = Mathf.Clamp01(yMin - margin);
            xMax = Mathf.Clamp01(xMax + margin); yMax = Mathf.Clamp01(yMax + margin);
            if (xMax - xMin < 0.01f || yMax - yMin < 0.01f) { n.Rect = new Rect(xMin, yMin, 0.01f, 0.01f); return; }
            n.Rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>Puts the camera where the parent camera would be if this door were an opening,
        /// cut at the door's exit and narrowed to the door's rectangle (as PortalView does for level 1).</summary>
        static void Place(Nested n, Camera main, Camera parentCam, Matrix4x4 parentCrop, int level)
        {
            var p = n.Portal;
            var linked = p.linkedPortal;
            var cam = n.Camera;
            var ct = cam.transform;
            ct.SetPositionAndRotation(Portal.TransferMatrix(p, linked).MultiplyPoint3x4(parentCam.transform.position),
                Portal.TransferRotation(p, linked) * parentCam.transform.rotation);

            int surface = PortalView.LayerPortalSurface, l2 = Layer(2), l3 = Layer(3);
            int mask = ~0;
            if (surface >= 0) mask &= ~(1 << surface);
            mask &= ~((1 << l2) | (1 << l3));
            cam.cullingMask = mask; // the level below is added by the caller if this one goes deeper

            cam.fieldOfView = main.fieldOfView;
            cam.nearClipPlane = main.nearClipPlane;
            cam.farClipPlane = main.farClipPlane;
            cam.aspect = main.aspect;
            cam.clearFlags = main.clearFlags;
            cam.allowHDR = main.allowHDR;
            cam.backgroundColor = main.backgroundColor;
            cam.depth = main.depth - level; // deeper levels render first
            cam.rect = new Rect(0f, 0f, 1f, 1f);

            Vector3 normal = linked.Normal, point = linked.transform.position;
            float behind = Vector3.Dot(normal, point - ct.position);
            Matrix4x4 projection;
            if (behind <= 0f) projection = main.projectionMatrix;
            else
            {
                float push = Mathf.Max(0.004f, 0.03f - behind);
                Matrix4x4 w2c = cam.worldToCameraMatrix;
                Vector3 camPoint = w2c.MultiplyPoint(point + normal * push);
                Vector3 camNormal = w2c.MultiplyVector(normal);
                projection = main.CalculateObliqueMatrix(new Vector4(camNormal.x, camNormal.y, camNormal.z, -Vector3.Dot(camPoint, camNormal)));
            }

            // Narrowed to the door's rectangle of the parent's view, on top of the parent's own narrowing.
            Rect r = n.Rect;
            var crop = Matrix4x4.identity;
            if (r.width < 0.999f || r.height < 0.999f)
            {
                float cx = (r.xMin + r.xMax) - 1f, cy = (r.yMin + r.yMax) - 1f;
                crop.m00 = 1f / r.width;
                crop.m11 = 1f / r.height;
                crop.m03 = -cx / r.width;
                crop.m13 = -cy / r.height;
            }
            n.Crop = crop * parentCrop;
            cam.projectionMatrix = n.Crop * projection;
        }

        static void SetRendering(Nested n, bool on, Camera main, RenderTexture parentTarget)
        {
            if (n.Target != null)
            {
                n.Camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(n.Target);
                n.Target = null;
            }
            if (on && parentTarget != null)
            {
                int w = RoundUp(parentTarget.width * n.Rect.width * PortalView.resolutionScale);
                int h = RoundUp(parentTarget.height * n.Rect.height * PortalView.resolutionScale);
                n.Target = RenderTexture.GetTemporary(w, h, 24, main.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);
                n.Target.name = $"{n.Portal.name} view (level {n.Level})";
                n.Camera.targetTexture = n.Target;
                n.Material.SetTexture(MainTexId, n.Target);
                n.Material.SetVector(ViewRectId, new Vector4(n.Rect.x, n.Rect.y, n.Rect.width, n.Rect.height));
            }
            else on = false;
            bool renderedBefore = n.Camera.enabled;
            n.Camera.enabled = on;
            n.Material.SetFloat(HasViewId, on && renderedBefore ? 1f : 0f);
            // Only drawn while it has a view; otherwise the parent sees the door's flat fill.
            n.Surface.enabled = on;
        }

        static int RoundUp(float pixels) => Mathf.Max(32, Mathf.CeilToInt(pixels / 32f) * 32);

        /// <summary>The flat fill a portal camera sees on a door it doesn't render deeper: the colour
        /// of the room behind it. Just in front of the wall, behind the door's surfaces.</summary>
        static void EnsureFill(PortalView view, int layer)
        {
            var p = view.Owner;
            if (p == null || view.Surface == null || filled.Contains(p)) return;
            filled.Add(p);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Fill (portal views only)";
            go.layer = layer;
            go.transform.SetParent(view.Surface.transform.parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // a quad faces -Z; this one faces out of the portal
            go.transform.localScale = new Vector3(p.size.x, p.size.y, 1f);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = p.visualMaterial != null ? new Material(p.visualMaterial) : new Material(shader);
            Color c = p.color * 0.55f;
            c.a = 1f;
            mat.SetColor(BaseColorId, c);
            mat.SetColor(ColorId, c);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static void LevelOff(int level)
        {
            foreach (var pair in nested.Values)
            {
                var n = pair[level - 2];
                if (n != null && n.Camera != null) SetRendering(n, false, null, null);
            }
        }

        static void AllOff()
        {
            LevelOff(2);
            LevelOff(3);
            anyOn = false;
        }

        /// <summary>Drops entries for portals that are gone (a new map loaded).</summary>
        static void Forget()
        {
            gone.Clear();
            foreach (var kv in nested) if (kv.Key == null) gone.Add(kv.Key);
            foreach (var p in gone)
            {
                foreach (var n in nested[p])
                    if (n != null && n.Target != null) RenderTexture.ReleaseTemporary(n.Target);
                nested.Remove(p);
            }
            filled.RemoveWhere(p => p == null);
        }
    }
}
