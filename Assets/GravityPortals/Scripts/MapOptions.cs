using UnityEngine;

namespace GravityPortals
{
    /// <summary>Per-map switches, placed in a map's scene by its builder. Maps without one use the defaults.</summary>
    public class MapOptions : MonoBehaviour
    {
        [Tooltip("Flash a warning when you're about to go through a portal that changes your gravity. " +
                 "Off where every door keeps your gravity relative to you (The Tesseract), so it would only mislead.")]
        public bool gravityWarnings = true;

        [Tooltip("Sounds carry through portals: a sound in the room behind a door is heard coming from that door " +
                 "(up to two doors away). For maps whose rooms only meet through portals (The Tesseract).")]
        public bool soundThroughPortals;

        [Tooltip("How many portals deep a portal view shows other portals' views (1 = just the first, " +
                 "as on every other map; 2-3 = doors seen through a door show their rooms too).")]
        [Range(1, 3)] public int portalRecursion = 1;

        [Header("Camera on gravity changes (0 = the player's own setting)")]
        [Tooltip("How quickly your body turns to a new gravity (higher = faster).")]
        public float gravityTurnRate;
        [Tooltip("The slowest that turn ever goes (degrees per second).")]
        public float gravityTurnMinSpeed;
        [Tooltip("On field maps: how quickly the view follows the floor round curves (higher = tighter, less lag).")]
        public float curveViewEase;

        public static MapOptions Current { get; private set; }

        public static bool GravityWarnings => Current == null || Current.gravityWarnings;
        public static bool SoundThroughPortals => Current != null && Current.soundThroughPortals;
        public static int PortalRecursion => Current != null ? Mathf.Clamp(Current.portalRecursion, 1, 3) : 1;

        public static float GravityTurnRate(float own) => Current != null && Current.gravityTurnRate > 0f ? Current.gravityTurnRate : own;
        public static float GravityTurnMinSpeed(float own) => Current != null && Current.gravityTurnMinSpeed > 0f ? Current.gravityTurnMinSpeed : own;
        public static float CurveViewEase(float own) => Current != null && Current.curveViewEase > 0f ? Current.curveViewEase : own;

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }
    }
}
