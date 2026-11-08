using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>
    /// One side of a portal pair, sitting flat on a surface (floor, wall, ceiling, slope).
    /// Local axes: forward (+Z) = surface normal pointing out into the room, X/Y = the
    /// portal's width/height on the surface.
    ///
    /// How passing through works:
    ///  1. When a traveller's center is over the portal and close to the surface, the
    ///     surface ("host" colliders) stops colliding with that traveller, so it can
    ///     sink into the floor / walk into the wall.
    ///  2. The moment its center crosses the surface plane it is transferred to the
    ///     linked portal, keeping momentum, and its gravity points into the exit surface.
    ///  3. The exit portal stays solid for that traveller until it leaves the exit zone,
    ///     so you land on the exit portal instead of falling straight back in.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Portal : MonoBehaviour
    {
        static readonly Quaternion HalfTurn = Quaternion.Euler(0f, 180f, 0f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Raised after any traveller passes through any portal (traveller, from, to).</summary>
        public static event Action<PortalTraveller, Portal, Portal> Teleported;

        static readonly Dictionary<int, Portal> registry = new Dictionary<int, Portal>();

        [Tooltip("Unique id, used to name portals over the network.")]
        public int portalId = -1;
        [Tooltip("Where travellers come out.")]
        public Portal linkedPortal;
        public Color color = new Color(0f, 0.85f, 1f);
        public Vector2 size = new Vector2(3f, 3f);
        [Tooltip("How far in front of the surface the detection zone reaches.")]
        public float triggerFront = 2.5f;
        [Tooltip("How far behind the surface the detection zone reaches (catches fast movers).")]
        public float triggerBack = 2f;
        [Tooltip("Unlit material used for the portal's frame.")]
        public Material visualMaterial;
        [Tooltip("Material (GravityPortals/PortalView) for the surface that shows the other side.")]
        public Material viewMaterial;
        [Tooltip("Walk-through doorway (quieter whoosh, always full-resolution view).")]
        public bool doorway;
        [Tooltip("Old energy look: thick pulsing frame, corner posts and a shimmering surface. Off = a clean opening with a thin colored edge.")]
        public bool energyEffects;

        /// <summary>Clean look: thin steady edge, no posts, no shimmer.</summary>
        public bool CleanLook => doorway || !energyEffects;

        [Tooltip("Invisible wall of a wrap-around map: no visuals, no rendered view, no effects. " +
                 "Falling through these over and over counts as falling into the void.")]
        public bool worldWrap;

        readonly List<Collider> hosts = new List<Collider>();
        readonly List<PortalTraveller> travellers = new List<PortalTraveller>();

        Renderer surfaceRenderer;
        readonly List<Renderer> frameRenderers = new List<Renderer>();
        readonly List<Transform> posts = new List<Transform>();
        MaterialPropertyBlock block;
        float phase;
        float lastPassFx = -10f;

        public Vector3 Normal => transform.forward;
        public IReadOnlyList<Collider> Hosts => hosts;
        public bool HasHost(Collider c) => hosts.Contains(c);

        void Awake()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, triggerFront + triggerBack);
            box.center = new Vector3(0f, 0f, (triggerFront - triggerBack) * 0.5f);
            phase = UnityEngine.Random.value * 10f;
            BuildVisuals();
        }

        void Start() => FindHosts();

        // ------------------------------------------------------------------ exit gravity

        /// <summary>Gravity within this many degrees of a world axis snaps onto it (degrees).</summary>
        public const float AlignedTolerance = 30f;

        /// <summary>
        /// The gravity a traveller has after passing from <paramref name="from"/> to <paramref name="to"/>:
        /// the direction you were falling carries straight through the portal.
        ///
        ///  - Drop into a floor portal: you were falling "into" it, so you keep falling out of
        ///    the exit, away from it, and land on the surface the exit portal looks at (exactly
        ///    what you saw through the portal). You come out already upright, so no flip.
        ///  - Walk through a wall portal like a door: you come out upright relative to the exit
        ///    doorway (which is how a door can lead into a world with a different "down").
        ///
        /// Directions within 30 degrees of an axis snap to it (e.g. the tilted ramp portal), and
        /// the camera eases the difference. Deterministic, so the server can check it.
        /// </summary>
        public static Vector3 GravityAfterPass(Portal from, Portal to, Vector3 gravityBefore)
        {
            Vector3 carriedGravity = TransferRotation(from, to) * gravityBefore.normalized;
            return SnapToAxis(carriedGravity, AlignedTolerance);
        }

        /// <summary>Removes tiny rounding drift so gravity stays exactly on an axis when it should.</summary>
        static Vector3 SnapToAxis(Vector3 v, float maxAngle)
        {
            Vector3 best = v.normalized;
            float bestAngle = maxAngle;
            foreach (var axis in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                float a = Vector3.Angle(v, axis);
                if (a <= bestAngle) { bestAngle = a; best = axis; }
            }
            return best;
        }

        void OnEnable()
        {
            if (portalId >= 0) registry[portalId] = this;
        }

        void OnDisable()
        {
            if (portalId >= 0 && registry.TryGetValue(portalId, out var p) && p == this) registry.Remove(portalId);
        }

        public static Portal FindById(int id) => registry.TryGetValue(id, out var p) ? p : null;
        public static IEnumerable<Portal> All => registry.Values;

        /// <summary>Ray vs this portal's opening, from the front only. Returns the hit distance or -1.</summary>
        public float RaycastOpening(Vector3 origin, Vector3 direction)
        {
            float denom = Vector3.Dot(direction, Normal);
            if (denom > -1e-4f) return -1f; // parallel or coming from behind
            float t = Vector3.Dot(transform.position - origin, Normal) / denom;
            if (t < 0f) return -1f;
            Vector3 local = transform.InverseTransformPoint(origin + direction * t);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f ? t : -1f;
        }

        /// <summary>How far a point is outside this portal's detection zone (0 = inside).
        /// The server uses it to check that a claimed teleport really happened here.</summary>
        public float DistanceOutsideZone(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 clamped = new Vector3(
                Mathf.Clamp(local.x, -size.x * 0.5f, size.x * 0.5f),
                Mathf.Clamp(local.y, -size.y * 0.5f, size.y * 0.5f),
                Mathf.Clamp(local.z, -triggerBack, triggerFront));
            return Vector3.Distance(local, clamped);
        }

        /// <summary>Finds the surface colliders this portal is mounted on.</summary>
        public void FindHosts()
        {
            hosts.Clear();
            var found = Physics.OverlapBox(
                transform.position - Normal * 0.1f,
                new Vector3(size.x * 0.45f, size.y * 0.45f, 0.08f),
                transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in found)
                if (c.attachedRigidbody == null || c.attachedRigidbody.isKinematic)
                    hosts.Add(c);
            if (hosts.Count == 0 && !worldWrap) // wrap walls are open air on purpose
                Debug.LogWarning($"[Portal] '{name}' is not mounted on any surface; travellers can't pass into it.", this);
        }

        void OnTriggerEnter(Collider other)
        {
            var t = TravellerOf(other);
            if (t != null && !travellers.Contains(t)) travellers.Add(t);
        }

        void OnTriggerExit(Collider other)
        {
            var t = TravellerOf(other);
            if (t == null) return;
            travellers.Remove(t);
            t.SetPortalIgnore(this, false);
            if (t.ArrivalPortal == this) t.ArrivalPortal = null;
        }

        static PortalTraveller TravellerOf(Collider c) =>
            c.attachedRigidbody != null ? c.attachedRigidbody.GetComponent<PortalTraveller>() : null;

        void FixedUpdate()
        {
            if (linkedPortal == null) return;
            for (int i = travellers.Count - 1; i >= 0; i--)
            {
                var t = travellers[i];
                if (t == null) { travellers.RemoveAt(i); continue; }
                if (Process(t)) travellers.RemoveAt(i);
            }
        }

        /// <summary>Returns true if the traveller was sent through.</summary>
        bool Process(PortalTraveller t)
        {
            if (!t.SimulatesLocally) return false;

            Vector3 local = transform.InverseTransformPoint(t.Center);

            // Just came out of here. You arrive at exactly the mirrored spot, which can be
            // partly "inside" this surface, so keep the surface open until you've stepped
            // fully out. (No push = no snap.) Your fall direction carries through, so you
            // always move away from the exit and can't loop.
            if (t.ArrivalPortal == this)
            {
                float clear = t.ExtentAlong(Normal, t.Body.rotation) + 0.05f;
                bool inOpening = Mathf.Abs(local.x) <= size.x * 0.5f + 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f + 0.5f;
                if (inOpening && local.z < clear && local.z > -0.15f)
                {
                    t.SetPortalIgnore(this, true);
                    return false;
                }
                // Stepped fully out (or turned around and walked back in): an ordinary portal again.
                t.ArrivalPortal = null;
            }

            bool overPortal = Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f;
            // Reach further for fast movers, so a projectile can't hit the wall in the
            // one physics step before the portal opens the surface for it.
            float approachSpeed = Mathf.Max(0f, -Vector3.Dot(t.Velocity, Normal));
            float reach = t.ExtentAlong(Normal, t.Body.rotation) + 0.25f + approachSpeed * Time.fixedDeltaTime * 2f;

            // Only open for things arriving from the front: something already behind the
            // surface (e.g. walking past the back of a free-standing doorway) is left alone.
            if (overPortal && local.z < reach && local.z >= 0f) t.SetPortalIgnore(this, true);
            else if (!overPortal && local.z > 0f) t.SetPortalIgnore(this, false);

            // Center crossed the surface while passing in: send it through. Anything that
            // got behind the surface while passing in is always sent, so it can't get stuck.
            if (t.IsIgnoring(this) && local.z < 0f)
            {
                if (Time.time - t.LastTeleportTime < 0.05f) return false;
                t.Teleport(this, linkedPortal);
                return true;
            }
            return false;
        }

        /// <summary>Keeps an arriving traveller inside the exit opening and on its front side.
        /// It is NOT pushed clear of the surface: you arrive exactly where you'd be if the two
        /// portals were one opening, and the surface stays open while you step out.</summary>
        public Vector3 ClampExitPosition(Vector3 worldPosition, float extentAlongNormal)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            local.x = Mathf.Clamp(local.x, -size.x * 0.5f, size.x * 0.5f);
            local.y = Mathf.Clamp(local.y, -size.y * 0.5f, size.y * 0.5f);
            local.z = Mathf.Max(local.z, 0.01f);
            return transform.TransformPoint(local);
        }

        /// <summary>Maps a point/direction in front of portal A to the matching one in front of portal B.
        /// Going in through A's front comes out of B's front (hence the half turn).</summary>
        public static Matrix4x4 TransferMatrix(Portal from, Portal to) =>
            to.transform.localToWorldMatrix * Matrix4x4.Rotate(HalfTurn) * from.transform.worldToLocalMatrix;

        public static Quaternion TransferRotation(Portal from, Portal to) =>
            to.transform.rotation * HalfTurn * Quaternion.Inverse(from.transform.rotation);

        internal static void RaiseTeleported(PortalTraveller t, Portal from, Portal to)
        {
            if (from.worldWrap || to.worldWrap)
            {
                // Wrapping round the map should be invisible: no sparks, no sound.
                Teleported?.Invoke(t, from, to);
                return;
            }
            // Your own pass stays visually clean (just the whoosh); others see the sparks.
            bool self = t == GravityPlayerController.Local;
            from.PlayPassFx(from.transform.position, sparks: !self);
            to.PlayPassFx(t.Center, sparks: !self);
            Teleported?.Invoke(t, from, to);
        }

        /// <summary>Burst of energy and a whoosh where something went through.</summary>
        public void PlayPassFx(Vector3 point, bool sparks = true)
        {
            if (Time.time - lastPassFx < 0.08f || Application.isBatchMode) return;
            lastPassFx = Time.time;
            if (!sparks)
            {
                Sfx.Play3D(SoundBank.PortalWhoosh, point, doorway ? 0.3f : 0.6f, UnityEngine.Random.Range(0.9f, 1.1f));
                return;
            }
            Vector3 center = transform.position + Normal * 0.05f;
            for (int i = 0; i < 14; i++)
            {
                // Sparks fly out along the surface and away from it.
                Vector2 ring = UnityEngine.Random.insideUnitCircle.normalized;
                Vector3 along = transform.right * ring.x * size.x * 0.5f + transform.up * ring.y * size.y * 0.5f;
                Vector3 velocity = (along.normalized * UnityEngine.Random.Range(1f, 3f) + Normal * UnityEngine.Random.Range(1.5f, 4f));
                CombatFx.Spark(center + along * UnityEngine.Random.Range(0.6f, 1f), velocity, color, UnityEngine.Random.Range(0.3f, 0.6f));
            }
            Sfx.Play3D(SoundBank.PortalWhoosh, point, 0.8f, UnityEngine.Random.Range(0.9f, 1.1f));
        }

        // ---------------------------------------------------------------- visuals

        void BuildVisuals()
        {
            if (worldWrap) return; // invisible: the map's repeating copies show what's beyond
            var mat = visualMaterial != null ? visualMaterial : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            block = new MaterialPropertyBlock();
            var root = new GameObject("Visuals").transform;
            root.SetParent(transform, false);

            // The surface is a very thin box (it thickens while you pass through, see PortalView).
            var viewShader = viewMaterial != null ? viewMaterial.shader : Shader.Find("GravityPortals/PortalView");
            bool canRender = viewShader != null && viewShader.isSupported && !Application.isBatchMode
                && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (canRender)
            {
                var surfaceMat = viewMaterial != null ? new Material(viewMaterial) : new Material(viewShader);
                surfaceMat.name = $"{name} surface";
                surfaceRenderer = Part(PrimitiveType.Cube, root, Vector3.zero, Quaternion.identity, new Vector3(size.x, size.y, 0.02f), surfaceMat);
                int layer = PortalView.LayerPortalSurface;
                if (layer >= 0) surfaceRenderer.gameObject.layer = layer;
                gameObject.AddComponent<PortalView>().Init(this, surfaceRenderer, surfaceMat);
            }
            else
            {
                surfaceRenderer = Part(PrimitiveType.Quad, root, new Vector3(0f, 0f, 0.02f), HalfTurn, new Vector3(size.x, size.y, 1f), mat);
            }

            // Low hum so you can hear portals around you.
            if (!Application.isBatchMode)
            {
                var hum = gameObject.AddComponent<AudioSource>();
                hum.clip = SoundBank.PortalHum;
                hum.loop = true;
                hum.spatialBlend = 1f;
                hum.rolloffMode = AudioRolloffMode.Linear;
                hum.minDistance = 1f;
                hum.maxDistance = 12f;
                hum.volume = 0.35f;
                hum.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
                hum.Play();
            }

            // Clean look: a thin, flush frame and no posts, so the portal reads as a plain opening.
            float t = CleanLook ? 0.05f : 0.14f, depth = CleanLook ? 0.04f : 0.1f;
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            frameRenderers.Add(Part(PrimitiveType.Cube, root, new Vector3(0f, hy, depth * 0.5f), Quaternion.identity, new Vector3(size.x + t, t, depth), mat));
            frameRenderers.Add(Part(PrimitiveType.Cube, root, new Vector3(0f, -hy, depth * 0.5f), Quaternion.identity, new Vector3(size.x + t, t, depth), mat));
            frameRenderers.Add(Part(PrimitiveType.Cube, root, new Vector3(-hx, 0f, depth * 0.5f), Quaternion.identity, new Vector3(t, size.y + t, depth), mat));
            frameRenderers.Add(Part(PrimitiveType.Cube, root, new Vector3(hx, 0f, depth * 0.5f), Quaternion.identity, new Vector3(t, size.y + t, depth), mat));

            // Corner posts stick out along the normal: they show which way is "up"
            // for anyone who comes out of this portal.
            if (CleanLook) return;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    var post = Part(PrimitiveType.Cube, root, new Vector3(sx * hx, sy * hy, 0.4f), Quaternion.identity, new Vector3(0.07f, 0.07f, 0.8f), mat);
                    frameRenderers.Add(post);
                    posts.Add(post.transform);
                }
        }

        static Renderer Part(PrimitiveType type, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            col.enabled = false;
            Destroy(col);
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

        void Update()
        {
            if (surfaceRenderer == null) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin((Time.time + phase) * 3f);
            float shimmer = 0.5f + 0.5f * Mathf.Sin((Time.time + phase) * 7.3f);

            bool hasView = surfaceRenderer.sharedMaterial != null && surfaceRenderer.sharedMaterial.HasProperty("_HasView");
            SetColor(surfaceRenderer, hasView
                ? color * (0.85f + 0.3f * pulse)                               // tints the view's energy rim
                : color * (0.18f + 0.22f * pulse + 0.08f * shimmer));          // plain glowing surface
            Color frame = CleanLook ? color * 0.9f : color * (1.6f + 0.9f * pulse); // clean look: a steady, quiet edge
            frame.a = 1f;
            foreach (var r in frameRenderers) SetColor(r, frame);

            float postLength = 0.6f + 0.35f * pulse;
            foreach (var p in posts)
            {
                var s = p.localScale; s.z = postLength; p.localScale = s;
                var lp = p.localPosition; lp.z = postLength * 0.5f; p.localPosition = lp;
            }
        }

        void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.02f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 1.5f);
            Gizmos.DrawWireCube(Vector3.forward * 1.5f, Vector3.one * 0.15f);
        }

        void OnDrawGizmosSelected()
        {
            if (linkedPortal == null) return;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = color;
            Gizmos.DrawLine(transform.position, linkedPortal.transform.position);
        }
    }
}
