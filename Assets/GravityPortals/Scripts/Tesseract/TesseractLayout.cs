using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>Sizes for The Tesseract, kept in one place (the TesseractSettings asset holds one;
    /// change it, then rebuild the map).</summary>
    [System.Serializable]
    public sealed class TesseractConfig
    {
        [Tooltip("Inside size of every room (they are all cubes), metres.")]
        public float roomSize = 20f;
        [Tooltip("Door width, along the room edge it stands on, metres.")]
        public float doorWidth = 4f;
        [Tooltip("Door height, from the room edge it stands on into its face, metres.")]
        public float doorHeight = 4.5f;
        [Tooltip("Thickness of the room walls, metres.")]
        public float wallThickness = 1f;
        [Tooltip("Distance between room centres. Rooms only meet through their doors, so keep them far apart.")]
        public float roomSpacing = 200f;
        [Tooltip("Radius of the curved room edges and corners you walk round onto the walls, metres (bigger = a gentler turn, less flat floor).")]
        public float edgeRadius = 3f;
        [Tooltip("How many doors deep you can see through doors (1 = only the room behind, 3 = two rooms further).")]
        [Range(1, 3)] public int portalRecursion = 3;

        public float Half => roomSize * 0.5f;
        /// <summary>Half the length of the straight gap left in a curved edge where a door stands.</summary>
        public float DoorGapHalf => doorWidth * 0.5f + 0.35f;
    }

    /// <summary>
    /// "The Tesseract": eight cube rooms joined like the eight cells of a 4D hypercube.
    ///
    /// Each room is one cell, named by the 4D axis it sits on: East/West = +x/-x, Above/Below =
    /// +y/-y, North/South = +z/-z, Shell/Core = +w/-w. Two rooms on the same axis are opposite and
    /// never meet; every other pair shares a face, so each room has exactly six doors, one per face
    /// (24 connections in all, listed in <see cref="Connections"/>).
    ///
    /// Inside a room the three 4D axes other than its own run along world X, Y and Z, as given by
    /// its <see cref="RoomDef.Frame"/>. The frames are the cells' true orientations, so every door
    /// pair joins its rooms exactly the way the cells are glued in 4D: walking through keeps your
    /// "up" relative to the room (the gravity rule carries it through with no roll at all).
    ///
    /// Each door stands against one edge of its face (centred along it), so for anyone standing on
    /// the face next to that edge it is an ordinary floor-level door. That neighbouring face is the
    /// connection's <see cref="Connection.Floor"/>: you walk in standing on it and come out standing
    /// on the same face of the other room. (Doors in the middle of the faces would put wall doors
    /// out of reach, and every drop through a floor door would land on the opposite face's door and
    /// fall on round the 4th dimension forever.) The floors are chosen so that in every room the
    /// two opposite faces never use the same edge (a drop never lands on a door), no room edge holds
    /// two doors, and every face is the floor of exactly one door.
    ///
    /// Every room's edges and corners are rounded (<see cref="TesseractConfig.edgeRadius"/>) and the
    /// map's <see cref="RoomField"/> (<see cref="Field"/>) makes down follow them, so you can walk off
    /// the floor onto any wall. Along each door the edge stays sharp, so the door sits flush on its floor.
    ///
    /// Pure data (built by <see cref="PavilionBuilder"/>); <see cref="Validate"/> checks all of it and
    /// <see cref="Report"/> lists every door. Engine-only calls are avoided, so both run outside Unity.
    /// </summary>
    public static class TesseractLayout
    {
        public const int AxisX = 0, AxisY = 1, AxisZ = 2, AxisW = 3;
        const string AxisLetters = "xyzw";

        public sealed class RoomDef
        {
            public string Name;
            /// <summary>The 4D axis and side this cell sits on.</summary>
            public int Axis, Sign;
            /// <summary>Which 4D direction runs along world X, Y and Z inside this room, e.g. "+w +y +z".</summary>
            public string Frame;
            public Color Color;
        }

        /// <summary>The eight rooms. Room i's walls use material i.</summary>
        public static readonly RoomDef[] Rooms =
        {
            new RoomDef { Name = "Core",  Axis = AxisW, Sign = -1, Frame = "+x +y +z", Color = new Color(1f, 0.82f, 0.35f) },
            // The "outside" cell is the inside-out one: its frame is the Core's mirrored in X.
            new RoomDef { Name = "Shell", Axis = AxisW, Sign = +1, Frame = "-x +y +z", Color = new Color(0.78f, 0.84f, 0.95f) },
            // Wedges: the face toward the Core is on the side toward the middle of the usual drawing.
            new RoomDef { Name = "East",  Axis = AxisX, Sign = +1, Frame = "+w +y +z", Color = new Color(1f, 0.45f, 0.2f) },
            new RoomDef { Name = "West",  Axis = AxisX, Sign = -1, Frame = "-w +y +z", Color = new Color(0.2f, 0.85f, 0.8f) },
            new RoomDef { Name = "North", Axis = AxisZ, Sign = +1, Frame = "+x +y +w", Color = new Color(0.35f, 0.55f, 1f) },
            new RoomDef { Name = "South", Axis = AxisZ, Sign = -1, Frame = "+x +y -w", Color = new Color(0.45f, 0.95f, 0.35f) },
            new RoomDef { Name = "Above", Axis = AxisY, Sign = +1, Frame = "+x +w +z", Color = new Color(0.75f, 0.45f, 1f) },
            new RoomDef { Name = "Below", Axis = AxisY, Sign = -1, Frame = "+x -w +z", Color = new Color(1f, 0.35f, 0.6f) },
        };

        public struct Connection
        {
            public string A, B;
            /// <summary>The room whose shared face is the floor you walk through this door on.</summary>
            public string Floor;
            public Connection(string a, string b, string floor) { A = a; B = b; Floor = floor; }
        }

        /// <summary>Every door pair. Opposite rooms (same axis) must never appear here.</summary>
        public static readonly Connection[] Connections =
        {
            new Connection("Core", "East", "Below"),
            new Connection("Core", "West", "South"),
            new Connection("Core", "North", "East"),
            new Connection("Core", "South", "Above"),
            new Connection("Core", "Above", "West"),
            new Connection("Core", "Below", "North"),
            new Connection("Shell", "East", "Above"),
            new Connection("Shell", "West", "North"),
            new Connection("Shell", "North", "Below"),
            new Connection("Shell", "South", "West"),
            new Connection("Shell", "Above", "South"),
            new Connection("Shell", "Below", "East"),
            new Connection("East", "North", "Shell"),
            new Connection("East", "South", "Core"),
            new Connection("East", "Above", "North"),
            new Connection("East", "Below", "South"),
            new Connection("West", "North", "Above"),
            new Connection("West", "South", "Below"),
            new Connection("West", "Above", "Shell"),
            new Connection("West", "Below", "Core"),
            new Connection("North", "Above", "Core"),
            new Connection("North", "Below", "West"),
            new Connection("South", "Above", "East"),
            new Connection("South", "Below", "Shell"),
        };

        public static int MatWall(int room) => room;
        /// <summary>Glowing trim in a room's colour: its curved edges, and the surrounds of the doors leading to it.</summary>
        public static int MatGlow(int room) => Rooms.Length + room;
        public const int MatBeacon = 16;
        public static int MaterialCount => 17;

        public const string SpawnRoom = "Shell";
        /// <summary>Teams will spawn on these two opposite faces of the Shell (for now everyone does).</summary>
        public static readonly string[] SpawnFaces = { "Below", "Above" };

        /// <summary>One side of a door pair.</summary>
        public struct Door
        {
            public int Room, Target, Floor;
            public int Portal, Link;      // indices into the layout's portals
            public Vector3 Position, Normal, Up;
            public Vector2 Size;
        }

        // ------------------------------------------------------------------ 4D helpers

        public static int RoomIndex(string name)
        {
            for (int i = 0; i < Rooms.Length; i++)
                if (Rooms[i].Name == name) return i;
            return -1;
        }

        public static bool Opposite(int a, int b) => Rooms[a].Axis == Rooms[b].Axis && Rooms[a].Sign != Rooms[b].Sign;

        /// <summary>Do rooms a and b share a face (different axes)?</summary>
        public static bool Neighbours(int a, int b) => Rooms[a].Axis != Rooms[b].Axis;

        public static Vector3 Centre(int room, TesseractConfig c) =>
            new Vector3((room % 4) * c.roomSpacing, 0f, (room / 4) * c.roomSpacing);

        public static Vector4 Axis4(int axis, float length)
        {
            var v = Vector4.zero;
            v[axis] = length;
            return v;
        }

        /// <summary>The 4D unit vector pointing from a room's middle to its face shared with <paramref name="other"/>.</summary>
        public static Vector4 Toward(int other) => Axis4(Rooms[other].Axis, Rooms[other].Sign);

        static Vector4[][] frames;

        /// <summary>The 4D directions along world X, Y, Z in a room (null if its Frame doesn't parse).</summary>
        public static Vector4[] Frame(int room)
        {
            if (frames == null)
            {
                frames = new Vector4[Rooms.Length][];
                for (int i = 0; i < Rooms.Length; i++) frames[i] = ParseFrame(Rooms[i].Frame);
            }
            return frames[room];
        }

        static Vector4[] ParseFrame(string text)
        {
            var parts = (text ?? "").Split(' ');
            if (parts.Length != 3) return null;
            var f = new Vector4[3];
            for (int k = 0; k < 3; k++)
            {
                if (parts[k].Length != 2) return null;
                float sign = parts[k][0] == '+' ? 1f : parts[k][0] == '-' ? -1f : 0f;
                int axis = AxisLetters.IndexOf(parts[k][1]);
                if (sign == 0f || axis < 0) return null;
                f[k] = Axis4(axis, sign);
            }
            return f;
        }

        /// <summary>A 4D direction (along the room's own three axes) as a world direction in that room.</summary>
        public static Vector3 Dir(int room, Vector4 v)
        {
            var f = Frame(room);
            return new Vector3(Vector4.Dot(v, f[0]), Vector4.Dot(v, f[1]), Vector4.Dot(v, f[2]));
        }

        /// <summary>A world direction in a room as a 4D direction.</summary>
        public static Vector4 Dir4(int room, Vector3 v)
        {
            var f = Frame(room);
            return f[0] * v.x + f[1] * v.y + f[2] * v.z;
        }

        /// <summary>The world direction from a room's middle toward its face shared with <paramref name="other"/>.</summary>
        public static Vector3 FaceDir(int room, int other) => Dir(room, Toward(other));

        static Vector3 Point(int room, Vector4 v, TesseractConfig c) => Centre(room, c) + Dir(room, v);

        // ------------------------------------------------------------------ doors

        /// <summary>Both sides of every valid connection: connection k gives doors (portals) 2k and 2k+1.
        /// Invalid connections (see <see cref="Validate"/>) are skipped.</summary>
        public static List<Door> Doors(TesseractConfig c)
        {
            var doors = new List<Door>();
            for (int k = 0; k < Connections.Length; k++)
            {
                int a = RoomIndex(Connections[k].A), b = RoomIndex(Connections[k].B), g = RoomIndex(Connections[k].Floor);
                if (!ValidConnection(a, b, g)) continue;
                int i = doors.Count;
                doors.Add(MakeDoor(a, b, g, i, i + 1, c));
                doors.Add(MakeDoor(b, a, g, i + 1, i, c));
            }
            return doors;
        }

        static bool ValidConnection(int a, int b, int g) =>
            a >= 0 && b >= 0 && g >= 0 && Neighbours(a, b) && Neighbours(a, g) && Neighbours(b, g)
            && Frame(a) != null && Frame(b) != null;

        /// <summary>The door in <paramref name="room"/> on its face toward <paramref name="target"/>,
        /// standing against that face's edge with <paramref name="floor"/>'s face.</summary>
        static Door MakeDoor(int room, int target, int floor, int portal, int link, TesseractConfig c)
        {
            float h = c.Half;
            Vector4 face = Toward(target), down = Toward(floor);
            Vector4 centre = face * h + down * (h - c.doorHeight * 0.5f);
            return new Door
            {
                Room = room, Target = target, Floor = floor, Portal = portal, Link = link,
                Position = Point(room, centre, c),
                Normal = Dir(room, -face), // out of the face, into the room
                Up = Dir(room, -down),     // away from the floor it stands on
                Size = new Vector2(c.doorWidth, c.doorHeight),
            };
        }

        // ------------------------------------------------------------------ the map

        public static PavilionLayout Generate(TesseractConfig c)
        {
            var layout = new PavilionLayout { Seed = 0 };
            float h = c.Half, t = c.wallThickness, span = c.roomSize + 2f * t;
            var doors = Doors(c);

            for (int r = 0; r < Rooms.Length; r++)
            {
                Vector3 o = Centre(r, c);
                string n = Rooms[r].Name;
                // A closed box of six slabs that don't overlap: X slabs cover the edges, then Y, then Z.
                Wall(layout, $"{n} wall +X", o + new Vector3(h + t * 0.5f, 0f, 0f), new Vector3(t, span, span), r);
                Wall(layout, $"{n} wall -X", o - new Vector3(h + t * 0.5f, 0f, 0f), new Vector3(t, span, span), r);
                Wall(layout, $"{n} wall +Y", o + new Vector3(0f, h + t * 0.5f, 0f), new Vector3(c.roomSize, t, span), r);
                Wall(layout, $"{n} wall -Y", o - new Vector3(0f, h + t * 0.5f, 0f), new Vector3(c.roomSize, t, span), r);
                Wall(layout, $"{n} wall +Z", o + new Vector3(0f, 0f, h + t * 0.5f), new Vector3(c.roomSize, c.roomSize, t), r);
                Wall(layout, $"{n} wall -Z", o - new Vector3(0f, 0f, h + t * 0.5f), new Vector3(c.roomSize, c.roomSize, t), r);

                // One strong light in the middle in the room's own colour (the glowing trim does the rest:
                // forward rendering lights each mesh with only a few lights, so more lamps would flicker in and out).
                layout.Lights.Add(new PavilionLight
                {
                    Position = o, Range = c.roomSize * 1.7f, Intensity = 2.6f,
                    Color = Color.Lerp(Color.white, Rooms[r].Color, 0.55f),
                });

                int count = 0;
                foreach (var d in doors) if (d.Room == r) count++;
                layout.Rooms.Add(new PavilionRoomInfo
                {
                    Name = n, Bounds = new Bounds(o, Vector3.one * c.roomSize), Up = Vector3.up, Doors = count,
                });
            }

            foreach (var d in doors)
            {
                layout.Portals.Add(new PavilionPortal
                {
                    Name = $"{Rooms[d.Room].Name} to {Rooms[d.Target].Name}",
                    Position = d.Position, Normal = d.Normal, Up = d.Up, Size = d.Size,
                    Link = d.Link,
                    Color = Rooms[d.Target].Color, // the frame shows the colour of the room behind it
                    Doorway = true,
                });
            }

            foreach (var d in doors) Surround(layout, d);
            Beacon(layout, c);

            for (int r = 0; r < Rooms.Length; r++) Curves(layout, r, doors, c);
            Spawns(layout, doors, c);
            Crates(layout, doors, c);
            return layout;
        }

        public const string HillRoom = "Core";

        /// <summary>Which team a spawn belongs to: the index of its face in <see cref="SpawnFaces"/>
        /// (its "down" points into that face of the Shell), or -1.</summary>
        public static int SpawnTeam(Vector3 up)
        {
            int shell = RoomIndex(SpawnRoom);
            for (int i = 0; i < SpawnFaces.Length; i++)
                if (Vector3.Dot(-up, FaceDir(shell, RoomIndex(SpawnFaces[i]))) > 0.99f) return i;
            return -1;
        }
        const float SurroundWidth = 0.22f, SurroundGap = 0.06f, SurroundDepth = 0.04f;

        /// <summary>A glowing surround on the wall round a door (both sides and the top; the bottom is
        /// the floor), in the colour of the room it leads to.</summary>
        static void Surround(PavilionLayout layout, Door d)
        {
            Vector3 right = Vector3.Cross(d.Up, d.Normal);
            float w = d.Size.x, h = d.Size.y, side = SurroundGap + SurroundWidth;
            Vector3 face = d.Position + d.Normal * (SurroundDepth * 0.5f);
            int mat = MatGlow(d.Target);
            string n = $"{Rooms[d.Room].Name} to {Rooms[d.Target].Name} surround";
            foreach (float sign in new[] { -1f, 1f })
                // Sides: from the floor up to the top bar, which covers the corners.
                Trim(layout, n, face + right * sign * (w * 0.5f + SurroundGap + SurroundWidth * 0.5f) + d.Up * (SurroundGap * 0.5f),
                    right, SurroundWidth, d.Up, h + SurroundGap, d.Normal, mat);
            Trim(layout, n, face + d.Up * (h * 0.5f + SurroundGap + SurroundWidth * 0.5f), right, w + 2f * side, d.Up, SurroundWidth, d.Normal, mat);
        }

        /// <summary>A non-solid, axis-aligned strip: lengths along three perpendicular axis directions.</summary>
        static void Trim(PavilionLayout layout, string name, Vector3 centre, Vector3 a, float la, Vector3 b, float lb, Vector3 n, int mat)
        {
            Vector3 size = Abs(a) * la + Abs(b) * lb + Abs(n) * SurroundDepth;
            layout.Boxes.Add(new PavilionBox
            {
                Name = name, Center = centre, Size = size, Rotation = Quaternion.identity,
                Material = (PavilionMat)mat, Collide = false, Visible = true,
            });
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>The objective: a glowing tilted cube floating in the middle of the Core (not solid).</summary>
        static void Beacon(PavilionLayout layout, TesseractConfig c)
        {
            Vector3 forward = new Vector3(1f, 1f, 0f).normalized, up = new Vector3(-1f, 1f, 1f).normalized;
            layout.Boxes.Add(new PavilionBox
            {
                Name = "Core beacon", Center = Centre(RoomIndex(HillRoom), c), Size = Vector3.one * 1.6f,
                Rotation = PavilionLayout.Basis(forward, up), Material = (PavilionMat)MatBeacon, Collide = false, Visible = true,
            });
        }

        /// <summary>One crate per room, on the flat of its world-down face clear of the doors (to
        /// throw, shoot and push through them).</summary>
        static void Crates(PavilionLayout layout, List<Door> doors, TesseractConfig c)
        {
            float flat = c.Half - c.edgeRadius - 1f;
            var spots = new[] { new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, -0.5f), Vector2.zero };
            for (int r = 0; r < Rooms.Length; r++)
            {
                foreach (var spot in spots)
                {
                    Vector3 pos = Centre(r, c) + new Vector3(spot.x * flat, -c.Half + 0.45f, spot.y * flat);
                    if (NearDoor(pos, doors, 2.5f)) continue;
                    layout.Props.Add(new PavilionMarker { Position = pos, Rotation = Quaternion.identity });
                    break;
                }
            }
        }

        /// <summary>The gravity field for the map: rounded rooms, sharp where the doors stand.</summary>
        public static RoomField Field(TesseractConfig c)
        {
            var doors = Doors(c);
            var field = new RoomField
            {
                centres = new Vector3[Rooms.Length],
                half = c.Half,
                edgeRadius = c.edgeRadius,
                sharpEdges = new RoomField.SharpEdge[doors.Count],
            };
            for (int r = 0; r < Rooms.Length; r++) field.centres[r] = Centre(r, c);
            for (int i = 0; i < doors.Count; i++)
                field.sharpEdges[i] = new RoomField.SharpEdge
                {
                    room = doors[i].Room, faceA = -doors[i].Normal, faceB = FaceDir(doors[i].Room, doors[i].Floor),
                    halfLength = c.DoorGapHalf,
                };
            return field;
        }

        // ------------------------------------------------------------------ curved edges

        const int ArcSteps = 12; // 7.5 degrees per step round every curve

        static readonly Vector3[] WorldAxes = { Vector3.right, Vector3.up, Vector3.forward };

        /// <summary>The door in a room standing in the edge between faces a and b, if any.</summary>
        static bool DoorEdge(int room, Vector3 a, Vector3 b, List<Door> doors)
        {
            foreach (var d in doors)
            {
                if (d.Room != room) continue;
                Vector3 face = -d.Normal, floor = FaceDir(room, d.Floor);
                if ((Same(face, a) && Same(floor, b)) || (Same(face, b) && Same(floor, a))) return true;
            }
            return false;
        }

        static bool Same(Vector3 u, Vector3 v) => (u - v).sqrMagnitude < 1e-4f;

        /// <summary>A room's rounded edges (quarter-pipes) and corners (sphere caps), so you can walk
        /// onto any wall. Where a door stands against an edge, the edge stays sharp along the door,
        /// with flat end caps (door jambs) closing off the curve.</summary>
        static void Curves(PavilionLayout layout, int room, List<Door> doors, TesseractConfig c)
        {
            Vector3 o = Centre(room, c);
            float s = c.Half - c.edgeRadius, R = c.edgeRadius, gap = c.DoorGapHalf;
            float uvScale = 1f / (c.roomSize + 2f * c.wallThickness); // matches the walls' grid
            var curves = new PavilionSurface { Name = $"{Rooms[room].Name} curves", Material = (PavilionMat)MatWall(room) };
            var jambs = new PavilionSurface { Name = $"{Rooms[room].Name} door jambs", Material = (PavilionMat)MatWall(room) };
            var glow = new PavilionSurface { Name = $"{Rooms[room].Name} glow", Material = (PavilionMat)MatGlow(room), Collide = false };

            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                {
                    Vector3 along = WorldAxes[3 - i - j];
                    for (int si = -1; si <= 1; si += 2)
                        for (int sj = -1; sj <= 1; sj += 2)
                        {
                            Vector3 a = WorldAxes[i] * si, b = WorldAxes[j] * sj;
                            Vector3 core = o + (a + b) * s;
                            if (DoorEdge(room, a, b, doors))
                            {
                                Fillet(curves, core, a, b, along, -s, -gap, R, uvScale);
                                Fillet(curves, core, a, b, along, gap, s, R, uvScale);
                                Band(glow, core, a, b, along, -s, -gap, R);
                                Band(glow, core, a, b, along, gap, s, R);
                                Jamb(jambs, core, a, b, along, -gap, along, R);
                                Jamb(jambs, core, a, b, along, gap, -along, R);
                            }
                            else
                            {
                                Fillet(curves, core, a, b, along, -s, s, R, uvScale);
                                Band(glow, core, a, b, along, -s, s, R);
                            }
                        }
                }
            for (int corner = 0; corner < 8; corner++)
            {
                var sign = new Vector3((corner & 1) == 0 ? 1f : -1f, (corner & 2) == 0 ? 1f : -1f, (corner & 4) == 0 ? 1f : -1f);
                Cap(curves, o + sign * s, sign, R, uvScale);
            }
            layout.Surfaces.Add(curves);
            layout.Surfaces.Add(jambs);
            layout.Surfaces.Add(glow);
        }

        /// <summary>A quarter-pipe from face a round to face b, from t0 to t1 along the edge.</summary>
        static void Fillet(PavilionSurface m, Vector3 core, Vector3 a, Vector3 b, Vector3 along, float t0, float t1, float R, float uv)
        {
            int start = m.Vertices.Count;
            for (int k = 0; k <= ArcSteps; k++)
            {
                float th = k * (Mathf.PI * 0.5f / ArcSteps);
                Vector3 radial = a * Mathf.Cos(th) + b * Mathf.Sin(th);
                foreach (float t in new[] { t0, t1 })
                {
                    m.Vertices.Add(core + radial * R + along * t);
                    m.Normals.Add(-radial);
                    m.UVs.Add(new Vector2(t * uv, th * R * uv));
                }
            }
            for (int k = 0; k < ArcSteps; k++)
            {
                int v = start + 2 * k;
                Quad(m, v, v + 1, v + 3, v + 2);
            }
        }

        /// <summary>The flat end of a cut quarter-pipe: the corner left between the curve and the
        /// two walls, facing <paramref name="facing"/> (into the door's gap).</summary>
        static void Jamb(PavilionSurface m, Vector3 core, Vector3 a, Vector3 b, Vector3 along, float t, Vector3 facing, float R)
            => JambFan(m, core, a, b, along, t, facing, R);

        /// <summary>A glowing strip along the middle of a quarter-pipe (just off its surface), so every
        /// edge of the room shows the room's colour.</summary>
        static void Band(PavilionSurface m, Vector3 core, Vector3 a, Vector3 b, Vector3 along, float t0, float t1, float R)
        {
            const float halfWidth = 0.14f, lift = 0.012f;
            float mid = Mathf.PI * 0.25f, spread = halfWidth / R;
            int start = m.Vertices.Count;
            foreach (float th in new[] { mid - spread, mid + spread })
            {
                Vector3 radial = a * Mathf.Cos(th) + b * Mathf.Sin(th);
                foreach (float t in new[] { t0, t1 })
                {
                    m.Vertices.Add(core + radial * (R - lift) + along * t);
                    m.Normals.Add(-radial);
                    m.UVs.Add(Vector2.zero);
                }
            }
            Quad(m, start, start + 1, start + 3, start + 2);
        }

        static void JambFan(PavilionSurface m, Vector3 core, Vector3 a, Vector3 b, Vector3 along, float t, Vector3 facing, float R)
        {
            int k0 = m.Vertices.Count;
            m.Vertices.Add(core + (a + b) * R + along * t);
            m.Normals.Add(facing);
            m.UVs.Add(Vector2.zero);
            for (int k = 0; k <= ArcSteps; k++)
            {
                float th = k * (Mathf.PI * 0.5f / ArcSteps);
                m.Vertices.Add(core + (a * Mathf.Cos(th) + b * Mathf.Sin(th)) * R + along * t);
                m.Normals.Add(facing);
                m.UVs.Add(new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * 0.1f);
            }
            for (int k = 0; k < ArcSteps; k++) Tri(m, k0, k0 + 1 + k, k0 + 2 + k);
        }

        /// <summary>A sphere cap (one eighth of a sphere) rounding a corner, meeting its three quarter-pipes.</summary>
        static void Cap(PavilionSurface m, Vector3 core, Vector3 sign, float R, float uv)
        {
            int start = m.Vertices.Count, n = ArcSteps;
            for (int p = 0; p <= n; p++)
                for (int q = 0; q <= n; q++)
                {
                    float phi = p * (Mathf.PI * 0.5f / n), th = q * (Mathf.PI * 0.5f / n);
                    var u = new Vector3(Mathf.Cos(phi) * Mathf.Cos(th) * sign.x, Mathf.Cos(phi) * Mathf.Sin(th) * sign.y, Mathf.Sin(phi) * sign.z);
                    m.Vertices.Add(core + u * R);
                    m.Normals.Add(-u);
                    m.UVs.Add(new Vector2(th, phi) * (R * uv));
                }
            for (int p = 0; p < n; p++)
                for (int q = 0; q < n; q++)
                {
                    int v = start + p * (n + 1) + q;
                    Quad(m, v, v + 1, v + n + 2, v + n + 1);
                }
        }

        static void Quad(PavilionSurface m, int a, int b, int c, int d)
        {
            Tri(m, a, b, c);
            Tri(m, a, c, d);
        }

        /// <summary>Adds a triangle wound so its front faces the way its vertex normals point (the side
        /// you walk on); skips slivers (the sphere caps' pole).</summary>
        static void Tri(PavilionSurface m, int a, int b, int c)
        {
            Vector3 cross = Vector3.Cross(m.Vertices[b] - m.Vertices[a], m.Vertices[c] - m.Vertices[a]);
            if (cross.sqrMagnitude < 1e-10f) return;
            Vector3 n = m.Normals[a] + m.Normals[b] + m.Normals[c];
            m.Triangles.Add(a);
            if (Vector3.Dot(cross, n) >= 0f) { m.Triangles.Add(b); m.Triangles.Add(c); }
            else { m.Triangles.Add(c); m.Triangles.Add(b); }
        }

        static void Wall(PavilionLayout layout, string name, Vector3 centre, Vector3 size, int room)
        {
            layout.Boxes.Add(new PavilionBox
            {
                Name = name, Center = centre, Size = size, Rotation = Quaternion.identity,
                Material = (PavilionMat)MatWall(room), Collide = true, Visible = true,
            });
        }

        /// <summary>Four spawns on each spawn face of the Shell, clear of every door.</summary>
        static void Spawns(PavilionLayout layout, List<Door> doors, TesseractConfig c)
        {
            int room = RoomIndex(SpawnRoom);
            float h = c.Half;
            float[] spots = { 0.5f, 0.9f }; // share of the way from the face's middle to where its curved edges start
            float flat = h - c.edgeRadius - 0.6f;
            foreach (var faceName in SpawnFaces)
            {
                int face = RoomIndex(faceName);
                Vector4 down = Toward(face);
                // The two axes across the face.
                var across = new List<Vector4>();
                for (int a = 0; a < 4; a++)
                    if (a != Rooms[room].Axis && a != Rooms[face].Axis) across.Add(Axis4(a, 1f));
                int placed = 0;
                foreach (float s in spots)
                    for (int q = 0; q < 8 && placed < 4; q++)
                    {
                        // Round the face: four diagonals, then four sides.
                        float u = q < 4 ? (q % 2 == 0 ? 1f : -1f) : (q == 4 ? 1f : q == 5 ? -1f : 0f);
                        float v = q < 4 ? (q < 2 ? 1f : -1f) : (q == 6 ? 1f : q == 7 ? -1f : 0f);
                        Vector4 offset = (across[0] * u + across[1] * v) * (s * flat);
                        Vector3 pos = Point(room, down * (h - 0.95f) + offset, c);
                        if (NearDoor(pos, doors, 3.5f)) continue;
                        Vector3 up = Dir(room, -down);
                        Vector3 facing = Dir(room, -offset).normalized; // toward the middle of the face
                        layout.Spawns.Add(new PavilionMarker { Position = pos, Rotation = PavilionLayout.Basis(facing, up) });
                        placed++;
                    }
            }
        }

        static bool NearDoor(Vector3 p, List<Door> doors, float clearance)
        {
            foreach (var d in doors)
                if (DistanceToDoor(p, d) < clearance) return true;
            return false;
        }

        /// <summary>Distance from a point to a door's rectangle.</summary>
        public static float DistanceToDoor(Vector3 p, Door d)
        {
            Vector3 right = Vector3.Cross(d.Up, d.Normal);
            Vector3 v = p - d.Position;
            float x = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(v, right)) - d.Size.x * 0.5f);
            float y = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(v, d.Up)) - d.Size.y * 0.5f);
            float z = Vector3.Dot(v, d.Normal);
            return Mathf.Sqrt(x * x + y * y + z * z);
        }

        // ------------------------------------------------------------------ portal maths (as Portal does it)

        /// <summary>A world direction carried from door <paramref name="from"/> through to <paramref name="to"/>
        /// (Portal.TransferRotation: into the front of one, out of the front of the other).</summary>
        public static Vector3 TransferDir(Door from, Door to, Vector3 v)
        {
            Vector3 fr = Vector3.Cross(from.Up, from.Normal), tr = Vector3.Cross(to.Up, to.Normal);
            float x = Vector3.Dot(v, fr), y = Vector3.Dot(v, from.Up), z = Vector3.Dot(v, from.Normal);
            return tr * -x + to.Up * y + to.Normal * -z; // half turn round up
        }

        public static Vector3 TransferPoint(Door from, Door to, Vector3 p) => to.Position + TransferDir(from, to, p - from.Position);

        /// <summary>Portal.GravityAfterPass: carry the fall direction through, snap onto an axis within 30 degrees.</summary>
        public static Vector3 GravityAfterPass(Door from, Door to, Vector3 gravity)
        {
            Vector3 g = TransferDir(from, to, gravity.normalized);
            Vector3 best = g.normalized;
            float bestAngle = 30f;
            foreach (var axis in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                float a = Vector3.Angle(g, axis);
                if (a <= bestAngle) { bestAngle = a; best = axis; }
            }
            return best;
        }

        // ------------------------------------------------------------------ checks

        /// <summary>Everything that can be wrong with the room table, the connections and the built
        /// layout. Empty = all good. <paramref name="layout"/> may be null (then only the data is checked).</summary>
        public static List<string> Validate(TesseractConfig c, PavilionLayout layout = null)
        {
            var problems = new List<string>();

            // Rooms: eight distinct cells, each with a proper (not mirrored) frame along its own three axes.
            if (Rooms.Length != 8) problems.Add($"There are {Rooms.Length} rooms; a tesseract has 8.");
            for (int i = 0; i < Rooms.Length; i++)
            {
                var r = Rooms[i];
                for (int j = 0; j < i; j++)
                {
                    if (Rooms[j].Name == r.Name) problems.Add($"Two rooms are called {r.Name}.");
                    if (Rooms[j].Axis == r.Axis && Rooms[j].Sign == r.Sign) problems.Add($"{Rooms[j].Name} and {r.Name} are the same cell.");
                }
                var f = ParseFrame(r.Frame);
                if (f == null) { problems.Add($"{r.Name}: can't read its frame \"{r.Frame}\" (want e.g. \"+w +y +z\")."); continue; }
                Vector4 n = Axis4(r.Axis, r.Sign);
                bool tangent = true;
                for (int k = 0; k < 3; k++) if (Mathf.Abs(f[k][r.Axis]) > 0f) tangent = false;
                if (!tangent || Det(f[0], f[1], f[2], n) == 0f)
                    problems.Add($"{r.Name}: its frame must use its three other axes, once each.");
                else if (Det(n, f[0], f[1], f[2]) < 0f)
                    problems.Add($"{r.Name}: its frame is mirrored, so its doors can't line up with its neighbours'.");
            }
            if (problems.Count > 0) return problems;

            // Connections: real rooms, never opposite, no repeats, six per room, a floor beside both.
            var count = new int[Rooms.Length];
            var seen = new HashSet<(int, int)>();
            foreach (var con in Connections)
            {
                int a = RoomIndex(con.A), b = RoomIndex(con.B), g = RoomIndex(con.Floor);
                string label = $"{con.A}-{con.B}";
                if (a < 0 || b < 0 || g < 0) { problems.Add($"{label}: unknown room name ({con.A}, {con.B}, floor {con.Floor})."); continue; }
                if (a == b) { problems.Add($"{label}: a room connected to itself."); continue; }
                if (Opposite(a, b)) { problems.Add($"{label}: opposite rooms must never connect."); continue; }
                if (!seen.Add((Mathf.Min(a, b), Mathf.Max(a, b)))) { problems.Add($"{label}: connected twice."); continue; }
                if (!Neighbours(a, g) || !Neighbours(b, g))
                    problems.Add($"{label}: floor {con.Floor} isn't beside both rooms' shared face (it must not be {con.A}, {con.B} or their opposites).");
                count[a]++;
                count[b]++;
            }
            for (int i = 0; i < Rooms.Length; i++)
                if (count[i] != 6) problems.Add($"{Rooms[i].Name} has {count[i]} connections; every room needs exactly 6.");

            // Door placement inside each room.
            for (int r = 0; r < Rooms.Length; r++)
            {
                var mine = new List<(int target, int floor)>();
                foreach (var con in Connections)
                {
                    int a = RoomIndex(con.A), b = RoomIndex(con.B), g = RoomIndex(con.Floor);
                    if (a == r && b >= 0) mine.Add((b, g));
                    else if (b == r && a >= 0) mine.Add((a, g));
                }
                for (int i = 0; i < mine.Count; i++)
                    for (int j = i + 1; j < mine.Count; j++)
                    {
                        var p = mine[i];
                        var q = mine[j];
                        if (p.target >= 0 && q.target >= 0 && Opposite(p.target, q.target) && p.floor == q.floor)
                            problems.Add($"{Rooms[r].Name}: the doors to {Rooms[p.target].Name} and {Rooms[q.target].Name} are on opposite faces against the same edge, so a drop through one lands on the other.");
                        if (p.target == q.floor && q.target == p.floor)
                            problems.Add($"{Rooms[r].Name}: the doors to {Rooms[p.target].Name} and {Rooms[q.target].Name} share one room edge.");
                    }
            }

            if (c.doorWidth <= 0.5f || c.doorHeight <= 2f) problems.Add("Doors are too small to walk through.");
            if (c.doorHeight > c.Half - c.doorWidth * 0.5f - 0.5f || c.doorWidth > c.roomSize - 2f)
                problems.Add($"Doors ({c.doorWidth} x {c.doorHeight} m) are too big for {c.roomSize} m rooms: doors on one face would run into each other's landing spots.");
            if (c.roomSpacing < c.roomSize + 2f * c.wallThickness + 60f)
                problems.Add($"Rooms are only {c.roomSpacing} m apart: keep them at least 60 m clear of each other so nothing is seen or heard straight across.");
            if (problems.Count > 0) return problems;

            if (c.edgeRadius < 1.5f || c.edgeRadius > c.Half - c.doorWidth * 0.5f - 1f)
                problems.Add($"Curved edges of {c.edgeRadius} m don't fit: keep them between 1.5 m (a standing player must fit on the curve) and {c.Half - c.doorWidth * 0.5f - 1f} m (clear of the doors).");
            if (problems.Count > 0) return problems;

            CheckPasses(c, problems);
            CheckWalking(c, problems, out _, out _);
            if (layout != null) CheckGeometry(c, layout, problems);
            return problems;
        }

        /// <summary>Every door pair matches the 4D gluing, and every way through it behaves.</summary>
        static void CheckPasses(TesseractConfig c, List<string> problems)
        {
            var doors = Doors(c);
            foreach (var d in doors)
            {
                var e = doors[d.Link];
                string label = $"{Rooms[d.Room].Name} to {Rooms[d.Target].Name}";

                // The pair joins the rooms exactly as the cells are glued: the shared face's two other
                // axes are kept, and heading into the face becomes heading out of the other room's face.
                foreach (var v in new[] { Vector3.right, Vector3.up, Vector3.forward })
                {
                    Vector4 v4 = Dir4(d.Room, v);
                    int b = Rooms[d.Target].Axis, a = Rooms[d.Room].Axis;
                    Vector4 glued = v4;
                    glued[b] = 0f;
                    glued[a] = -Rooms[d.Target].Sign * Rooms[d.Room].Sign * v4[b];
                    Vector3 expected = Dir(d.Target, glued);
                    if ((TransferDir(d, e, v) - expected).sqrMagnitude > 1e-6f)
                    { problems.Add($"{label}: the door turns you differently from the 4D gluing."); break; }
                }
                if ((TransferPoint(d, e, d.Position) - e.Position).sqrMagnitude > 1e-6f)
                    problems.Add($"{label}: the two doors don't line up.");

                // Walking through on its floor: you come out standing on the same face of the other room.
                Vector3 walkGravity = FaceDir(d.Room, d.Floor);
                Vector3 after = GravityAfterPass(d, e, walkGravity);
                if (Vector3.Angle(after, FaceDir(d.Target, d.Floor)) > 0.5f)
                    problems.Add($"{label}: walking through on the {Rooms[d.Floor].Name} face doesn't bring you out on it.");
                if (Mathf.Abs(Vector3.Dot(walkGravity, d.Normal)) > 1e-4f)
                    problems.Add($"{label}: the door isn't upright for someone standing on its floor.");

                // Dropping into it from its own face: you fall across the other room; you mustn't land on a door.
                Vector3 drop = GravityAfterPass(d, e, -d.Normal);
                if (Vector3.Angle(drop, e.Normal) > 0.5f)
                    problems.Add($"{label}: a drop through it doesn't carry you straight out of the other door.");
                Vector3 landing = e.Position + e.Normal * c.roomSize;
                foreach (var other in doors)
                {
                    if (other.Room != e.Room || other.Portal == e.Portal) continue;
                    Vector3 right = Vector3.Cross(other.Up, other.Normal);
                    Vector3 v = landing - other.Position;
                    if (Mathf.Abs(Vector3.Dot(v, other.Normal)) < 0.05f
                        && Mathf.Abs(Vector3.Dot(v, right)) < other.Size.x * 0.5f + 0.8f
                        && Mathf.Abs(Vector3.Dot(v, other.Up)) < other.Size.y * 0.5f + 0.8f)
                        problems.Add($"{label}: dropping through it lands you on (or right beside) the door {Rooms[other.Room].Name} to {Rooms[other.Target].Name}.");
                }
            }
        }

        /// <summary>A standing player's centre above the floor.</summary>
        const float BodyHeight = 0.9f;
        /// <summary>How far a sprinting player moves in one physics step (11 m/s, 50 steps a second).</summary>
        const float SprintStep = 0.22f;

        /// <summary>Walks and falls through the gravity field like a player: round every curved edge
        /// both ways, up to and through every door on its floor and out the other side, up to every
        /// door from its own face and the drop through it across the next room.</summary>
        static void CheckWalking(TesseractConfig c, List<string> problems, out float worstStep, out float worstCapture)
        {
            var field = Field(c);
            var doors = Doors(c);
            float h = c.Half, s = h - c.edgeRadius, R = c.edgeRadius;
            worstStep = 0f;
            worstCapture = 0f;

            // Round every curved edge, from face a onto face b (beside the door where an edge has one).
            for (int r = 0; r < Rooms.Length; r++)
            {
                Vector3 o = Centre(r, c);
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        if (i == j) continue;
                        for (int si = -1; si <= 1; si += 2)
                            for (int sj = -1; sj <= 1; sj += 2)
                            {
                                Vector3 a = WorldAxes[i] * si, b = WorldAxes[j] * sj, along = Vector3.Cross(a, b);
                                float t = DoorEdge(r, a, b, doors) ? (c.DoorGapHalf + s) * 0.5f : 0f;
                                var path = new List<(Vector3 p, Vector3 down)>();
                                for (float x = 0f; x < s; x += SprintStep) path.Add((o + a * (h - BodyHeight) + b * x + along * t, a));
                                int steps = Mathf.CeilToInt((R - BodyHeight) * Mathf.PI * 0.5f / SprintStep);
                                for (int k = 0; k <= steps; k++)
                                {
                                    float th = k * Mathf.PI * 0.5f / steps;
                                    Vector3 radial = a * Mathf.Cos(th) + b * Mathf.Sin(th);
                                    path.Add((o + (a + b) * s + radial * (R - BodyHeight) + along * t, radial));
                                }
                                for (float y = s; y > 0f; y -= SprintStep) path.Add((o + b * (h - BodyHeight) + a * y + along * t, b));

                                Vector3 g = a;
                                foreach (var (p, down) in path)
                                {
                                    Vector3 next = field.Resolve(g, p);
                                    worstStep = Mathf.Max(worstStep, Vector3.Angle(g, next));
                                    g = next;
                                    if (Vector3.Angle(g, down) > 1f)
                                    {
                                        problems.Add($"{Rooms[r].Name}: walking from the {WorldName(a)} face round onto the {WorldName(b)} face, gravity doesn't follow the floor.");
                                        break;
                                    }
                                }
                            }
                    }
            }

            foreach (var d in doors)
            {
                var e = doors[d.Link];
                string label = $"{Rooms[d.Room].Name} to {Rooms[d.Target].Name}";
                Vector3 o = Centre(d.Room, c), face = -d.Normal, floor = FaceDir(d.Room, d.Floor);

                // Walk up to the door on its floor and through it: no tilt on the way, upright after.
                Vector3 g = floor;
                bool tilted = false;
                for (float x = 0f; x <= h; x += SprintStep)
                {
                    g = field.Resolve(g, o + floor * (h - BodyHeight) + face * x);
                    if (Vector3.Angle(g, floor) > 0.01f) tilted = true;
                }
                if (tilted) problems.Add($"{label}: walking up to it on its floor, gravity tilts (a curve where the door's corner should be sharp).");
                Vector3 crossing = o + floor * (h - BodyHeight) + face * h;
                Vector3 exit = TransferPoint(d, e, crossing);
                g = field.Resolve(GravityAfterPass(d, e, floor), exit);
                Vector3 exitFloor = FaceDir(e.Room, d.Floor);
                if (Vector3.Angle(g, exitFloor) > 0.5f) problems.Add($"{label}: walking through, you don't come out standing on the {Rooms[d.Floor].Name} face.");
                for (float x = 0f; x < h; x += SprintStep)
                {
                    g = field.Resolve(g, exit + e.Normal * x);
                    if (Vector3.Angle(g, exitFloor) > 0.01f) { problems.Add($"{label}: walking away from the exit, gravity tilts."); break; }
                }

                // Walk up to the door on its own face (it's a hole at the edge of that floor)...
                g = face;
                tilted = false;
                for (float y = 0f; y <= h - c.doorHeight * 0.5f; y += SprintStep)
                {
                    g = field.Resolve(g, o + face * (h - BodyHeight) + floor * y);
                    if (Vector3.Angle(g, face) > 0.01f) tilted = true;
                }
                if (tilted) problems.Add($"{label}: walking up to it on its own face, gravity tilts.");

                // ...and drop in: fall across the other room and land.
                g = field.Resolve(GravityAfterPass(d, e, face), e.Position);
                if (Vector3.Angle(g, e.Normal) > 0.5f) { problems.Add($"{label}: a drop through it doesn't carry you straight out of the other door."); continue; }
                Vector3 p = e.Position, v = g * 3f;
                bool landed = false;
                for (int n = 0; n < 1000 && !landed; n++)
                {
                    v += g * (22f * 0.02f);
                    v = Vector3.ClampMagnitude(v, 45f);
                    p += v * 0.02f;
                    Vector3 next = field.Resolve(g, p);
                    worstCapture = Mathf.Max(worstCapture, Vector3.Angle(g, next));
                    g = next;
                    // (Not the exit itself: you come out of that surface.)
                    landed = Vector3.Dot(p - e.Position, e.Normal) > 2f && Clearance(field, p) <= BodyHeight;
                }
                if (!landed) { problems.Add($"{label}: a drop through it never lands."); continue; }
                Vector3 surfaceDown = field.SurfaceDown(p, out bool sharp);
                if (sharp || surfaceDown == Vector3.zero) surfaceDown = RoomField.NearestAxis(p - Centre(e.Room, c));
                if (Vector3.Angle(g, surfaceDown) > 1f)
                    problems.Add($"{label}: a drop through it lands on a curve with the wrong gravity ({Vector3.Angle(g, surfaceDown):0} degrees off).");
            }
            if (worstStep > 10f) problems.Add($"Gravity turns {worstStep:0} degrees in one physics step at a sprint somewhere: too sharp a curve.");
            if (worstCapture > 35f) problems.Add($"A drop catches you onto a curve with a {worstCapture:0} degree jump.");
        }

        /// <summary>How far a point is from the room surface in front of it.</summary>
        static float Clearance(RoomField f, Vector3 p)
        {
            int room = f.RoomOf(p, 0f);
            if (room < 0) return 0f;
            Vector3 l = p - f.centres[room];
            float s = f.half - f.edgeRadius;
            f.SurfaceDown(p, out bool sharp);
            float box = f.half - Mathf.Max(Mathf.Abs(l.x), Mathf.Max(Mathf.Abs(l.y), Mathf.Abs(l.z)));
            if (sharp) return box;
            Vector3 d = new Vector3(Mathf.Max(0f, Mathf.Abs(l.x) - s), Mathf.Max(0f, Mathf.Abs(l.y) - s), Mathf.Max(0f, Mathf.Abs(l.z) - s));
            return d.sqrMagnitude > 0f ? f.edgeRadius - d.magnitude : box;
        }

        /// <summary>The built layout: no overlapping walls, every door on a wall with open room in
        /// front, spawns standing on a floor and clear of doors.</summary>
        static void CheckGeometry(TesseractConfig c, PavilionLayout layout, List<string> problems)
        {
            var boxes = layout.Boxes;
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    Vector3 d = boxes[i].Center - boxes[j].Center, s = (boxes[i].Size + boxes[j].Size) * 0.5f;
                    if (Mathf.Abs(d.x) < s.x - 1e-3f && Mathf.Abs(d.y) < s.y - 1e-3f && Mathf.Abs(d.z) < s.z - 1e-3f)
                        problems.Add($"Walls overlap: {boxes[i].Name} and {boxes[j].Name}.");
                }

            if (layout.Portals.Count != 2 * Connections.Length) problems.Add($"{layout.Portals.Count} portals built; expected {2 * Connections.Length}.");
            for (int i = 0; i < layout.Portals.Count; i++)
            {
                var p = layout.Portals[i];
                if (p.Link < 0 || p.Link >= layout.Portals.Count || layout.Portals[p.Link].Link != i)
                    problems.Add($"{p.Name}: its link doesn't lead back to it.");
                if (!InsideAny(boxes, p.Position - p.Normal * 0.1f)) problems.Add($"{p.Name}: no wall behind it.");
                Vector3 right = Vector3.Cross(p.Up, p.Normal);
                foreach (var corner in new[] { right, -right })
                    foreach (var vert in new[] { p.Up, -p.Up })
                    {
                        Vector3 q = p.Position + (corner * p.Size.x + vert * p.Size.y) * 0.45f;
                        if (!InsideAny(boxes, q - p.Normal * 0.1f)) problems.Add($"{p.Name}: part of it hangs off its wall.");
                        if (InsideAny(boxes, q + p.Normal * 0.5f)) problems.Add($"{p.Name}: something blocks the space in front of it.");
                    }
            }

            var doors = Doors(c);
            foreach (var s in layout.Spawns)
            {
                Vector3 up = s.Rotation * Vector3.up;
                if (InsideAny(boxes, s.Position)) problems.Add($"A spawn at {s.Position} is inside a wall.");
                if (!InsideAny(boxes, s.Position - up * 1.0f)) problems.Add($"A spawn at {s.Position} has no floor under it.");
                if (NearDoor(s.Position, doors, 3f)) problems.Add($"A spawn at {s.Position} is right by a door.");
            }
            if (layout.Spawns.Count < 8) problems.Add($"Only {layout.Spawns.Count} spawns fit clear of the doors.");

            // Teams: the same number of spawns on each of the Shell's two spawn faces, and no others.
            var perTeam = new int[SpawnFaces.Length];
            foreach (var s in layout.Spawns)
            {
                int team = SpawnTeam(s.Rotation * Vector3.up);
                if (team < 0) problems.Add($"A spawn at {s.Position} isn't on either team's face.");
                else perTeam[team]++;
            }
            for (int i = 1; i < perTeam.Length; i++)
                if (perTeam[i] != perTeam[0]) problems.Add($"Teams get different numbers of spawns ({string.Join(", ", perTeam)}).");

            // Door surrounds stay inside the straight gap of the edge they stand on (clear of the curves).
            if (c.doorWidth * 0.5f + SurroundGap + SurroundWidth >= c.DoorGapHalf)
                problems.Add("Door surrounds are wider than the straight stretch of edge left for the door.");

            // Crates: one per room, resting on a floor, clear of doors.
            if (layout.Props.Count != Rooms.Length) problems.Add($"Only {layout.Props.Count} of {Rooms.Length} rooms got a crate spot clear of the doors.");
            foreach (var p in layout.Props)
            {
                Vector3 up = p.Rotation * Vector3.up;
                if (InsideAny(boxes, p.Position) || !InsideAny(boxes, p.Position - up * 0.6f)) problems.Add($"The crate spot at {p.Position} isn't resting on a floor.");
            }

            // Spawns stand on flat floor: the field leaves their gravity alone.
            var field = Field(c);
            foreach (var p in layout.Props)
            {
                Vector3 down = -(p.Rotation * Vector3.up);
                if (Vector3.Angle(field.Resolve(down, p.Position), down) > 0.01f) problems.Add($"The crate spot at {p.Position} is on a curve.");
            }
            foreach (var s in layout.Spawns)
            {
                Vector3 down = -(s.Rotation * Vector3.up);
                if (Vector3.Angle(field.Resolve(down, s.Position), down) > 0.01f) problems.Add($"A spawn at {s.Position} is on a curve, not flat floor.");
            }

            // The curves as built match the field: standing anywhere on them, down is straight into them.
            foreach (var surface in layout.Surfaces)
            {
                if (!surface.Name.EndsWith(" curves")) continue;
                for (int i = 0; i < surface.Vertices.Count; i++)
                {
                    Vector3 n = surface.Normals[i];
                    Vector3 down = field.SurfaceDown(surface.Vertices[i] + n * BodyHeight, out bool sharp);
                    if (sharp) continue;
                    if (down == Vector3.zero || Vector3.Angle(down, -n) > 0.5f)
                    {
                        problems.Add($"{surface.Name}: the gravity field doesn't match the curve at {surface.Vertices[i]}.");
                        break;
                    }
                }
            }
        }

        static bool InsideAny(List<PavilionBox> boxes, Vector3 p)
        {
            foreach (var b in boxes)
            {
                Vector3 d = p - b.Center, h = b.Size * 0.5f;
                if (Mathf.Abs(d.x) <= h.x && Mathf.Abs(d.y) <= h.y && Mathf.Abs(d.z) <= h.z) return true;
            }
            return false;
        }

        /// <summary>4x4 determinant of four column vectors.</summary>
        static float Det(Vector4 a, Vector4 b, Vector4 c, Vector4 d)
        {
            float[,] m = new float[4, 4];
            var cols = new[] { a, b, c, d };
            for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++) m[i, j] = cols[j][i];
            float det = 1f;
            for (int col = 0; col < 4; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < 4; r++) if (Mathf.Abs(m[r, col]) > Mathf.Abs(m[pivot, col])) pivot = r;
                if (Mathf.Abs(m[pivot, col]) < 1e-6f) return 0f;
                if (pivot != col)
                {
                    for (int k = 0; k < 4; k++) { float tmp = m[col, k]; m[col, k] = m[pivot, k]; m[pivot, k] = tmp; }
                    det = -det;
                }
                det *= m[col, col];
                for (int r = col + 1; r < 4; r++)
                {
                    float f = m[r, col] / m[col, col];
                    for (int k = col; k < 4; k++) m[r, k] -= f * m[col, k];
                }
            }
            return det;
        }

        // ------------------------------------------------------------------ report

        static string WorldName(Vector3 v)
        {
            if (v.x > 0.5f) return "+X"; if (v.x < -0.5f) return "-X";
            if (v.y > 0.5f) return "+Y (ceiling)"; if (v.y < -0.5f) return "-Y (floor)";
            if (v.z > 0.5f) return "+Z"; return "-Z";
        }

        /// <summary>Every room's six doors and where they lead, then the check results.</summary>
        public static string Report(TesseractConfig c)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"The Tesseract: {Rooms.Length} rooms ({c.roomSize} m), {Connections.Length} connections, doors {c.doorWidth} x {c.doorHeight} m.");
            var doors = Doors(c);
            for (int r = 0; r < Rooms.Length; r++)
            {
                int n = 0;
                foreach (var d in doors) if (d.Room == r) n++;
                sb.AppendLine($"{Rooms[r].Name} ({n} doors, frame {Rooms[r].Frame}):");
                foreach (var d in doors)
                {
                    if (d.Room != r) continue;
                    sb.AppendLine($"  {WorldName(-d.Normal),-13} face -> {Rooms[d.Target].Name,-5}  stands on the {WorldName(FaceDir(r, d.Floor)),-13} face ({Rooms[d.Floor].Name} side)   portal {d.Portal} <-> {d.Link}");
                }
            }
            var problems = Validate(c, Generate(c));
            CheckWalking(c, new List<string>(), out float worstStep, out float worstCapture);
            sb.AppendLine($"Walking: gravity turns at most {worstStep:0.0} degrees per physics step at a sprint round the curves; " +
                          $"the biggest catch after a drop is {worstCapture:0.0} degrees (eased by the camera).");
            if (problems.Count == 0) sb.AppendLine("Check: all good (6 doors per room, no opposite pairs, every pass, walk, drop and landing checked).");
            else
            {
                sb.AppendLine($"Check: {problems.Count} problem(s):");
                foreach (var p in problems) sb.AppendLine("  - " + p);
            }
            return sb.ToString();
        }
    }
}
