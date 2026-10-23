using System.Collections.Generic;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>Grid navigation for bots: floor height lookup and A* paths (ramps included).</summary>
    public partial class MapBuilder
    {
        List<Vector2Int> _openCells;

        public bool IsOpen(int x, int y) => !Solid(x, y);

        public Vector2Int CellOf(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / Cell + _w * 0.5f);
            int y = Mathf.FloorToInt(p.z / Cell + _h * 0.5f);
            return new Vector2Int(Mathf.Clamp(x, 0, _w - 1), Mathf.Clamp(y, 0, _h - 1));
        }

        /// <summary>Floor height under a world position (bilinear over the cell's corners, so ramps are smooth).</summary>
        public float FloorHeight(Vector3 p)
        {
            var c = CellOf(p);
            if (Solid(c.x, c.y)) return 0f;
            var h = CornerHeights(c.x, c.y);
            float u = Mathf.Clamp01(p.x / Cell + _w * 0.5f - c.x);
            float v = Mathf.Clamp01(p.z / Cell + _h * 0.5f - c.y);
            float south = Mathf.Lerp(h[0], h[1], u);
            float north = Mathf.Lerp(h[3], h[2], u);
            return Mathf.Lerp(south, north, v);
        }

        public Vector3 CellFloorCenter(Vector2Int c)
        {
            var p = CellCenter(c.x, c.y);
            p.y = FloorHeight(p);
            return p;
        }

        public Vector3 RandomOpenPoint()
        {
            if (_openCells == null)
            {
                _openCells = new List<Vector2Int>();
                for (int x = 0; x < _w; x++)
                for (int y = 0; y < _h; y++)
                    if (!Solid(x, y)) _openCells.Add(new Vector2Int(x, y));
            }
            return CellFloorCenter(_openCells[Random.Range(0, _openCells.Count)]);
        }

        /// <summary>Can you walk directly from cell a to the 4-neighbour b (shared edge at the same height)?</summary>
        bool Connected(Vector2Int a, Vector2Int b)
        {
            if (Solid(b.x, b.y)) return false;
            var ha = CornerHeights(a.x, a.y);
            var hb = CornerHeights(b.x, b.y);
            int dx = b.x - a.x, dy = b.y - a.y;
            // Corner order: 0 SW, 1 SE, 2 NE, 3 NW
            if (dx == 1) return Same(ha[1], hb[0]) && Same(ha[2], hb[3]);
            if (dx == -1) return Same(ha[0], hb[1]) && Same(ha[3], hb[2]);
            if (dy == 1) return Same(ha[3], hb[0]) && Same(ha[2], hb[1]);
            return Same(ha[0], hb[3]) && Same(ha[1], hb[2]);
        }

        static bool Same(float a, float b) => Mathf.Abs(a - b) < 0.05f;

        static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        /// <summary>A* over floor cells. Returns world points (cell centres at floor height), excluding the start.</summary>
        public List<Vector3> FindPath(Vector3 from, Vector3 to)
        {
            var start = CellOf(from);
            var goal = CellOf(to);
            var result = new List<Vector3>();
            if (Solid(goal.x, goal.y) || Solid(start.x, start.y)) return result;

            var open = new List<Vector2Int> { start };
            var came = new Dictionary<Vector2Int, Vector2Int>();
            var g = new Dictionary<Vector2Int, int> { [start] = 0 };
            var closed = new HashSet<Vector2Int>();
            int H(Vector2Int c) => Mathf.Abs(c.x - goal.x) + Mathf.Abs(c.y - goal.y);

            int guard = 0;
            while (open.Count > 0 && guard++ < 4000)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (g[open[i]] + H(open[i]) < g[open[best]] + H(open[best])) best = i;
                var cur = open[best];
                open.RemoveAt(best);
                if (cur == goal)
                {
                    var c = goal;
                    while (c != start) { result.Add(CellFloorCenter(c)); c = came[c]; }
                    result.Reverse();
                    return result;
                }
                closed.Add(cur);
                foreach (var d in Dirs)
                {
                    var n = cur + d;
                    if (closed.Contains(n) || !Connected(cur, n)) continue;
                    int ng = g[cur] + 1;
                    if (!g.TryGetValue(n, out int old) || ng < old)
                    {
                        g[n] = ng;
                        came[n] = cur;
                        if (!open.Contains(n)) open.Add(n);
                    }
                }
            }
            return result;
        }
    }
}
