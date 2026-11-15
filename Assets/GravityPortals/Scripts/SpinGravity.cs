using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Spin gravity for a rotating station round the world Z axis: "down" points straight out from
    /// (or in toward) the axis, turning smoothly as you walk round a ring, with optional spoke
    /// tunnels and a hub (see <see cref="SpinField"/>). Only maps that place this component have
    /// it; everywhere else gravity is the usual fixed direction.
    ///
    /// The rule (<see cref="Apply"/>) keeps whatever sign your gravity already has on a ring,
    /// outward or inward, and points it along the field at your position. Players, crates and
    /// bolts call it every physics step and after a portal pass (so going through a portal can flip
    /// you from outward to inward), and the server uses the same rule to validate movement.
    /// </summary>
    public class SpinGravity : MonoBehaviour
    {
        [Tooltip("The station's centre (on the spin axis, which is world Z).")]
        public Vector3 axisPoint = Vector3.zero;
        public SpinField field = new SpinField();

        public static SpinGravity Current { get; private set; }

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        public Vector3 Resolve(Vector3 g, Vector3 p) => field.Resolve(g, p - axisPoint);

        /// <summary>The rule for everyone: on a spin map, follow the field; elsewhere, unchanged.</summary>
        public static Vector3 Apply(Vector3 g, Vector3 p) => Current != null ? Current.Resolve(g, p) : g;

        /// <summary>Fallen off a ring into the void.</summary>
        public bool OutOfBounds(Vector3 p) => field.OutOfBounds(p - axisPoint);

        /// <summary>Server check: does <paramref name="reported"/> match the field anywhere along the
        /// move from <paramref name="from"/> to <paramref name="to"/>? (The client works gravity out
        /// at the start of its step, and on a tight curve it turns noticeably within one move.)</summary>
        public static bool MatchesAlong(Vector3 reported, Vector3 previous, Vector3 from, Vector3 to, float tolerance)
        {
            if (Current == null) return Vector3.Angle(reported, previous) < tolerance;
            for (int i = 0; i <= 4; i++)
                if (Vector3.Angle(reported, Current.Resolve(previous, Vector3.Lerp(from, to, i / 4f))) < tolerance) return true;
            return false;
        }
    }
}
