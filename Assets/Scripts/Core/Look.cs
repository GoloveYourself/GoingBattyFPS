using UnityEngine;

namespace GoingBatty
{
    /// <summary>Materials, colors and procedural textures/meshes. Everything visual is made here in code.</summary>
    public static class Look
    {
        public static readonly Color WorldFace = Color.black;
        public static readonly Color WorldEdge = new Color(0.30f, 0.36f, 0.48f, 1f);
        public static readonly Color SelfSonar = new Color(0.35f, 0.95f, 1.0f, 1f);

        public static readonly Color[] TeamColors =
        {
            new Color(1.0f, 0.38f, 0.30f, 1f), // Team 0 - Red
            new Color(0.32f, 0.62f, 1.0f, 1f), // Team 1 - Blue
        };
        public static readonly string[] TeamNames = { "Red", "Blue" };

        // One distinct sonar color per player slot (deathmatch).
        public static readonly Color[] SlotColors =
        {
            new Color(0.35f, 0.95f, 1.00f), new Color(1.00f, 0.55f, 0.25f),
            new Color(0.70f, 1.00f, 0.35f), new Color(1.00f, 0.40f, 0.80f),
            new Color(1.00f, 0.92f, 0.35f), new Color(0.65f, 0.50f, 1.00f),
            new Color(0.40f, 1.00f, 0.75f), new Color(1.00f, 0.35f, 0.35f),
        };

        static Shader _unlit, _line, _add;
        static Texture2D _dot;
        static Mesh _ghostMesh;

        public static Shader UnlitShader => _unlit ??= Load("Shaders/BattyUnlit", "Unlit/Color");
        public static Shader LineShader => _line ??= Load("Shaders/BattyLine", "Unlit/Color");
        public static Shader AdditiveShader => _add ??= Load("Shaders/BattyAdditive", "Particles/Standard Unlit");

        static Shader Load(string res, string fallback)
        {
            var s = Resources.Load<Shader>(res);
            if (s == null) s = Shader.Find(fallback);
            if (s == null) Debug.LogError("[GoingBatty] Missing shader " + res);
            return s;
        }

        public static Material Solid(Color c)
        {
            var m = new Material(UnlitShader);
            m.SetColor("_Color", c);
            return m;
        }

        public static Material Line(Color c)
        {
            var m = new Material(LineShader);
            m.SetColor("_Color", c);
            return m;
        }

        public static Material Additive(Color c, bool dotTexture = false)
        {
            var m = new Material(AdditiveShader);
            m.SetColor("_Color", c);
            if (dotTexture) m.SetTexture("_MainTex", Dot);
            return m;
        }

        /// <summary>Soft round dot used for sonar echo particles.</summary>
        public static Texture2D Dot
        {
            get
            {
                if (_dot != null) return _dot;
                const int n = 32;
                _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1 - d);
                    a = a * a * (3 - 2 * a);
                    _dot.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                _dot.Apply();
                return _dot;
            }
        }

        /// <summary>Capsule mesh with white vertex colors, sized like a player.</summary>
        public static Mesh GhostMesh
        {
            get
            {
                if (_ghostMesh != null) return _ghostMesh;
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                var src = tmp.GetComponent<MeshFilter>().sharedMesh;
                _ghostMesh = Object.Instantiate(src);
                Object.Destroy(tmp);
                var cols = new Color[_ghostMesh.vertexCount];
                for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
                _ghostMesh.colors = cols;
                return _ghostMesh;
            }
        }

        /// <summary>Builds a line-list mesh of the 12 edges of a box.</summary>
        public static Mesh BoxEdges(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++)
                v[i] = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            int[] idx = { 0,1, 2,3, 4,5, 6,7, 0,2, 1,3, 4,6, 5,7, 0,4, 1,5, 2,6, 3,7 };
            var m = new Mesh();
            m.vertices = v;
            var c = new Color[8];
            for (int i = 0; i < 8; i++) c[i] = Color.white;
            m.colors = c;
            m.SetIndices(idx, MeshTopology.Lines, 0);
            return m;
        }

        public static Color ColorForSlot(int slot) => SlotColors[Mathf.Abs(slot) % SlotColors.Length];
    }
}
