using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// The turning cube's on-screen warning for the local player, shown during the warning before a
    /// turn and while it turns:
    ///  - standing on the layer about to turn: a calm "hold on" (it will carry you);
    ///  - near it but not on it (where its sweep could crush you): a red "stand clear", with the
    ///    screen edges glowing.
    /// Nothing at all otherwise.
    /// </summary>
    public class CubeTurnHud : MonoBehaviour
    {
        [Tooltip("How close to a turning layer's seam counts as in danger, metres (your middle from the seam).")]
        public float seamDanger = 1.5f;
        [Tooltip("Inside the hollow, closer than this to the turning axis is always safe (nothing sweeps there), metres.")]
        public float safeRadius = 13.5f;

        GUIStyle style;

        void OnGUI()
        {
            var turns = CubeTurns.Current;
            var me = GravityPlayerController.Local;
            if (turns == null || me == null || !me.IsAlive) return;
            var pose = turns.RenderPose;
            float warning = turns.schedule.warning;
            if (!pose.Turning && pose.UntilNext > warning) return;

            var move = pose.Move;
            Vector3 p = me.transform.position - turns.Centre;
            int piece = me.RidingPiece;
            bool riding = piece >= 0 && piece < 27 && pose.InMove[piece];
            float along = CubePose.Coord(p, move.Axis) * move.Side;
            float radial = Vector3.ProjectOnPlane(p, move.AxisVector).magnitude;
            bool danger = !riding && along > pose.Seam - seamDanger && radial > safeRadius;
            if (!riding && !danger) return;

            if (style == null)
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 17 };
            float w = Screen.width, h = Screen.height, cx = w * 0.5f;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 12f);
            string when = pose.Turning ? "TURNING" : $"TURNS IN {Mathf.CeilToInt(pose.UntilNext)}";
            string text;
            Color c;
            if (danger)
            {
                text = $"STAND CLEAR  -  LAYER {when}";
                c = new Color(1f, 0.25f, 0.15f, pulse);
                float edge = 14f;
                GUI.color = new Color(1f, 0.2f, 0.1f, 0.22f * pulse);
                var white = Texture2D.whiteTexture;
                GUI.DrawTexture(new Rect(0, 0, w, edge), white);
                GUI.DrawTexture(new Rect(0, h - edge, w, edge), white);
                GUI.DrawTexture(new Rect(0, 0, edge, h), white);
                GUI.DrawTexture(new Rect(w - edge, 0, edge, h), white);
            }
            else
            {
                text = $"HOLD ON  -  YOUR LAYER {when}";
                c = new Color(1f, 0.7f, 0.25f, 0.9f);
            }
            GUI.color = new Color(0f, 0f, 0f, c.a * 0.6f);
            GUI.Label(new Rect(cx - 249, h * 0.18f + 1, 500, 28), text, style);
            GUI.color = c;
            GUI.Label(new Rect(cx - 250, h * 0.18f, 500, 28), text, style);
            GUI.color = Color.white;
        }
    }
}
