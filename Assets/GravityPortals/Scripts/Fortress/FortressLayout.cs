using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// "Hollow Fortress": a vast hollow cube of a Japanese wooden fortress around a central void.
    /// All six inner faces are full arenas (the floor, the four walls turned 90 degrees, the
    /// ceiling turned 180), each with its own "down" into its face, so stepping off anything
    /// drops you back onto your own face: no void deaths.
    ///
    /// Every face has a shrine in the middle (the objective), four corner towers with tatami
    /// rooms and breakable shoji walls, staircases up to a second tier of tower roofs joined by
    /// lantern bridges, one third-tier overlook, and cover. Each face has its own colour
    /// (orange floor, red ceiling, four distinct moonlight blues on the walls) so you can tell
    /// at a glance which way is down. Three shoji door portals per face link every face to
    /// three others; doors are upright on their face, so you always come out standing.
    ///
    /// Pure data (the same containers as the Pavilion), built by <see cref="PavilionBuilder"/>.
    /// Material indices: see the Mat constants and <see cref="FaceMat"/>.
    /// </summary>
    public static class FortressLayout
    {
        public const float Half = 36f;          // half the inner size of the cube
        public const float FaceExtent = 22.5f;  // each face's content stays within this of its centre...
        public const float FaceHeight = 13f;    // ...and this far off its surface, so faces never meet

        public const int MatShell = 0, MatWood = 1, MatStone = 2, MatPaper = 3, MatHeart = 4;
        public const int FaceMatBase = 5;
        public const int SlotTatami = 0, SlotLacquer = 1, SlotLantern = 2, SlotPaper = 3;
        public const int MaterialCount = FaceMatBase + 6 * 4;
        public static int FaceMat(int face, int slot) => FaceMatBase + face * 4 + slot;

        public static readonly string[] FaceNames = { "Floor", "Ceiling", "North Wall", "South Wall", "East Wall", "West Wall" };
        /// <summary>Each face's colour: warm orange floor, deep red ceiling, four moonlit blues.</summary>
        public static readonly Color[] FaceColors =
        {
            new Color(1f, 0.55f, 0.2f),    // floor: lantern orange
            new Color(0.9f, 0.12f, 0.14f), // ceiling: deep red
            new Color(0.45f, 0.7f, 1f),    // north: ice blue
            new Color(0.3f, 0.95f, 0.88f), // south: teal ice
            new Color(0.66f, 0.5f, 1f),    // east: violet moon
            new Color(0.82f, 0.9f, 1f),    // west: pale silver
        };

        static readonly Vector3[] FaceUp = { Vector3.up, Vector3.down, Vector3.back, Vector3.forward, Vector3.left, Vector3.right };
        static readonly Vector3[] FaceForward = { Vector3.forward, Vector3.forward, Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        static readonly int[] FaceTurn = { 0, 1, 2, 3, 0, 1 }; // quarter turns, so the faces aren't all laid out alike

        static readonly Color[] PairColors =
        {
            new Color(1f, 0.6f, 0.2f), new Color(0.95f, 0.25f, 0.3f), new Color(1f, 0.85f, 0.35f),
            new Color(0.7f, 0.4f, 1f), new Color(0.3f, 0.8f, 1f), new Color(0.4f, 1f, 0.6f),
            new Color(1f, 0.45f, 0.75f), new Color(0.95f, 0.95f, 0.6f), new Color(0.6f, 1f, 1f),
        };

        public static PavilionLayout Generate()
        {
            var g = new Generator();
            g.Run();
            return g.Layout;
        }

        sealed class Generator
        {
            public readonly PavilionLayout Layout = new PavilionLayout { Seed = 0 };
            int face;
            Quaternion rot;
            Vector3 origin;
            int nextBreakId;
            // Door slots per face: 0 = third-tier overlook, 1 = tower roof, 2 = ground choke point.
            readonly Dictionary<(int face, int slot), PavilionPortal> doors = new Dictionary<(int, int), PavilionPortal>();

            Vector3 W(Vector3 local) => origin + rot * local;
            Vector3 Dir(Vector3 local) => rot * local;

            void B(string name, Vector3 c, Vector3 s, int mat, bool collide = true, bool breakable = false)
            {
                Layout.Boxes.Add(new PavilionBox
                {
                    Name = $"{FaceNames[face]}: {name}",
                    Center = W(c), Size = s, Rotation = rot,
                    Material = (PavilionMat)mat, Collide = collide, Visible = true,
                    Breakable = breakable, BreakId = breakable ? nextBreakId++ : -1,
                });
            }

            int Tatami => FaceMat(face, SlotTatami);
            int Lacquer => FaceMat(face, SlotLacquer);
            int Lantern => FaceMat(face, SlotLantern);
            int Paper => FaceMat(face, SlotPaper);
            Color LightColor => Color.Lerp(FaceColors[face], Color.white, 0.3f);

            public void Run()
            {
                Shell();
                Heart();
                for (face = 0; face < 6; face++)
                {
                    origin = -FaceUp[face] * Half;
                    float a = FaceTurn[face] * 45f * Mathf.Deg2Rad; // half of the quarter-turn angle
                    var turn = new Quaternion(0f, Mathf.Sin(a), 0f, Mathf.Cos(a));
                    rot = PavilionLayout.Basis(FaceForward[face], FaceUp[face]) * turn;
                    BuildFace();
                }
                LinkDoors();
            }

            // ---------------------------------------------------------------- the cube and its heart

            void Shell()
            {
                float h = Half, t = 1f;
                void S(string n, Vector3 c, Vector3 s) => Layout.Boxes.Add(new PavilionBox
                {
                    Name = $"Shell {n}", Center = c, Size = s, Rotation = Quaternion.identity,
                    Material = (PavilionMat)MatShell, Collide = true, Visible = true, BreakId = -1,
                });
                S("Floor", new Vector3(0, -h - t / 2, 0), new Vector3(2 * h, t, 2 * h));
                S("Ceiling", new Vector3(0, h + t / 2, 0), new Vector3(2 * h, t, 2 * h));
                S("East", new Vector3(h + t / 2, 0, 0), new Vector3(t, 2 * h + 2 * t, 2 * h));
                S("West", new Vector3(-h - t / 2, 0, 0), new Vector3(t, 2 * h + 2 * t, 2 * h));
                S("North", new Vector3(0, 0, h + t / 2), new Vector3(2 * h + 2 * t, 2 * h + 2 * t, t));
                S("South", new Vector3(0, 0, -h - t / 2), new Vector3(2 * h + 2 * t, 2 * h + 2 * t, t));
            }

            /// <summary>A glowing lantern core hanging in the middle of the void: one landmark
            /// every face can see (and grapple).</summary>
            void Heart()
            {
                void H(string n, Vector3 c, Vector3 s, int mat) => Layout.Boxes.Add(new PavilionBox
                {
                    Name = $"Heart {n}", Center = c, Size = s, Rotation = Quaternion.identity,
                    Material = (PavilionMat)mat, Collide = true, Visible = true, BreakId = -1,
                });
                H("Core", Vector3.zero, Vector3.one * 4f, MatHeart);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        H("Frame", new Vector3(sx * 2.05f, 0, sz * 2.05f), new Vector3(0.35f, 4.6f, 0.35f), MatWood);
                H("Cap", new Vector3(0, 2.4f, 0), new Vector3(4.8f, 0.3f, 4.8f), MatWood);
                H("Cap", new Vector3(0, -2.4f, 0), new Vector3(4.8f, 0.3f, 4.8f), MatWood);
                Layout.Lights.Add(new PavilionLight { Position = Vector3.zero, Range = 48f, Intensity = 1.6f, Color = new Color(1f, 0.92f, 0.8f) });
            }

            // ---------------------------------------------------------------- one face (its own frame: surface at y = 0, up = +y)

            void BuildFace()
            {
                // Tinted tatami over this face's part of the shell, with a lacquered border.
                B("Tatami", new Vector3(0, 0.02f, 0), new Vector3(44f, 0.04f, 44f), Tatami, collide: false);
                for (int s = -1; s <= 1; s += 2)
                {
                    B("Border", new Vector3(0, 0.06f, s * 22f), new Vector3(44.3f, 0.12f, 0.3f), Lacquer, collide: false);
                    B("Border", new Vector3(s * 22f, 0.06f, 0), new Vector3(0.3f, 0.12f, 44.3f), Lacquer, collide: false);
                }

                Shrine();
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Tower(sx, sz);
                for (int sx = -1; sx <= 1; sx += 2) Bridge(sx * 15f);
                Overlook();
                GroundDoor();
                Cover();
            }

            /// <summary>The objective: a raised stone shrine under a pillared roof, ringed by
            /// low breakable screens.</summary>
            void Shrine()
            {
                B("Shrine Base", new Vector3(0, 0.4f, 0), new Vector3(10f, 0.8f, 10f), MatStone);
                for (int s = -1; s <= 1; s += 2)
                {
                    B("Shrine Glow", new Vector3(0, 0.82f, s * 4.6f), new Vector3(9.2f, 0.05f, 0.2f), Lantern, collide: false);
                    B("Shrine Glow", new Vector3(s * 4.6f, 0.82f, 0), new Vector3(0.2f, 0.05f, 9.2f), Lantern, collide: false);
                }
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        B("Shrine Pillar", new Vector3(sx * 4.2f, 4.3f, sz * 4.2f), new Vector3(1f, 7f, 1f), Lacquer);
                B("Shrine Roof", new Vector3(0, 8.05f, 0), new Vector3(12f, 0.5f, 12f), MatWood);
                B("Shrine Roof Edge", new Vector3(0, 8.4f, 0), new Vector3(12.3f, 0.2f, 12.3f), Lacquer, collide: false);
                B("Shrine Lantern", new Vector3(0, 6.6f, 0), new Vector3(0.9f, 1.3f, 0.9f), Lantern, collide: false);
                Layout.Lights.Add(new PavilionLight { Position = W(new Vector3(0, 6f, 0)), Range = 22f, Intensity = 2.6f, Color = LightColor });

                // Low screens round it: close cover that shots and blasts tear through.
                for (int s = -1; s <= 1; s += 2)
                {
                    B("Shrine Screen", new Vector3(0, 1f, s * 7.2f), new Vector3(3.6f, 2f, 0.12f), Paper, breakable: true);
                    B("Shrine Screen", new Vector3(s * 7.2f, 1f, 0), new Vector3(0.12f, 2f, 3.6f), Paper, breakable: true);
                    for (int e = -1; e <= 1; e += 2)
                    {
                        B("Screen Post", new Vector3(e * 1.9f, 1.1f, s * 7.2f), new Vector3(0.16f, 2.2f, 0.16f), MatWood);
                        B("Screen Post", new Vector3(s * 7.2f, 1.1f, e * 1.9f), new Vector3(0.16f, 2.2f, 0.16f), MatWood);
                    }
                }
            }

            const float TowerC = 15f, TowerHalf = 4.5f, Tier2 = 4.5f, Tier3 = 8f;

            /// <summary>A corner tower: a tatami room with breakable shoji walls (doorways toward
            /// the shrine), its roof a second tier reached by an outside staircase.</summary>
            void Tower(int sx, int sz)
            {
                float cx = sx * TowerC, cz = sz * TowerC, h = TowerHalf;
                for (int px = -1; px <= 1; px += 2)
                    for (int pz = -1; pz <= 1; pz += 2)
                        B("Tower Post", new Vector3(cx + px * 4.25f, 2.05f, cz + pz * 4.25f), new Vector3(0.5f, 4.1f, 0.5f), Lacquer);

                // Walls. x-sides run along z at x = cx +- 4.35; z-sides run along x at z = cz +- 4.35.
                for (int side = -1; side <= 1; side += 2)
                {
                    bool innerX = side == -sx, innerZ = side == -sz; // the sides facing the shrine get doorways
                    WallSide(new Vector3(cx + side * 4.35f, 0, cz), Vector3.forward, innerX);
                    WallSide(new Vector3(cx, 0, cz + side * 4.35f), Vector3.right, innerZ);
                }

                B("Tower Roof", new Vector3(cx, 4.3f, cz), new Vector3(9f, 0.4f, 9f), MatWood);
                B("Tower Floor", new Vector3(cx, 0.04f, cz), new Vector3(8.4f, 0.04f, 8.4f), Tatami, collide: false);
                B("Low Table", new Vector3(cx + sx * 1.5f, 0.3f, cz + sz * 1.5f), new Vector3(1.6f, 0.6f, 0.9f), Lacquer);
                B("Room Lantern", new Vector3(cx, 3.2f, cz), new Vector3(0.5f, 0.7f, 0.5f), Lantern, collide: false);
                Layout.Lights.Add(new PavilionLight { Position = W(new Vector3(cx, 3f, cz)), Range = 11f, Intensity = 1.8f, Color = LightColor });

                // Roof rails: none on the outer x edge (the staircase arrives there), a gap in the
                // inner z edge where the bridge joins.
                Rail(new Vector3(cx - sx * h, Tier2, cz), 9f, Vector3.forward);                 // inner x edge
                Rail(new Vector3(cx, Tier2, cz + sz * h), 9f, Vector3.right);                   // outer z edge
                Rail(new Vector3(cx - 2.95f, Tier2, cz - sz * h), 3.1f, Vector3.right);         // inner z edge, left of the bridge
                Rail(new Vector3(cx + 2.95f, Tier2, cz - sz * h), 3.1f, Vector3.right);         // ...and right of it

                // Outside staircase up the outer x side, climbing toward the shrine side.
                Stair(new Vector3(cx + sx * 5.8f, 0f, cz + sz * 2.25f), new Vector3(0, 0, -sz), Tier2, 2.4f);
                // A landing at the top, level with the roof (step across onto it), railed on the open sides.
                float landZ = cz - sz * 3.05f;
                B("Stair Landing", new Vector3(cx + sx * 5.8f, Tier2 - 0.2f, landZ), new Vector3(2.4f, 0.4f, 1.6f), MatWood);
                Rail(new Vector3(cx + sx * 5.8f, Tier2, cz - sz * 3.85f), 2.4f, Vector3.right);
                Rail(new Vector3(cx + sx * 6.95f, Tier2, landZ), 1.6f, Vector3.forward);

                if (sz < 0)
                {
                    // The two towers away from the doors are where people spawn.
                    Vector3 facing = new Vector3(-sx, 0, -sz).normalized;
                    Layout.Spawns.Add(new PavilionMarker
                    {
                        Position = W(new Vector3(cx - sx * 1.5f, 0.95f, cz - sz * 1.5f)),
                        Rotation = rot * PavilionLayout.Basis(facing, Vector3.up),
                    });
                }
                else if (sx < 0)
                {
                    // Door 1: on this tower's roof, at the outer edge, looking across to the shrine.
                    B("Door Panel", new Vector3(cx - 4.35f, Tier2 + 2f, cz + 1.2f), new Vector3(0.3f, 4f, 3.2f), MatWood);
                    B("Door Frame", new Vector3(cx - 4.18f, Tier2 + 3.75f, cz + 1.2f), new Vector3(0.06f, 0.25f, 2.8f), Lacquer, collide: false);
                    AddDoor(1, new Vector3(cx - 4.19f, Tier2 + 1.7f, cz + 1.2f), Vector3.right, new Vector2(2.4f, 3.4f));
                }
            }

            /// <summary>One wall of a tower room: breakable shoji panels between posts, a beam on
            /// top, and (facing the shrine) a doorway with a lintel.</summary>
            void WallSide(Vector3 center, Vector3 along, bool doorway)
            {
                Vector3 Size(float length, float height, float thick) => along.x != 0f ? new Vector3(length, height, thick) : new Vector3(thick, height, length);
                B("Wall Beam", center + Vector3.up * 3.8f, Size(9f, 0.6f, 0.3f), MatWood);
                if (doorway)
                {
                    for (int e = -1; e <= 1; e += 2)
                        B("Shoji", center + along * (e * 2.6f) + Vector3.up * 1.75f, Size(2.8f, 3.5f, 0.12f), Paper, breakable: true);
                    B("Lintel", center + Vector3.up * 3.15f, Size(2.4f, 0.7f, 0.3f), MatWood);
                }
                else
                {
                    for (int e = -1; e <= 1; e += 2)
                        B("Shoji", center + along * (e * 2.05f) + Vector3.up * 1.75f, Size(3.9f, 3.5f, 0.12f), Paper, breakable: true);
                    B("Wall Post", center + Vector3.up * 1.75f, Size(0.2f, 3.5f, 0.2f), MatWood);
                }
            }

            /// <summary>A lantern-lit walkway between the two towers on one side (x = <paramref name="x"/>),
            /// on the second tier, with the ground lane running underneath.</summary>
            void Bridge(float x)
            {
                float z0 = -TowerC + TowerHalf, z1 = TowerC - TowerHalf, len = z1 - z0;
                B("Bridge Deck", new Vector3(x, Tier2 - 0.2f, 0), new Vector3(2.6f, 0.4f, len), MatWood);
                B("Bridge Beam", new Vector3(x, Tier2 - 0.6f, 0), new Vector3(0.6f, 0.4f, len), Lacquer);
                for (int s = -1; s <= 1; s += 2)
                {
                    Rail(new Vector3(x + s * 1.24f, Tier2, 0), len, Vector3.forward);
                    for (float z = z0 + 2f; z < z1 - 1f; z += 4f)
                    {
                        B("Bridge Lantern Post", new Vector3(x + s * 1.24f, Tier2 + 0.65f, z), new Vector3(0.12f, 1.3f, 0.12f), MatWood);
                        B("Bridge Lantern", new Vector3(x + s * 1.24f, Tier2 + 1.45f, z), new Vector3(0.3f, 0.32f, 0.3f), Lantern, collide: false);
                    }
                }
            }

            /// <summary>Third tier: a platform on the (+1, +1) tower's roof, up a second stair,
            /// with door 0 looking out over the face.</summary>
            void Overlook()
            {
                float cx = TowerC, cz = TowerC;
                const float x0 = 14.5f, x1 = 19.5f, z0 = 15.25f, z1 = 19.5f;
                float mx = (x0 + x1) * 0.5f, mz = (z0 + z1) * 0.5f;
                B("Overlook", new Vector3(mx, Tier3 - 0.2f, mz), new Vector3(x1 - x0, 0.4f, z1 - z0), MatWood);
                foreach (var (px, pz) in new[] { (x0 + 0.2f, z0 + 0.2f), (x1 - 0.15f, z0 + 0.2f), (x0 + 0.2f, z1 - 0.2f), (x1 - 0.2f, z1 - 0.2f) })
                    B("Overlook Post", new Vector3(px, (Tier2 + Tier3 - 0.4f) * 0.5f, pz), new Vector3(0.4f, Tier3 - 0.4f - Tier2, 0.4f), Lacquer);
                // Starts 1.25 m in from the roof edge, so there is room to walk up to it.
                Stair(new Vector3(18f, Tier2, z0 - (Tier3 - Tier2)), Vector3.forward, Tier3 - Tier2, 2.4f);

                Rail(new Vector3(x0, Tier3, mz), z1 - z0, Vector3.forward);                  // inner x edge
                Rail(new Vector3(mx, Tier3, z1), x1 - x0, Vector3.right);                    // outer z edge
                Rail(new Vector3((x0 + 16.7f) * 0.5f, Tier3, z0), 16.7f - x0, Vector3.right); // stair side, beside the stair
                Rail(new Vector3(x1, Tier3, (z0 + 16.1f) * 0.5f), 16.1f - z0, Vector3.forward); // door side, either side of the panel
                Rail(new Vector3(x1, Tier3, (18.7f + z1) * 0.5f), z1 - 18.7f, Vector3.forward);

                B("Door Panel", new Vector3(19.3f, Tier3 + 1.8f, 17.4f), new Vector3(0.3f, 3.6f, 2.6f), MatWood);
                B("Door Frame", new Vector3(19.13f, Tier3 + 3.35f, 17.4f), new Vector3(0.06f, 0.25f, 2.2f), Lacquer, collide: false);
                AddDoor(0, new Vector3(19.14f, Tier3 + 1.5f, 17.4f), Vector3.left, new Vector2(2f, 3f));
                Layout.Lights.Add(new PavilionLight { Position = W(new Vector3(mx, Tier3 + 2.5f, mz)), Range = 9f, Intensity = 1.4f, Color = LightColor });
            }

            /// <summary>Door 2: a free-standing panel at the edge of the face, at the end of a lane
            /// to the shrine flanked by low walls.</summary>
            void GroundDoor()
            {
                B("Door Panel", new Vector3(0, 2f, -19.7f), new Vector3(3.2f, 4f, 0.4f), MatWood);
                B("Door Frame", new Vector3(0, 3.75f, -19.48f), new Vector3(2.8f, 0.25f, 0.06f), Lacquer, collide: false);
                AddDoor(2, new Vector3(0, 1.7f, -19.49f), Vector3.forward, new Vector2(2.4f, 3.4f));
                for (int s = -1; s <= 1; s += 2)
                    B("Low Wall", new Vector3(s * 4f, 0.6f, -15f), new Vector3(3f, 1.2f, 0.6f), MatStone);
            }

            void Cover()
            {
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        B("Pillar", new Vector3(sx * 12f, 2f, sz * 7f), new Vector3(1.2f, 4f, 1.2f), Lacquer);
                        B("Stone Lantern", new Vector3(sx * 21f, 0.7f, sz * 21f), new Vector3(0.6f, 1.4f, 0.6f), MatStone);
                        B("Lantern Light", new Vector3(sx * 21f, 1.6f, sz * 21f), new Vector3(0.45f, 0.4f, 0.45f), Lantern, collide: false);
                    }
                for (int sx = -1; sx <= 1; sx += 2)
                    Layout.Props.Add(new PavilionMarker { Position = W(new Vector3(sx * 9f, 0.5f, 0)), Rotation = rot });
            }

            // ---------------------------------------------------------------- pieces

            /// <summary>A waist-high rail standing on <paramref name="baseCenter"/>.</summary>
            void Rail(Vector3 baseCenter, float length, Vector3 along)
            {
                Vector3 Size(float l, float h, float t) => along.x != 0f ? new Vector3(l, h, t) : new Vector3(t, h, l);
                B("Rail", baseCenter + Vector3.up * 0.95f, Size(length, 0.1f, 0.12f), MatWood);
                for (float a = -length * 0.5f + 0.3f; a < length * 0.5f; a += 2f)
                    B("Rail Post", baseCenter + along * a + Vector3.up * 0.47f, Size(0.12f, 0.95f, 0.12f), MatWood);
            }

            /// <summary>A 45-degree flight climbing along <paramref name="run"/> from <paramref name="bottom"/>,
            /// real steps over a hidden walking ramp.</summary>
            void Stair(Vector3 bottom, Vector3 run, float rise, float width)
            {
                const float step = 0.25f, thickness = 0.3f;
                int count = Mathf.RoundToInt(rise / step);
                Vector3 up = Vector3.up, side = Vector3.Cross(up, run);
                Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
                for (int i = 0; i < count; i++)
                {
                    float v0 = i * step - 0.4f, v1 = (i + 1) * step;
                    Vector3 c = bottom + run * ((i + 0.5f) * step) + up * ((v0 + v1) * 0.5f);
                    B($"Step {i + 1}", c, Abs(side) * width + up * (v1 - v0) + Abs(run) * step, i % 2 == 0 ? MatWood : Lacquer, collide: i == count - 1);
                }
                Vector3 a = bottom - run * step, b = bottom + run * (rise - step) + up * rise;
                Vector3 n = (up - run).normalized;
                Layout.Boxes.Add(new PavilionBox
                {
                    Name = $"{FaceNames[face]}: Walkable Ramp (hidden)",
                    Center = W((a + b) * 0.5f - n * thickness * 0.5f),
                    Size = new Vector3(width, thickness, (b - a).magnitude),
                    Rotation = rot * PavilionLayout.Basis((b - a).normalized, n),
                    Collide = true, Visible = false, StairRamp = true, StairOrigin = W(bottom), BreakId = -1,
                });
            }

            void AddDoor(int slot, Vector3 position, Vector3 normal, Vector2 size)
            {
                doors[(face, slot)] = new PavilionPortal
                {
                    Name = $"{FaceNames[face]} Door {slot}",
                    Position = W(position), Normal = Dir(normal), Up = Dir(Vector3.up),
                    Size = size, Doorway = true, Link = -1,
                };
            }

            /// <summary>Faces {floor, north, east} each link to faces {ceiling, south, west}, one door
            /// per pair, through matching slots: every face has three ways out, to three others.</summary>
            void LinkDoors()
            {
                int[] xs = { 0, 2, 4 }, ys = { 1, 3, 5 };
                int pair = 0;
                for (int xi = 0; xi < 3; xi++)
                    for (int yi = 0; yi < 3; yi++, pair++)
                    {
                        int slot = (xi + yi) % 3;
                        var a = doors[(xs[xi], slot)];
                        var b = doors[(ys[yi], slot)];
                        a.Color = b.Color = PairColors[pair];
                        int ia = Layout.Portals.Count;
                        a.Link = ia + 1;
                        b.Link = ia;
                        Layout.Portals.Add(a);
                        Layout.Portals.Add(b);
                    }
            }
        }
    }
}
