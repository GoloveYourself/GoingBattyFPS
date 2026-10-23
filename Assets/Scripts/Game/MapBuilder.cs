using System.Collections.Generic;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Builds the arena from an ASCII grid. Walls, floor and ceiling are black boxes; only
    /// their outlines are drawn, so players can see room boundaries but nothing else.
    ///
    /// Legend:  # wall   . floor (ground)   : raised floor (+1.5 m)
    ///          e/E ramp rising to the east (first/second half), w/W west, n/N north, s/S south
    ///          R = Red base floor  B = Blue base floor  (ground level, used for spawns & captures)
    ///
    /// Only the left half is authored; the right half is a mirror, so Capture the Box is fair.
    /// To make a new map, add another layout and point Build() at it.
    /// </summary>
    public class MapBuilder
    {
        public const float Cell = 1.5f;
        public const float Ceiling = 4.2f;
        public const float Raised = 1.5f;

        // Rows listed top (north) to bottom (south). 20 columns each = left half.
        static readonly string[] BurrowLeftHalf =
        {
            "####################", // 25
            "####################", // 24
            "####################", // 23
            "####################", // 22
            "###..eE:::::::::::::", // 21  raised north walkway (+1.5 m), runs across the whole map
            "###..eE:::::::::::::", // 20
            "###..#####::#####NN#", // 19
            "###..#####NN#####nn#", // 18  ramps up to the walkway from the junction room and atrium
            "###..#####nn###.....", // 17  atrium (cols 15-19 + mirror)
            "#RRRRRR##....##...ss", // 16
            "#RRRRRR##....##...SS", // 15  ramp up to the centre platform
            "#RRRRRR##....##...::", // 14  raised centre platform (4x4 cells)
            "#RRRRRR...........::", // 13  middle lane: base -> junction room -> atrium
            "#RRRRRR...........::", // 12
            "#RRRRRR##....##...::", // 11
            "#RRRRRR##....##...NN", // 10  ramp up to the centre platform
            "#RRRRRR##....##...nn", //  9
            "###..#####..###.....", //  8
            "###..#####..###..###", //  7
            "###..#####..###..###", //  6
            "###.................", //  5  south lane, runs under the whole map
            "###.................", //  4
            "####################", //  3
            "####################", //  2
            "####################", //  1
            "####################", //  0
        };

        public readonly List<Vector3> DmSpawns = new List<Vector3>();
        public readonly List<Vector3>[] TeamSpawns = { new List<Vector3>(), new List<Vector3>() };
        public readonly Vector3[] BoxHome = new Vector3[2];
        public readonly Bounds[] BaseZone = new Bounds[2];
        public GameObject Root { get; private set; }

        char[,] _g;
        int _w, _h;

        public static MapBuilder Build(Transform parent)
        {
            var m = new MapBuilder();
            m.Parse(BurrowLeftHalf);
            m.CreateGeometry(parent);
            m.FindMarkers();
            return m;
        }

        void Parse(string[] leftRowsTopDown)
        {
            _h = leftRowsTopDown.Length;
            int half = leftRowsTopDown[0].Length;
            _w = half * 2;
            _g = new char[_w, _h];
            for (int y = 0; y < _h; y++)
            {
                string row = leftRowsTopDown[_h - 1 - y].PadRight(half, '#');
                for (int x = 0; x < half; x++)
                {
                    char c = row[x];
                    _g[x, y] = c;
                    _g[_w - 1 - x, y] = Mirror(c);
                }
            }
        }

        static char Mirror(char c)
        {
            switch (c)
            {
                case 'e': return 'w';
                case 'E': return 'W';
                case 'w': return 'e';
                case 'W': return 'E';
                case 'R': return 'B';
                case 'B': return 'R';
                default: return c;
            }
        }

        public Vector3 CellCenter(int x, int y, float height = 0)
            => new Vector3((x + 0.5f - _w * 0.5f) * Cell, height, (y + 0.5f - _h * 0.5f) * Cell);

        Vector3 Corner(int x, int y, float h) => new Vector3((x - _w * 0.5f) * Cell, h, (y - _h * 0.5f) * Cell);

        bool Solid(int x, int y) => x < 0 || y < 0 || x >= _w || y >= _h || _g[x, y] == '#';

        /// <summary>Floor heights at the cell's corners: SW, SE, NE, NW. Walls return the ceiling.</summary>
        float[] CornerHeights(int x, int y)
        {
            if (Solid(x, y)) return new[] { Ceiling, Ceiling, Ceiling, Ceiling };
            float lo = 0, mid = Raised * 0.5f, hi = Raised;
            switch (_g[x, y])
            {
                case ':': return new[] { hi, hi, hi, hi };
                case 'e': return new[] { lo, mid, mid, lo };
                case 'E': return new[] { mid, hi, hi, mid };
                case 'w': return new[] { mid, lo, lo, mid };
                case 'W': return new[] { hi, mid, mid, hi };
                case 'n': return new[] { lo, lo, mid, mid };
                case 'N': return new[] { mid, mid, hi, hi };
                case 's': return new[] { mid, mid, lo, lo };
                case 'S': return new[] { hi, hi, mid, mid };
                default: return new[] { lo, lo, lo, lo };
            }
        }

        void CreateGeometry(Transform parent)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var lineVerts = new List<Vector3>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool twoSided = false)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
                if (!twoSided) return;
                // Walls/steps face both ways: physics raycasts ignore back faces of mesh colliders.
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }
            void Line(Vector3 a, Vector3 b) { lineVerts.Add(a); lineVerts.Add(b); }

            // Floors and ceilings of open cells
            for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
            {
                if (Solid(x, y)) continue;
                var h = CornerHeights(x, y);
                Quad(Corner(x, y, h[0]), Corner(x, y + 1, h[3]), Corner(x + 1, y + 1, h[2]), Corner(x + 1, y, h[1]));
                Quad(Corner(x, y, Ceiling), Corner(x + 1, y, Ceiling), Corner(x + 1, y + 1, Ceiling), Corner(x, y + 1, Ceiling));
            }

            // Vertical faces + outline edges wherever neighbouring heights differ.
            // Vertical borders (between x and x+1)
            for (int x = -1; x < _w; x++)
            for (int y = 0; y < _h; y++)
            {
                if (Solid(x, y) && Solid(x + 1, y)) continue;
                var a = CornerHeights(x, y);      // use its SE(1) and NE(2)
                var b = CornerHeights(x + 1, y);  // use its SW(0) and NW(3)
                float aS = a[1], aN = a[2], bS = b[0], bN = b[3];
                if (Mathf.Approximately(aS, bS) && Mathf.Approximately(aN, bN)) continue;
                Quad(Corner(x + 1, y, Mathf.Min(aS, bS)), Corner(x + 1, y, Mathf.Max(aS, bS)),
                     Corner(x + 1, y + 1, Mathf.Max(aN, bN)), Corner(x + 1, y + 1, Mathf.Min(aN, bN)), true);
                Line(Corner(x + 1, y, aS), Corner(x + 1, y + 1, aN));
                Line(Corner(x + 1, y, bS), Corner(x + 1, y + 1, bN));
            }
            // Horizontal borders (between y and y+1)
            for (int x = 0; x < _w; x++)
            for (int y = -1; y < _h; y++)
            {
                if (Solid(x, y) && Solid(x, y + 1)) continue;
                var a = CornerHeights(x, y);      // its NW(3) and NE(2)
                var b = CornerHeights(x, y + 1);  // its SW(0) and SE(1)
                float aW = a[3], aE = a[2], bW = b[0], bE = b[1];
                if (Mathf.Approximately(aW, bW) && Mathf.Approximately(aE, bE)) continue;
                Quad(Corner(x, y + 1, Mathf.Min(aW, bW)), Corner(x + 1, y + 1, Mathf.Min(aE, bE)),
                     Corner(x + 1, y + 1, Mathf.Max(aE, bE)), Corner(x, y + 1, Mathf.Max(aW, bW)), true);
                Line(Corner(x, y + 1, aW), Corner(x + 1, y + 1, aE));
                Line(Corner(x, y + 1, bW), Corner(x + 1, y + 1, bE));
            }
            // Vertical corner edges at grid points where the boundary turns
            for (int x = 0; x <= _w; x++)
            for (int y = 0; y <= _h; y++)
            {
                float sw = CornerHeights(x - 1, y - 1)[2], se = CornerHeights(x, y - 1)[3];
                float ne = CornerHeights(x, y)[0], nw = CornerHeights(x - 1, y)[1];
                bool S = !Mathf.Approximately(sw, se), N = !Mathf.Approximately(nw, ne);
                bool W = !Mathf.Approximately(sw, nw), E = !Mathf.Approximately(se, ne);
                int count = (S ? 1 : 0) + (N ? 1 : 0) + (W ? 1 : 0) + (E ? 1 : 0);
                bool straight = count == 2 && ((S && N) || (W && E));
                if (count == 0 || straight) continue;
                float lo = Mathf.Min(Mathf.Min(sw, se), Mathf.Min(ne, nw));
                float hi = Mathf.Max(Mathf.Max(sw, se), Mathf.Max(ne, nw));
                if (hi - lo > 0.01f) Line(Corner(x, y, lo), Corner(x, y, hi));
            }

            Root = new GameObject("Map");
            Root.transform.SetParent(parent, false);

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var world = new GameObject("World");
            world.transform.SetParent(Root.transform, false);
            world.AddComponent<MeshFilter>().sharedMesh = mesh;
            world.AddComponent<MeshRenderer>().sharedMaterial = Look.Solid(Look.WorldFace);
            var col = world.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;

            var lines = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            lines.SetVertices(lineVerts);
            var lc = new Color[lineVerts.Count];
            for (int i = 0; i < lc.Length; i++) lc[i] = Color.white;
            lines.colors = lc;
            var idx = new int[lineVerts.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            lines.SetIndices(idx, MeshTopology.Lines, 0);
            lines.RecalculateBounds();
            var edges = new GameObject("Edges");
            edges.transform.SetParent(Root.transform, false);
            edges.AddComponent<MeshFilter>().sharedMesh = lines;
            edges.AddComponent<MeshRenderer>().sharedMaterial = Look.Line(Look.WorldEdge);
        }

        void FindMarkers()
        {
            var redCells = new List<Vector2Int>();
            var blueCells = new List<Vector2Int>();
            for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
            {
                char c = _g[x, y];
                if (c == 'R') redCells.Add(new Vector2Int(x, y));
                else if (c == 'B') blueCells.Add(new Vector2Int(x, y));
            }

            BaseZone[0] = ZoneFrom(redCells);
            BaseZone[1] = ZoneFrom(blueCells);
            BoxHome[0] = new Vector3(BaseZone[0].center.x, 0.6f, BaseZone[0].center.z);
            BoxHome[1] = new Vector3(BaseZone[1].center.x, 0.6f, BaseZone[1].center.z);

            // Team spawns: base cells away from the box itself
            foreach (var c in redCells) if ((c.x + c.y) % 2 == 0) TeamSpawns[0].Add(CellCenter(c.x, c.y, 0.05f));
            foreach (var c in blueCells) if ((c.x + c.y) % 2 == 0) TeamSpawns[1].Add(CellCenter(c.x, c.y, 0.05f));

            // Deathmatch spawns: spread across ground and raised floors
            int[,] picks =
            {
                { 2, 10 }, { 5, 15 }, { 3, 19 }, { 9, 21 }, { 15, 20 }, { 11, 15 }, { 11, 9 },
                { 6, 4 }, { 13, 5 }, { 16, 13 }, { 16, 8 }, { 18, 12 },
            };
            for (int i = 0; i < picks.GetLength(0); i++)
            {
                int x = picks[i, 0], y = picks[i, 1];
                AddSpawn(x, y);
                AddSpawn(_w - 1 - x, y);
            }
        }

        void AddSpawn(int x, int y)
        {
            if (Solid(x, y)) return;
            float h = CornerHeights(x, y)[0];
            DmSpawns.Add(CellCenter(x, y, h + 0.05f));
        }

        Bounds ZoneFrom(List<Vector2Int> cells)
        {
            if (cells.Count == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = new Bounds(CellCenter(cells[0].x, cells[0].y, 1f), Vector3.zero);
            foreach (var c in cells)
            {
                b.Encapsulate(CellCenter(c.x, c.y, 0f) - new Vector3(Cell, 0, Cell) * 0.5f);
                b.Encapsulate(CellCenter(c.x, c.y, Ceiling) + new Vector3(Cell, 0, Cell) * 0.5f);
            }
            return b;
        }
    }
}
