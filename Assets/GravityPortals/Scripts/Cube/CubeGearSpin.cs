using UnityEngine;

namespace GravityPortals
{
    /// <summary>A gear of the cube's machinery that spins round its own axis while its face turns
    /// (driven by the cube's turns; look only, no collider).</summary>
    public class CubeGearSpin : MonoBehaviour
    {
        [Tooltip("The face (0..5: +X, -X, +Y, -Y, +Z, -Z) whose turns drive this gear.")]
        public int face;
        [Tooltip("Degrees it spins round its own axis (local Z) per degree its face turns.")]
        public float ratio = 1f;

        Quaternion rest;
        bool hasRest;

        void Awake()
        {
            rest = transform.localRotation;
            hasRest = true;
        }

        /// <summary>Sets how far its face has turned in all, degrees (finished turns plus the one in progress).</summary>
        public void SetFaceAngle(float degrees)
        {
            if (!hasRest) Awake();
            transform.localRotation = rest * CubeSchedule.AxisAngle(Vector3.forward, degrees * ratio);
        }
    }
}
