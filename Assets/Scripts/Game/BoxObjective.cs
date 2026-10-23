using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Capture the Box: each team's Box. It plays elevator music and is always visible to
    /// everyone — and so is whoever is carrying it. Also draws each team's base outline.
    /// </summary>
    public class BoxObjective : MonoBehaviour
    {
        int _team;
        MapBuilder _map;
        Transform _box;
        Transform _carrierGhost;
        Material _ghostMat;
        AudioSource _music;
        GameObject _baseOutline;
        bool _visible;

        public void Init(int team, MapBuilder map)
        {
            _team = team;
            _map = map;
            var color = Look.TeamColors[team];

            _box = new GameObject("Box").transform;
            _box.SetParent(transform, false);
            var edges = new GameObject("Edges");
            edges.transform.SetParent(_box, false);
            edges.AddComponent<MeshFilter>().sharedMesh = Look.BoxEdges(Vector3.one * 0.6f);
            edges.AddComponent<MeshRenderer>().sharedMaterial = Look.Line(color * 1.4f);
            var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(_box, false);
            glow.transform.localScale = Vector3.one * 0.55f;
            var gm = glow.GetComponent<MeshFilter>().sharedMesh;
            var copy = Instantiate(gm);
            var cols = new Color[copy.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            copy.colors = cols;
            glow.GetComponent<MeshFilter>().sharedMesh = copy;
            glow.GetComponent<MeshRenderer>().sharedMaterial = Look.Additive(new Color(color.r, color.g, color.b, 0.35f));

            var musicGo = new GameObject("ElevatorMusic");
            musicGo.transform.SetParent(transform, false);
            _music = musicGo.AddComponent<AudioSource>();
            _music.clip = Sfx.ElevatorLoop;
            _music.loop = true;
            _music.spatialBlend = 1f;
            _music.rolloffMode = AudioRolloffMode.Linear;
            _music.minDistance = 3f;
            _music.maxDistance = 38f;
            _music.dopplerLevel = 0;
            _music.volume = 0.55f;

            // Carrier silhouette (always visible while carrying)
            var cg = new GameObject("CarrierGhost");
            cg.transform.SetParent(transform, false);
            cg.transform.localScale = new Vector3(Tuning.PlayerRadius * 2f, Tuning.PlayerHeight * 0.5f, Tuning.PlayerRadius * 2f);
            cg.AddComponent<MeshFilter>().sharedMesh = Look.GhostMesh;
            _ghostMat = Look.Additive(new Color(color.r, color.g, color.b, 0.45f));
            cg.AddComponent<MeshRenderer>().sharedMaterial = _ghostMat;
            _carrierGhost = cg.transform;

            // Base outline on the floor
            _baseOutline = new GameObject("BaseOutline");
            _baseOutline.transform.SetParent(transform, false);
            var z = map.BaseZone[team];
            float y = 0.03f;
            var v = new[]
            {
                new Vector3(z.min.x, y, z.min.z), new Vector3(z.max.x, y, z.min.z),
                new Vector3(z.max.x, y, z.max.z), new Vector3(z.min.x, y, z.max.z),
            };
            var m = new Mesh();
            m.vertices = v;
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, MeshTopology.Lines, 0);
            _baseOutline.AddComponent<MeshFilter>().sharedMesh = m;
            _baseOutline.AddComponent<MeshRenderer>().sharedMaterial = Look.Line(color);

            SetVisible(false);
        }

        public void SetVisible(bool v)
        {
            if (_visible == v && _box != null && _box.gameObject.activeSelf == v) return;
            _visible = v;
            _box.gameObject.SetActive(v);
            _music.gameObject.SetActive(v);
            _baseOutline.SetActive(v);
            _carrierGhost.gameObject.SetActive(false);
            if (v && !_music.isPlaying) _music.Play();
            if (!v) _music.Stop();
        }

        void Update()
        {
            if (!_visible || GameSession.I == null) return;
            var gs = GameSession.I;
            var b = gs.Boxes[_team];
            Vector3 pos = b.Pos;
            bool carried = b.Status == BoxStatus.Carried;
            Transform carrier = carried ? gs.CarrierTransform(b.CarrierSlot) : null;
            bool localCarrier = carried && b.CarrierSlot == gs.LocalSlot;

            if (carrier != null)
            {
                pos = carrier.position + Vector3.up * (Tuning.PlayerHeight + 0.45f);
                _carrierGhost.gameObject.SetActive(!localCarrier);
                _carrierGhost.SetPositionAndRotation(carrier.position + Vector3.up * (Tuning.PlayerHeight * 0.5f), carrier.rotation);
                float pulse = 0.35f + 0.15f * Mathf.Sin(Time.time * 6f);
                var c = Look.TeamColors[_team]; c.a = pulse;
                _ghostMat.SetColor("_Color", c);
            }
            else
            {
                _carrierGhost.gameObject.SetActive(false);
                pos += Vector3.up * (0.1f * Mathf.Sin(Time.time * 2f));
            }

            _box.position = Vector3.Lerp(_box.position, pos, (_box.position - pos).sqrMagnitude > 9 ? 1f : 1f - Mathf.Exp(-20f * Time.deltaTime));
            _box.rotation = Quaternion.Euler(0, Time.time * 45f, 0);
            // If you're carrying it, keep it from blocking your view
            _box.gameObject.SetActive(!localCarrier);
            _music.transform.position = localCarrier && gs.Local != null ? gs.Local.transform.position + Vector3.up : _box.position;
        }
    }
}
