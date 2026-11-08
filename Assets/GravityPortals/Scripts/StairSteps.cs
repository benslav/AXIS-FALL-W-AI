using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Marks a hidden walking ramp as a staircase. The physics stays a smooth ramp (a capsule
    /// can't climb real steps nicely), but the player's camera uses this to sit on the tread
    /// under their feet and step up or down at each edge, with a footstep per step.
    ///
    /// Treads are every <see cref="stepSize"/> along whichever "up" you walk with, measured
    /// from <see cref="origin"/>, so a 45-degree flight works as stairs for both gravity
    /// directions that can walk it.
    /// </summary>
    public class StairSteps : MonoBehaviour
    {
        [Tooltip("A point on a tread edge (world space). Treads are whole steps from here.")]
        public Vector3 origin;
        public float stepSize = 0.25f;

        /// <summary>Which step you're on, for a given point on the ramp and your up.</summary>
        public int StepIndex(Vector3 rampPoint, Vector3 up) =>
            Mathf.FloorToInt(Vector3.Dot(rampPoint - origin, up) / stepSize + 1e-3f);

        /// <summary>How far below the ramp surface the tread under you is (0 to -stepSize).</summary>
        public float TreadOffset(Vector3 rampPoint, Vector3 up)
        {
            float h = Vector3.Dot(rampPoint - origin, up) / stepSize;
            return (Mathf.Floor(h + 1e-3f) - h) * stepSize;
        }
    }
}
