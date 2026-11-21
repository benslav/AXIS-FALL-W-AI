using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Turns the cube's layers on schedule, the same on every machine.
    ///
    /// Sync: nothing is sent. The turns are a pure function of the seed and the clock
    /// (<see cref="CubeSchedule"/>), and online the clock is Netcode's server time, which every
    /// client tracks. The seed is the session's map seed (re-rolled every match) when there is a
    /// lobby, else a fixed one. So the server is authoritative simply by owning the clock, and a
    /// late joiner works the cube out from the same seed and time.
    ///
    /// Physics: each fixed step moves every piece (a kinematic Rigidbody) to where it must be at
    /// the end of the step, so it pushes and carries what touches it. Drawing: every frame the
    /// pieces are shown at the exact pose for this frame's time (the same smoothing the local
    /// player's camera uses), then put back to their physics pose before the next physics step,
    /// so physics and portals always see the true position.
    ///
    /// Also drives the gears, the warning lamps on the layer about to turn and the sounds, and
    /// gives the gravity field (<see cref="SpinGravity.CubePose"/>) the current pose.
    /// </summary>
    [DefaultExecutionOrder(-200)] // before players, bodies and portals
    public class CubeTurns : MonoBehaviour
    {
        public CubeSchedule schedule = new CubeSchedule();
        [Tooltip("Seed used when there's no lobby (a map opened directly).")]
        public int fallbackSeed = 1;
        [Tooltip("The warning lamps' material: renderers using it on a piece flash before and during its turns.")]
        public Material warningMaterial;
        [Tooltip("Half the cube's outer size (for placing sounds), metres.")]
        public float outerHalf = 21f;
        [Tooltip("The hollow's light by each face (0..5: +X, -X, +Y, -Y, +Z, -Z): it pulses amber before and while its face turns.")]
        public Light[] faceLights = new Light[0];
        [Tooltip("Colour the face light pulses to.")]
        public Color alarmColor = new Color(1f, 0.5f, 0.12f);

        Color[] lightColors;
        float[] lightIntensities;

        public static CubeTurns Current { get; private set; }

        /// <summary>The pose physics uses (at the end of the current physics step).</summary>
        public CubePose Pose { get; } = new CubePose();
        /// <summary>The pose drawn this frame.</summary>
        public CubePose RenderPose { get; } = new CubePose();

        readonly CubePiece[] pieces = new CubePiece[27];
        readonly Quaternion[] physicsRotation = new Quaternion[27];
        readonly List<Renderer>[] lamps = new List<Renderer>[27];
        CubeGearSpin[] gears = new CubeGearSpin[0];
        int lastWarned = -1, lastGround = -1;
        bool started;

        // Clock: physics runs on fixed steps, so it follows Time.fixedTimeAsDouble shifted onto the
        // server clock; the shift is re-measured all the time and eased (no jumps from network jitter).
        double clockOffset;
        bool hasOffset;

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
            if (SpinGravity.Current != null && SpinGravity.Current.CubePose == Pose) SpinGravity.Current.CubePose = null;
        }

        void Start()
        {
            for (int i = 0; i < 27; i++)
            {
                pieces[i] = CubePiece.Find(i);
                physicsRotation[i] = Quaternion.identity;
                lamps[i] = new List<Renderer>();
                if (pieces[i] == null) continue;
                foreach (var r in pieces[i].GetComponentsInChildren<Renderer>(true))
                    if (warningMaterial != null && r.sharedMaterial == warningMaterial)
                    {
                        lamps[i].Add(r);
                        r.enabled = false;
                    }
            }
            gears = FindObjectsByType<CubeGearSpin>(FindObjectsSortMode.None);
            lightColors = new Color[faceLights.Length];
            lightIntensities = new float[faceLights.Length];
            for (int f = 0; f < faceLights.Length; f++)
                if (faceLights[f] != null) { lightColors[f] = faceLights[f].color; lightIntensities[f] = faceLights[f].intensity; }
            started = true;
        }

        int Seed()
        {
            var lobby = LobbyState.Instance;
            return lobby != null && lobby.IsSpawned ? lobby.MapSeed : fallbackSeed;
        }

        /// <summary>Seconds on the shared clock (server time online, local time offline).</summary>
        static double SharedNow()
        {
            var nm = NetworkManager.Singleton;
            if (NetMode.IsOnline && nm != null) return nm.ServerTime.Time;
            return Time.timeAsDouble;
        }

        void UpdateClock()
        {
            double target = SharedNow() - Time.timeAsDouble;
            if (!hasOffset || System.Math.Abs(target - clockOffset) > 0.25)
            {
                clockOffset = target; // first time, or a real jump (reconnect): take it
                hasOffset = true;
            }
            else
            {
                clockOffset += (target - clockOffset) * 0.05; // ease out jitter
            }
        }

        /// <summary>The shared clock at local time <paramref name="localTime"/>.</summary>
        public double ClockAt(double localTime) => localTime + clockOffset;

        /// <summary>The shared clock now (as physics sees it: the end of the current step).</summary>
        public double PhysicsClock => ClockAt(Time.fixedTimeAsDouble + Time.fixedDeltaTime);

        void FixedUpdate()
        {
            if (!started) return;
            UpdateClock();
            schedule.seed = Seed();
            // Back to the true pose first (the frame drew the pieces a little earlier along).
            for (int i = 0; i < 27; i++)
                if (pieces[i] != null) pieces[i].transform.rotation = physicsRotation[i];

            schedule.Evaluate(PhysicsClock, Pose);
            for (int i = 0; i < 27; i++)
            {
                var p = pieces[i];
                if (p == null) continue;
                Quaternion q = Pose.Pieces[i];
                if (Quaternion.Dot(q, physicsRotation[i]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                // A finished turn whose end we skipped (a hitch) or a late join: teleport, don't sweep
                // (and don't carry anyone along with the jump).
                if (Angle(q, physicsRotation[i]) > 30f)
                {
                    p.transform.rotation = q;
                    stepDelta[i] = Quaternion.identity;
                }
                else
                {
                    stepDelta[i] = q * CubeSchedule.Conjugate(physicsRotation[i]);
                }
                p.Body.MoveRotation(q);
                physicsRotation[i] = q;
            }
            if (SpinGravity.Current != null) SpinGravity.Current.CubePose = Pose;
        }

        void Update()
        {
            if (!started) return;
            UpdateClock();
            schedule.seed = Seed();
            schedule.Evaluate(ClockAt(Time.timeAsDouble), RenderPose);
            var pose = RenderPose;

            // Draw the pieces where they are now.
            for (int i = 0; i < 27; i++)
                if (pieces[i] != null) pieces[i].transform.rotation = pose.Pieces[i];

            foreach (var g in gears)
                if (g != null) g.SetFaceAngle(pose.FaceAngle(g.face));

            // Warning lamps: flash on the next layer to turn, steady while it turns.
            bool warn = !pose.Turning && pose.UntilNext <= schedule.warning;
            bool lit = pose.Turning || (warn && Mathf.Repeat(pose.UntilNext, 0.5f) > 0.25f);
            for (int i = 0; i < 27; i++)
            {
                bool on = lit && pose.InMove[i];
                foreach (var r in lamps[i]) if (r.enabled != on) r.enabled = on;
            }

            // The hollow's light by the turning face pulses amber (a slow, soft pulse: no strobing).
            for (int f = 0; f < faceLights.Length; f++)
            {
                var l = faceLights[f];
                if (l == null) continue;
                bool alarm = (warn || pose.Turning) && pose.Move.Face == f;
                float k = alarm ? 0.55f + 0.45f * Mathf.Sin(Time.time * 5f) : 0f;
                l.color = Color.Lerp(lightColors[f], alarmColor, k);
                l.intensity = lightIntensities[f] * (1f + 0.6f * k);
            }

            // Sounds, once per turn: the klaxon when the warning starts, the grind when it turns.
            int next = pose.Done;
            Vector3 n = CubeSchedule.FaceNormal(pose.Move.Face);
            if (warn && lastWarned != next && pose.UntilNext > schedule.warning - 0.5f)
            {
                lastWarned = next;
                Sfx.Play3D(SoundBank.Klaxon, n * (outerHalf + 4f), 0.8f, 1f, 90f);
                Sfx.Play3D(SoundBank.Klaxon, n * 8f, 0.8f, 1f, 60f);
            }
            if (pose.Turning && lastGround != next && pose.Progress < 0.1f)
            {
                lastGround = next;
                Sfx.Play3D(SoundBank.Grind, n * (outerHalf + 2f), 1f, 1f, 90f);
                Sfx.Play3D(SoundBank.Grind, n * 10f, 1f, 0.9f, 60f);
            }
        }

        static float Angle(Quaternion a, Quaternion b)
        {
            float d = Mathf.Min(1f, Mathf.Abs(Quaternion.Dot(a, b)));
            return 2f * Mathf.Acos(d) * Mathf.Rad2Deg;
        }

        /// <summary>The piece object at grid index <paramref name="i"/> (null for the core).</summary>
        public CubePiece Piece(int i) => pieces[i];

        // ------------------------------------------------------------------ for riders and the server

        readonly Quaternion[] stepDelta = InitDeltas();
        static Quaternion[] InitDeltas()
        {
            var d = new Quaternion[27];
            for (int i = 0; i < 27; i++) d[i] = Quaternion.identity;
            return d;
        }

        /// <summary>How piece <paramref name="i"/> turns during the current physics step (round the
        /// cube centre). Riders are moved by exactly this.</summary>
        public Quaternion StepDelta(int i) => i >= 0 && i < 27 ? stepDelta[i] : Quaternion.identity;

        /// <summary>The cube centre (pieces turn round it).</summary>
        public Vector3 Centre => Vector3.zero;

        CubeSchedule history;
        readonly CubePose historyPose = new CubePose();
        double historyTime = double.NaN;
        int historySeed;

        /// <summary>The cube at any moment on the shared clock (for checking moves made a moment
        /// ago, and drawing remote players). The returned pose is reused by the next call.</summary>
        public CubePose PoseAt(double clock)
        {
            if (history == null)
                history = new CubeSchedule
                {
                    firstTurn = schedule.firstTurn, interval = schedule.interval, duration = schedule.duration,
                    warning = schedule.warning, seam = schedule.seam,
                };
            int seed = Seed();
            if (clock != historyTime || seed != historySeed)
            {
                history.seed = seed;
                history.Evaluate(clock, historyPose);
                historyTime = clock;
                historySeed = seed;
            }
            return historyPose;
        }

        /// <summary>The shared clock right now, as the frame is drawn.</summary>
        public double Now => ClockAt(Time.timeAsDouble);
    }
}
