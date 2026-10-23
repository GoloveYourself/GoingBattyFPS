using System.Collections.Generic;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Draws sonar. A pulse fires a cone of rays from the speaker's head; as the wavefront
    /// travels (Tuning.SonarSpeed) it lights glowing dots where it touches walls and briefly
    /// reveals a "ghost" of any player it passes. Every client draws every pulse in the world,
    /// so you see other players' blasts (and where they came from) if you're looking that way.
    /// </summary>
    public class SonarSystem : MonoBehaviour
    {
        public static SonarSystem I { get; private set; }

        struct Pending
        {
            public float Time;
            public Vector3 Pos;
            public Color Color;
            public float Size;
            public RemotePlayer Reveal; // non-null = ghost instead of dot
        }

        readonly List<Pending> _pending = new List<Pending>(4096);
        ParticleSystem _ps;
        readonly Queue<Ghost> _ghostPool = new Queue<Ghost>();
        readonly List<Ghost> _ghosts = new List<Ghost>();
        readonly List<(LineRenderer lr, float born, float life)> _tracers = new List<(LineRenderer, float, float)>();
        readonly Queue<LineRenderer> _tracerPool = new Queue<LineRenderer>();
        Material _tracerMat;

        class Ghost
        {
            public GameObject Go;
            public Material Mat;
            public Color Color;
            public float Born, Life;
        }

        void Awake()
        {
            I = this;
            var go = new GameObject("Echoes");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40000;
            main.startSpeed = 0f;
            main.startLifetime = Tuning.EchoLifetime;
            main.gravityModifier = 0f;
            var em = _ps.emission; em.enabled = false;
            var shape = _ps.shape; shape.enabled = false;
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.55f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Look.Additive(Color.white, dotTexture: true);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.minParticleSize = 0.0005f;
            r.maxParticleSize = 0.25f;
            _ps.Play();

            _tracerMat = Look.Additive(Color.white);
        }

        // ------------------------------------------------------------------ pulses

        /// <summary>Emit a sonar pulse. strength 0..1; >= Tuning.ScreamStrength is a scream blast.</summary>
        public void Pulse(Vector3 origin, Vector3 dir, float strength, Color color, byte ownerSlot, bool isLocal = false)
        {
            strength = Mathf.Clamp01(strength);
            bool scream = strength >= Tuning.ScreamStrength;
            float range, cone; int rays;
            if (scream)
            {
                float k = Mathf.InverseLerp(Tuning.ScreamStrength, 1f, strength);
                range = Mathf.Lerp(Tuning.ScreamRangeMin, Tuning.ScreamRangeMax, k);
                cone = Tuning.ScreamConeDeg;
                rays = Tuning.ScreamRays;
            }
            else
            {
                float k = strength / Tuning.ScreamStrength;
                range = Mathf.Lerp(Tuning.TalkRangeMin, Tuning.TalkRangeMax, k);
                cone = Tuning.TalkConeDeg;
                rays = Mathf.RoundToInt(Tuning.TalkRays * Mathf.Lerp(0.6f, 1f, k));
            }

            // Where it came from: a bright flash at the speaker's head
            Emit(origin + dir * 0.3f, Color.Lerp(color, Color.white, 0.4f), scream ? 1.4f : 0.6f, 0.9f);
            if (isLocal) Sfx.Ui(scream ? Sfx.PingScream : Sfx.PingTalk, scream ? 0.5f : 0.25f);
            else Sfx.At(scream ? Sfx.PingScream : Sfx.PingTalk, origin, scream ? 0.8f : 0.45f, scream ? 60f : 35f);

            dir = dir.sqrMagnitude > 0 ? dir.normalized : Vector3.forward;
            var basis = Quaternion.LookRotation(dir);
            float cosCone = Mathf.Cos(cone * Mathf.Deg2Rad);
            float now = Time.time;
            var revealed = new HashSet<RemotePlayer>();
            var localPlayer = GameSession.I != null ? GameSession.I.Local : null;

            for (int i = 0; i < rays; i++)
            {
                float cosT = Mathf.Lerp(1f, cosCone, Random.value);
                float sinT = Mathf.Sqrt(1f - cosT * cosT);
                float phi = Random.value * Mathf.PI * 2f;
                var d = basis * new Vector3(sinT * Mathf.Cos(phi), sinT * Mathf.Sin(phi), cosT);

                if (!Physics.Raycast(origin, d, out var hit, range, ~0, QueryTriggerInteraction.Ignore)) continue;
                float t = now + hit.distance / Tuning.SonarSpeed;

                var rp = hit.collider.GetComponentInParent<RemotePlayer>();
                if (rp != null)
                {
                    if (rp.Slot != ownerSlot && revealed.Add(rp))
                        _pending.Add(new Pending { Time = t, Reveal = rp, Color = color });
                    continue;
                }
                if (localPlayer != null && hit.collider.transform.IsChildOf(localPlayer.transform)) continue;

                float fall = 1f - hit.distance / range * 0.65f;
                var c = color * fall; c.a = 1f;
                float size = 0.10f + hit.distance * 0.0045f + (scream ? 0.04f : 0f);
                _pending.Add(new Pending { Time = t, Pos = hit.point + hit.normal * 0.04f, Color = c, Size = size });
            }
        }

        void Emit(Vector3 pos, Color c, float size, float life)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                startColor = c,
                startSize = size,
                startLifetime = life,
                velocity = Vector3.zero,
                applyShapeToPosition = false,
            };
            _ps.Emit(ep, 1);
        }

        // ------------------------------------------------------------------ ghosts

        public void ShowGhost(Vector3 feetPos, Quaternion rot, Color color, float life)
        {
            Ghost g = _ghostPool.Count > 0 ? _ghostPool.Dequeue() : NewGhost();
            g.Go.SetActive(true);
            g.Go.transform.SetPositionAndRotation(feetPos + Vector3.up * (Tuning.PlayerHeight * 0.5f), rot);
            g.Color = color;
            g.Born = Time.time;
            g.Life = life;
            g.Mat.SetColor("_Color", color);
            _ghosts.Add(g);
        }

        Ghost NewGhost()
        {
            var go = new GameObject("Ghost");
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(Tuning.PlayerRadius * 2f, Tuning.PlayerHeight * 0.5f, Tuning.PlayerRadius * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = Look.GhostMesh;
            var mat = Look.Additive(Color.white);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // A little "nose" so you can tell which way they face
            var nose = new GameObject("Facing");
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0, 0.55f, 0.55f);
            nose.transform.localScale = new Vector3(0.35f, 0.12f, 0.35f);
            nose.AddComponent<MeshFilter>().sharedMesh = Look.GhostMesh;
            nose.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return new Ghost { Go = go, Mat = mat };
        }

        Color RevealColor(RemotePlayer rp, Color pulseColor)
        {
            var gs = GameSession.I;
            if (gs != null && gs.IsTeamMode && gs.Players.TryGetValue(rp.Slot, out var p)) return Look.TeamColors[p.Team % 2] * 1.3f;
            return Color.Lerp(pulseColor, Color.white, 0.55f);
        }

        // ------------------------------------------------------------------ weapon effects

        public void ShotFx(Vector3 muzzle, List<Vector3> ends, Color color)
        {
            Emit(muzzle, Color.Lerp(color, Color.white, 0.6f), 0.9f, 0.25f);
            foreach (var e in ends)
            {
                Tracer(muzzle, e, Color.Lerp(color, Color.white, 0.3f), 0.22f);
                Emit(e, color, 0.25f, 0.8f);
            }
        }

        public void RemoteShot(Vector3 origin, Vector3 dir, int seed, Color color, byte slot)
        {
            var ends = new List<Vector3>();
            foreach (var d in LocalPlayer.PelletDirections(dir, seed))
            {
                Vector3 end = origin + d * Tuning.WeaponRange;
                if (Physics.Raycast(origin + d * 0.6f, d, out var hit, Tuning.WeaponRange, ~0, QueryTriggerInteraction.Ignore))
                    end = hit.point;
                ends.Add(end);
            }
            ShotFx(origin + dir * 0.5f, ends, color);
            // A gunshot is loud: the shooter is briefly revealed to anyone looking.
            var rp = GameSession.I?.Remote(slot);
            if (rp != null) ShowGhost(rp.transform.position, rp.transform.rotation, Color.Lerp(color, Color.white, 0.5f), 0.6f);
            Pulse(origin, dir, 0.18f, color, slot);
            Sfx.At(Sfx.Gunshot, origin, 1f, 70f);
        }

        public void BlastFx(Vector3 pos, Vector3 dir, Color color)
        {
            for (int i = 0; i < 24; i++)
            {
                var r = Quaternion.LookRotation(dir) * (Quaternion.Euler(0, 0, i * 15f) * Vector3.up) * 0.5f;
                Emit(pos + r, color, 0.25f, 0.5f);
            }
            Emit(pos, Color.white, 1.0f, 0.3f);
        }

        public void RemoteBlast(Vector3 pos, Vector3 dir, Color color, byte slot)
        {
            BlastFx(pos, dir, color);
            var rp = GameSession.I?.Remote(slot);
            if (rp != null) ShowGhost(rp.transform.position, rp.transform.rotation, Color.Lerp(color, Color.white, 0.4f), 0.5f);
            Sfx.At(Sfx.BlastJump, pos, 1f, 50f);
        }

        void Tracer(Vector3 a, Vector3 b, Color c, float life)
        {
            LineRenderer lr;
            if (_tracerPool.Count > 0) lr = _tracerPool.Dequeue();
            else
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _tracerMat;
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            lr.gameObject.SetActive(true);
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = 0.035f; lr.endWidth = 0.02f;
            lr.startColor = c; lr.endColor = c;
            _tracers.Add((lr, Time.time, life));
        }

        // ------------------------------------------------------------------ tick

        void Update()
        {
            float now = Time.time;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.Time > now) continue;
                if (p.Reveal != null)
                {
                    if (p.Reveal.Alive)
                        ShowGhost(p.Reveal.transform.position, p.Reveal.transform.rotation, RevealColor(p.Reveal, p.Color), Tuning.GhostLifetime);
                }
                else Emit(p.Pos, p.Color, p.Size, Tuning.EchoLifetime);
                _pending[i] = _pending[_pending.Count - 1];
                _pending.RemoveAt(_pending.Count - 1);
            }

            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                var g = _ghosts[i];
                float k = (now - g.Born) / g.Life;
                if (k >= 1f)
                {
                    g.Go.SetActive(false);
                    _ghostPool.Enqueue(g);
                    _ghosts.RemoveAt(i);
                    continue;
                }
                var c = g.Color;
                c.a = (1f - k) * (1f - k) * 0.9f;
                g.Mat.SetColor("_Color", c);
            }

            for (int i = _tracers.Count - 1; i >= 0; i--)
            {
                var (lr, born, life) = _tracers[i];
                float k = (now - born) / life;
                if (k >= 1f)
                {
                    lr.gameObject.SetActive(false);
                    _tracerPool.Enqueue(lr);
                    _tracers.RemoveAt(i);
                    continue;
                }
                var c = lr.startColor; c.a = 1f - k;
                lr.startColor = c; lr.endColor = c;
            }
        }

        /// <summary>Clears all effects (used when leaving a match).</summary>
        public void ClearAll()
        {
            _pending.Clear();
            _ps.Clear();
            foreach (var g in _ghosts) { g.Go.SetActive(false); _ghostPool.Enqueue(g); }
            _ghosts.Clear();
        }
    }
}
