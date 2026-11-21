using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// A map-wide gravity field: "down" depends on where you are. Only maps that place this
    /// component have one; everywhere else gravity is the usual fixed direction that changes only
    /// through portals. It holds one of two fields:
    ///  - <see cref="SpinField"/>: a rotating station round the world Z axis. Down points straight
    ///    out from (or in toward) the axis, turning smoothly as you walk round a ring, with
    ///    optional spoke tunnels and a hub. On a ring it keeps the sign your gravity already has.
    ///  - <see cref="RoomField"/> (used when it has rooms): walk-anywhere cube rooms with rounded
    ///    edges (The Tesseract). Down follows the curve you stand on, onto any wall.
    ///
    /// Players, crates and bolts call <see cref="Apply"/> every physics step and after a portal
    /// pass, and the server uses the same rule to validate movement. On these maps the player's
    /// view turns with the floor and eases through curves (see GravityPlayerController).
    /// </summary>
    public class SpinGravity : MonoBehaviour
    {
        [Tooltip("The station's centre (on the spin axis, which is world Z).")]
        public Vector3 axisPoint = Vector3.zero;
        public SpinField field = new SpinField();
        [Tooltip("Cube rooms with rounded edges. Used instead of the spin field when it has any rooms.")]
        public RoomField rooms = new RoomField();
        [Tooltip("The turning cube: walk anywhere outside and inside; turning layers carry their gravity round. Used instead of the others when active.")]
        public CubeField cube = new CubeField { inside = new RoomField() };

        /// <summary>The cube's pose right now (set by its driver; null = every layer in place).</summary>
        public CubePose CubePose { get; set; }

        public static SpinGravity Current { get; private set; }

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        bool UseCube => cube != null && cube.Active;
        bool UseRooms => !UseCube && rooms != null && rooms.Active;

        public Vector3 Resolve(Vector3 g, Vector3 p) =>
            UseCube ? cube.Resolve(g, p - axisPoint, CubePose) :
            UseRooms ? rooms.Resolve(g, p - axisPoint) : field.Resolve(g, p - axisPoint);

        /// <summary>The rule for everyone: on a field map, follow the field; elsewhere, unchanged.</summary>
        public static Vector3 Apply(Vector3 g, Vector3 p) => Current != null ? Current.Resolve(g, p) : g;

        /// <summary>Fallen off a ring into the void, or out of every room.</summary>
        public bool OutOfBounds(Vector3 p) =>
            UseCube ? cube.OutOfBounds(p - axisPoint) :
            UseRooms ? rooms.OutOfBounds(p - axisPoint) : field.OutOfBounds(p - axisPoint);

        /// <summary>Server check: does <paramref name="reported"/> match the field anywhere along the
        /// move from <paramref name="from"/> to <paramref name="to"/>? (The client works gravity out
        /// at the start of its step, and on a tight curve it turns noticeably within one move.)</summary>
        public static bool MatchesAlong(Vector3 reported, Vector3 previous, Vector3 from, Vector3 to, float tolerance)
        {
            if (Current == null) return Vector3.Angle(reported, previous) < tolerance;
            if (Current.UseRooms || Current.UseCube)
            {
                // The room field depends on the gravity you have (it only takes over close to it),
                // and over one update a walk round a curve turns it a long way: follow it along the
                // move in small steps, as the client did.
                Vector3 g = previous;
                for (int i = 0; i <= 12; i++)
                {
                    g = Current.Resolve(g, Vector3.Lerp(from, to, i / 12f));
                    if (Vector3.Angle(reported, g) < tolerance) return true;
                }
                return Vector3.Angle(reported, previous) < tolerance;
            }
            for (int i = 0; i <= 4; i++)
                if (Vector3.Angle(reported, Current.Resolve(previous, Vector3.Lerp(from, to, i / 4f))) < tolerance) return true;
            return false;
        }

        // ------------------------------------------------------------------ the turning cube

        /// <summary>True on the turning cube.</summary>
        public static bool IsCube => Current != null && Current.UseCube;

        /// <summary>Gravity for a traveller carried by a turning layer of the cube (<paramref name="frame"/>
        /// = that layer's rotation so far): follows the layer wherever they stand on it. Elsewhere the
        /// usual rule.</summary>
        public static Vector3 ApplyRiding(Vector3 g, Vector3 p, Quaternion frame) =>
            IsCube ? Current.cube.ResolveInFrame(g, p - Current.axisPoint, frame) : Apply(g, p);

        /// <summary>Server check on the cube: like <see cref="MatchesAlong(Vector3,Vector3,Vector3,Vector3,float)"/>,
        /// but the cube keeps turning during the move, so follow the field along the move in time too:
        /// from the cube at <paramref name="clockFrom"/> to the cube at <paramref name="clockTo"/>
        /// (the clocks the client simulated at). Riders and everyone else are followed both ways.</summary>
        public static bool MatchesAlong(Vector3 reported, Vector3 previous, Vector3 from, Vector3 to, float tolerance,
            double clockFrom, double clockTo)
        {
            var turns = CubeTurns.Current;
            if (!IsCube || turns == null) return MatchesAlong(reported, previous, from, to, tolerance);
            var f = Current.cube;
            Vector3 g = previous, riding = previous;
            for (int i = 0; i <= 12; i++)
            {
                float u = i / 12f;
                var pose = turns.PoseAt(clockFrom + (clockTo - clockFrom) * u);
                Vector3 p = Vector3.Lerp(from, to, u) - Current.axisPoint;
                g = f.Resolve(g, p, pose);
                riding = pose.Turning ? f.ResolveInFrame(riding, p, pose.TurnRotation) : f.Resolve(riding, p, pose);
                if (Vector3.Angle(reported, g) < tolerance || Vector3.Angle(reported, riding) < tolerance) return true;
            }
            return Vector3.Angle(reported, previous) < tolerance;
        }
    }
}
