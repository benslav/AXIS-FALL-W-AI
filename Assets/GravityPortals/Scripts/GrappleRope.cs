using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPortals
{
    /// <summary>
    /// Draws a player's grappling rope (yours and everyone else's): a thin line from the hand
    /// to the anchor that shoots out when the hook is fired. Added at runtime by
    /// <see cref="GravityPlayerController"/>; runs after it so the rope starts exactly at the
    /// hand of this frame's camera.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class GrappleRope : MonoBehaviour
    {
        static readonly Color RopeColor = new Color(0.85f, 0.95f, 1f);

        GravityPlayerController controller;
        LineRenderer line;
        float extended; // 0..1 while the rope shoots out

        void Awake()
        {
            controller = GetComponent<GravityPlayerController>();
            var go = new GameObject("Grapple Rope");
            go.transform.SetParent(transform, false);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.06f;
            line.endWidth = 0.04f;
            line.numCapVertices = 2;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            line.material = new Material(shader);
            line.material.SetColor("_BaseColor", RopeColor);
            line.material.SetColor("_Color", RopeColor);
            line.enabled = false;
        }

        void LateUpdate()
        {
            if (controller == null || !controller.IsGrappling || !controller.IsAlive)
            {
                extended = 0f;
                line.enabled = false;
                return;
            }

            // The player's own renderers are moved onto a layer your camera doesn't draw;
            // the rope should always show.
            line.gameObject.layer = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;

            extended = Mathf.MoveTowards(extended, 1f, Time.deltaTime * 14f);
            Vector3 start = controller.RopeStart;
            line.SetPosition(0, start);
            line.SetPosition(1, Vector3.Lerp(start, controller.GrappleAnchor, extended));
            line.enabled = true;
        }
    }
}
