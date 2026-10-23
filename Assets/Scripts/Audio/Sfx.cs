using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// All sounds are synthesized at startup (no audio files to license or import).
    /// Includes an original, procedurally composed elevator-music loop for the Box.
    /// </summary>
    public static class Sfx
    {
        const int SR = 22050;

        public static AudioClip Gunshot, BlastJump, PingTalk, PingScream, KillConfirm, Death, Respawn, Capture, Pickup, Click, ElevatorLoop;

        public static void Build()
        {
            if (Gunshot != null) return;
            var rng = new System.Random(1234);
            Gunshot = Make("gunshot", 0.6f, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                float body = n * Mathf.Exp(-t * 9f) * 0.8f;
                float thump = Mathf.Sin(2 * Mathf.PI * (70f - 30f * t) * t) * Mathf.Exp(-t * 10f);
                return body * 0.7f + thump * 0.9f;
            }, lowpass: 0.35f);
            BlastJump = Make("blast", 0.5f, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                return n * Mathf.Exp(-t * 14f) * 0.5f + Mathf.Sin(2 * Mathf.PI * (55f - 20f * t) * t) * Mathf.Exp(-t * 7f);
            }, lowpass: 0.2f);
            PingTalk = Make("ping", 0.22f, t =>
            {
                float f = Mathf.Lerp(2300f, 1400f, t / 0.22f);
                return Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 18f) * 0.6f;
            });
            PingScream = Make("pingBig", 0.7f, t =>
            {
                float f = Mathf.Lerp(1600f, 500f, t / 0.7f);
                float s = Mathf.Sin(2 * Mathf.PI * f * t) + 0.4f * Mathf.Sin(2 * Mathf.PI * f * 1.5f * t);
                return s * Mathf.Exp(-t * 5f) * 0.5f;
            });
            KillConfirm = Make("kill", 0.35f, t =>
            {
                float f = t < 0.12f ? 880f : 1320f;
                return Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-(t % 0.12f) * 12f) * 0.4f;
            });
            Death = Make("death", 0.8f, t =>
                Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(300f, 80f, t / 0.8f) * t) * Mathf.Exp(-t * 3f) * 0.5f);
            Respawn = Make("respawn", 0.4f, t =>
                Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(400f, 900f, t / 0.4f) * t) * Mathf.Exp(-t * 6f) * 0.35f);
            Capture = Make("capture", 1.0f, t =>
            {
                float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
                int i = Mathf.Min(3, (int)(t / 0.12f));
                float lt = t - i * 0.12f;
                return Mathf.Sin(2 * Mathf.PI * notes[i] * t) * Mathf.Exp(-lt * 4f) * 0.4f;
            });
            Pickup = Make("pickup", 0.25f, t => Mathf.Sin(2 * Mathf.PI * 660f * t) * Mathf.Exp(-t * 10f) * 0.4f);
            Click = Make("click", 0.05f, t => Mathf.Sin(2 * Mathf.PI * 1200f * t) * Mathf.Exp(-t * 80f) * 0.3f);
            ElevatorLoop = BuildElevatorMusic();
        }

        delegate float Gen(float t);

        static AudioClip Make(string name, float seconds, Gen g, float lowpass = 1f)
        {
            int n = Mathf.CeilToInt(seconds * SR);
            var d = new float[n];
            float prev = 0;
            for (int i = 0; i < n; i++)
            {
                float v = g(i / (float)SR);
                prev = Mathf.Lerp(prev, v, lowpass);
                d[i] = Mathf.Clamp(prev, -1f, 1f);
            }
            // tiny fade-out to avoid clicks
            int fade = Mathf.Min(n, 200);
            for (int i = 0; i < fade; i++) d[n - 1 - i] *= i / (float)fade;
            var c = AudioClip.Create(name, n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>Original bossa-ish lounge loop: Fmaj7 - Dm7 - Gm7 - C7, soft keys, bass and flute.</summary>
        static AudioClip BuildElevatorMusic()
        {
            const float bpm = 104f;
            float beat = 60f / bpm;
            int beats = 32;
            int n = Mathf.CeilToInt(beats * beat * SR);
            var d = new float[n];

            // MIDI note numbers
            int[][] chords =
            {
                new[] { 53, 57, 60, 64 }, // Fmaj7
                new[] { 50, 53, 57, 60 }, // Dm7
                new[] { 55, 58, 62, 65 }, // Gm7
                new[] { 48, 52, 55, 58 }, // C7
            };
            int[] melody =
            {
                72, -1, 69, 67, 69, -1, 72, -1,   74, -1, 72, 69, 65, -1, -1, -1,
                70, -1, 69, 67, 65, -1, 67, -1,   64, -1, 67, 70, 72, -1, -1, -1,
            };

            for (int b = 0; b < beats; b++)
            {
                int ci = (b / 8) % 4;
                var ch = chords[ci];
                float t0 = b * beat;
                // Keys: comp on beats 1 and the "and" of 2 (bossa feel)
                bool comp = (b % 4 == 0) || (b % 4 == 2);
                if (comp)
                    foreach (var m in ch) AddNote(d, t0 + ((b % 4 == 2) ? beat * 0.5f : 0f), beat * 1.4f, Midi(m), 0.07f, Voice.Keys);
                // Bass: root on 1, fifth on 3
                if (b % 2 == 0)
                {
                    int bassNote = (b % 4 == 0) ? ch[0] - 12 : ch[0] - 5;
                    AddNote(d, t0, beat * 0.9f, Midi(bassNote), 0.22f, Voice.Bass);
                }
                // Melody
                int mn = melody[b % melody.Length];
                if (mn > 0) AddNote(d, t0, beat * 1.6f, Midi(mn), 0.10f, Voice.Flute);
                // Soft shaker
                for (int s = 0; s < 2; s++) AddNoise(d, t0 + s * beat * 0.5f, 0.05f, 0.025f, b * 7 + s);
            }

            float peak = 0.001f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            for (int i = 0; i < n; i++) d[i] = d[i] / peak * 0.8f;

            var c = AudioClip.Create("elevator", n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        enum Voice { Keys, Bass, Flute }

        static float Midi(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);

        static void AddNote(float[] d, float start, float dur, float f, float amp, Voice v)
        {
            int s0 = (int)(start * SR);
            int len = (int)(dur * SR);
            for (int i = 0; i < len; i++)
            {
                int idx = (s0 + i) % d.Length; // wrap so the loop is seamless
                float t = i / (float)SR;
                float env, s;
                switch (v)
                {
                    case Voice.Keys:
                        env = Mathf.Exp(-t * 2.5f) * Mathf.Min(1f, t * 200f);
                        s = Mathf.Sin(2 * Mathf.PI * f * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * f * 2 * t) + 0.1f * Mathf.Sin(2 * Mathf.PI * f * 3 * t);
                        break;
                    case Voice.Bass:
                        env = Mathf.Exp(-t * 3f) * Mathf.Min(1f, t * 300f);
                        s = Mathf.Sin(2 * Mathf.PI * f * t) + 0.2f * Mathf.Sin(2 * Mathf.PI * f * 2 * t);
                        break;
                    default:
                        float vib = 1f + 0.006f * Mathf.Sin(2 * Mathf.PI * 5.5f * t) * Mathf.Min(1f, t * 3f);
                        env = Mathf.Min(1f, t * 12f) * Mathf.Min(1f, (dur - t) * 8f);
                        s = Mathf.Sin(2 * Mathf.PI * f * vib * t) + 0.08f * Mathf.Sin(2 * Mathf.PI * f * 2 * vib * t);
                        break;
                }
                d[idx] += s * env * amp;
            }
        }

        static void AddNoise(float[] d, float start, float dur, float amp, int seed)
        {
            var r = new System.Random(seed);
            int s0 = (int)(start * SR), len = (int)(dur * SR);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                d[(s0 + i) % d.Length] += (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 60f) * amp;
            }
        }

        /// <summary>Plays a 3D sound at a world position.</summary>
        public static void At(AudioClip clip, Vector3 pos, float volume = 1f, float maxDistance = 45f)
        {
            if (clip == null) return;
            var go = new GameObject("sfx");
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.volume = volume;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 3f;
            a.maxDistance = maxDistance;
            a.dopplerLevel = 0;
            a.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        /// <summary>Plays a non-positional UI/self sound.</summary>
        public static void Ui(AudioClip clip, float volume = 1f)
        {
            if (clip == null || Bootstrap.UiAudio == null) return;
            Bootstrap.UiAudio.PlayOneShot(clip, volume);
        }
    }
}
