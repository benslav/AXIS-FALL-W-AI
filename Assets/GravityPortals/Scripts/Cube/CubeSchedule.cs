using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>One quarter turn of an outer layer of the cube.</summary>
    public struct CubeMove
    {
        /// <summary>World axis the layer turns round: 0 = X, 1 = Y, 2 = Z.</summary>
        public int Axis;
        /// <summary>Which outer layer: +1 or -1 along <see cref="Axis"/>.</summary>
        public int Side;
        /// <summary>Direction of the turn: +1 or -1 (a quarter turn of +-90 degrees round the axis).</summary>
        public int Dir;

        /// <summary>The face index (0..5: +X, -X, +Y, -Y, +Z, -Z) of the turning layer.</summary>
        public int Face => Axis * 2 + (Side > 0 ? 0 : 1);
        public Vector3 AxisVector => CubeSchedule.AxisVector(Axis);

        public override string ToString() => $"{"XYZ"[Axis]}{(Side > 0 ? "+" : "-")} {(Dir > 0 ? "cw" : "ccw")}";
    }

    /// <summary>
    /// Where every piece of the cube is at a given moment: a rotation per piece (home -> now),
    /// plus the turn in progress, if any. Filled by <see cref="CubeSchedule.Evaluate"/>.
    /// </summary>
    public sealed class CubePose
    {
        /// <summary>Each piece's rotation round the cube centre, from its home place to now
        /// (including the turn in progress).</summary>
        public readonly Quaternion[] Pieces = new Quaternion[27];
        /// <summary>Each piece's rotation after the finished turns only, as an exact integer matrix
        /// (row-major 3x3, entries -1/0/1), so every machine agrees exactly however long the match.</summary>
        public readonly int[][] Matrices = new int[27][];
        /// <summary>How many turns have finished.</summary>
        public int Done;
        public bool Turning;
        /// <summary>The turn in progress (or the next one, while not turning).</summary>
        public CubeMove Move;
        /// <summary>The turn in progress so far, 0..1 (eased: slow start and stop).</summary>
        public float Progress;
        /// <summary>The turn in progress so far, degrees (signed: Dir * 90 * eased progress).</summary>
        public float Angle;
        /// <summary>How fast it is turning right now, degrees per second (signed like Angle).</summary>
        public float AngularSpeed;
        /// <summary>The rotation of the turning layer so far (identity while not turning).</summary>
        public Quaternion TurnRotation = Quaternion.identity;
        /// <summary>Seconds until the next turn starts (0 while turning).</summary>
        public float UntilNext;
        /// <summary>Which pieces are in the turning layer (or the next one to turn).</summary>
        public readonly bool[] InMove = new bool[27];
        /// <summary>Each face's finished quarter turns in all (signed by direction), for the gears.</summary>
        public readonly int[] FaceQuarters = new int[6];

        /// <summary>How far face <paramref name="face"/> has turned in all, degrees (finished turns
        /// plus the one in progress): what its gears follow.</summary>
        public float FaceAngle(int face) => FaceQuarters[face] * 90f + (Turning && Move.Face == face ? Angle : 0f);

        /// <summary>Distance of the seam planes between layers from the centre (set by the schedule).</summary>
        public float Seam = 7f;

        public CubePose()
        {
            for (int i = 0; i < 27; i++)
            {
                Pieces[i] = Quaternion.identity;
                Matrices[i] = CubeSchedule.Identity();
            }
        }

        /// <summary>Is <paramref name="p"/> (relative to the cube centre) in the slab of the layer
        /// that is turning right now? Space there turns with the layer.</summary>
        public bool InTurningLayer(Vector3 p) => Turning && Coord(p, Move.Axis) * Move.Side > Seam;

        public static float Coord(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;
    }

    /// <summary>
    /// The cube's turns, the same on every machine: turn k starts <see cref="firstTurn"/> +
    /// k * <see cref="interval"/> seconds after the match starts and lasts <see cref="duration"/>
    /// seconds. Which layer turns and which way comes from the seed alone (never the same layer
    /// twice in a row, so a turn never just undoes the last one). The server only has to share
    /// the seed and the start time; everyone works out the cube from the clock.
    ///
    /// Only the six outer layers turn (like a real cube: its centres are fixed to the core and
    /// only spin), so the centre pieces never leave their face and the core never moves.
    ///
    /// Pure maths, no engine calls beyond Vector3/Quaternion arithmetic, so it can be checked
    /// outside Unity.
    /// </summary>
    [System.Serializable]
    public sealed class CubeSchedule
    {
        public int seed = 1;
        [Tooltip("Seconds from the start of the match to the first turn.")]
        public float firstTurn = 15f;
        [Tooltip("Seconds from the start of one turn to the start of the next.")]
        public float interval = 20f;
        [Tooltip("How long a quarter turn takes, seconds.")]
        public float duration = 4f;
        [Tooltip("Warning lights and klaxon this many seconds before a turn.")]
        public float warning = 3f;
        [Tooltip("Distance of the seams between layers from the centre (half a piece), metres.")]
        public float seam = 7f;

        readonly List<CubeMove> moves = new List<CubeMove>();
        int movesSeed = int.MinValue;

        // Finished-turn state cache, so evaluating every frame is cheap.
        int[][] cached;
        int cachedDone = -1;
        int cachedSeed = int.MinValue;
        readonly int[] cachedFaces = new int[6];

        // ------------------------------------------------------------------ pieces

        /// <summary>Piece index for grid place (x, y, z), each -1/0/1. 13 is the core.</summary>
        public static int Index(int x, int y, int z) => (x + 1) + 3 * (y + 1) + 9 * (z + 1);
        public static int Index(Vector3Int g) => Index(g.x, g.y, g.z);
        public static Vector3Int Home(int i) => new Vector3Int(i % 3 - 1, i / 3 % 3 - 1, i / 9 - 1);
        public const int Core = 13;

        public static bool IsCentre(int i)
        {
            var h = Home(i);
            return Mathf.Abs(h.x) + Mathf.Abs(h.y) + Mathf.Abs(h.z) == 1;
        }

        public static Vector3 AxisVector(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        /// <summary>Face index (0..5: +X, -X, +Y, -Y, +Z, -Z) -> outward direction.</summary>
        public static Vector3 FaceNormal(int face) => AxisVector(face / 2) * (face % 2 == 0 ? 1f : -1f);

        // ------------------------------------------------------------------ moves

        public CubeMove MoveAt(int k)
        {
            if (movesSeed != seed)
            {
                moves.Clear();
                movesSeed = seed;
            }
            while (moves.Count <= k)
            {
                int n = moves.Count;
                uint h = Hash((uint)seed, (uint)n);
                int face;
                if (n == 0) face = (int)(h % 6u);
                else face = (moves[n - 1].Face + 1 + (int)(h % 5u)) % 6; // any layer but the last one
                moves.Add(new CubeMove { Axis = face / 2, Side = face % 2 == 0 ? 1 : -1, Dir = ((h >> 16) & 1u) == 0 ? 1 : -1 });
            }
            return moves[k];
        }

        public float StartOf(int k) => firstTurn + k * interval;

        /// <summary>Eased turn progress: smoothstep, so it starts and stops gently.</summary>
        public static float Ease(float u) => u * u * (3f - 2f * u);
        static float EaseRate(float u) => 6f * u * (1f - u);

        // ------------------------------------------------------------------ evaluation

        /// <summary>The cube at <paramref name="time"/> seconds after the match started.</summary>
        public void Evaluate(double time, CubePose pose)
        {
            pose.Seam = seam;
            float t = (float)time;
            // Turn k is in progress or finished once t >= StartOf(k).
            int started = t < firstTurn ? 0 : Mathf.FloorToInt((t - firstTurn) / interval) + 1;
            int done = started;
            float progress = 0f, rate = 0f;
            bool turning = false;
            if (started > 0)
            {
                float since = t - StartOf(started - 1);
                if (since < duration)
                {
                    done = started - 1;
                    turning = true;
                    float u = Mathf.Clamp01(since / duration);
                    progress = Ease(u);
                    rate = EaseRate(u) / duration;
                }
            }

            var mats = Finished(done);
            var move = MoveAt(done);
            pose.Done = done;
            pose.Turning = turning;
            pose.Move = move;
            pose.Progress = progress;
            pose.Angle = move.Dir * 90f * progress;
            pose.AngularSpeed = turning ? move.Dir * 90f * rate : 0f;
            pose.TurnRotation = turning ? AxisAngle(move.AxisVector, pose.Angle) : Quaternion.identity;
            pose.UntilNext = turning ? 0f : Mathf.Max(0f, StartOf(done) - t);
            System.Array.Copy(cachedFaces, pose.FaceQuarters, 6);

            for (int i = 0; i < 27; i++)
            {
                var m = mats[i];
                System.Array.Copy(m, pose.Matrices[i], 9);
                Vector3Int now = Apply(m, Home(i));
                bool inMove = i != Core && Coord(now, move.Axis) == move.Side;
                pose.InMove[i] = inMove;
                Quaternion q = ToQuaternion(m);
                pose.Pieces[i] = turning && inMove ? pose.TurnRotation * q : q;
            }
        }

        /// <summary>Every piece's rotation after the first <paramref name="done"/> turns.</summary>
        int[][] Finished(int done)
        {
            // A little back in time (the server checking a move from a moment ago, clock jitter):
            // undo the last turns rather than replay them all.
            if (cached != null && cachedSeed == seed && done < cachedDone && cachedDone - done <= 8)
            {
                while (cachedDone > done)
                {
                    cachedDone--;
                    var move = MoveAt(cachedDone);
                    var undo = move;
                    undo.Dir = -move.Dir;
                    ApplyMove(cached, undo); // a turn keeps its layer, so the same pieces turn back
                    cachedFaces[move.Face] -= move.Dir;
                }
            }
            if (cached == null || cachedSeed != seed || done < cachedDone)
            {
                cached = new int[27][];
                for (int i = 0; i < 27; i++) cached[i] = Identity();
                cachedDone = 0;
                cachedSeed = seed;
                System.Array.Clear(cachedFaces, 0, 6);
            }
            while (cachedDone < done)
            {
                var move = MoveAt(cachedDone);
                ApplyMove(cached, move);
                cachedFaces[move.Face] += move.Dir;
                cachedDone++;
            }
            return cached;
        }

        /// <summary>Turns every piece in the move's layer a quarter turn (exact integers).</summary>
        public static void ApplyMove(int[][] mats, CubeMove move)
        {
            int[] q = Quarter(move.Axis, move.Dir);
            for (int i = 0; i < 27; i++)
            {
                if (i == Core) continue;
                Vector3Int now = Apply(mats[i], Home(i));
                if (Coord(now, move.Axis) != move.Side) continue;
                mats[i] = Mul(q, mats[i]);
            }
        }

        static int Coord(Vector3Int v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;

        // ------------------------------------------------------------------ exact rotations

        public static int[] Identity() => new[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        /// <summary>The integer matrix of a quarter turn (+-90 degrees) round a world axis, the same
        /// rotation as <see cref="AxisAngle"/> gives.</summary>
        public static int[] Quarter(int axis, int dir)
        {
            Quaternion q = AxisAngle(AxisVector(axis), dir * 90f);
            var m = new int[9];
            for (int c = 0; c < 3; c++)
            {
                Vector3 col = q * AxisVector(c);
                m[0 * 3 + c] = Mathf.RoundToInt(col.x);
                m[1 * 3 + c] = Mathf.RoundToInt(col.y);
                m[2 * 3 + c] = Mathf.RoundToInt(col.z);
            }
            return m;
        }

        public static int[] Mul(int[] a, int[] b)
        {
            var m = new int[9];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    m[r * 3 + c] = a[r * 3] * b[c] + a[r * 3 + 1] * b[3 + c] + a[r * 3 + 2] * b[6 + c];
            return m;
        }

        public static Vector3Int Apply(int[] m, Vector3Int v) => new Vector3Int(
            m[0] * v.x + m[1] * v.y + m[2] * v.z,
            m[3] * v.x + m[4] * v.y + m[5] * v.z,
            m[6] * v.x + m[7] * v.y + m[8] * v.z);

        public static Vector3 Apply(int[] m, Vector3 v) => new Vector3(
            m[0] * v.x + m[1] * v.y + m[2] * v.z,
            m[3] * v.x + m[4] * v.y + m[5] * v.z,
            m[6] * v.x + m[7] * v.y + m[8] * v.z);

        public static Quaternion ToQuaternion(int[] m) =>
            PavilionLayout.Basis(new Vector3(m[2], m[5], m[8]), new Vector3(m[1], m[4], m[7]));

        /// <summary>Rotation by <paramref name="degrees"/> round a unit <paramref name="axis"/>
        /// (same as Quaternion.AngleAxis, written out so it runs outside the engine).</summary>
        public static Quaternion AxisAngle(Vector3 axis, float degrees)
        {
            float h = degrees * Mathf.Deg2Rad * 0.5f, s = Mathf.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(h));
        }

        public static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        /// <summary>A well-mixed 32-bit hash of (seed, n): the same on every machine and runtime.</summary>
        static uint Hash(uint seed, uint n)
        {
            ulong z = ((ulong)seed << 32 | n) + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (uint)(z ^ (z >> 32));
        }
    }
}
