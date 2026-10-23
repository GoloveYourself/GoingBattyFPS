using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Plays another player's voice from their position in 3D (proximity chat).
    /// Volume falls off with distance; beyond ~32 m they can't be heard.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VoiceOut : MonoBehaviour
    {
        const float MaxHearDistance = 32f;

        AudioSource _src;
        readonly object _lock = new object();
        float[] _ring = new float[48000];
        int _w, _r, _count, _rate;
        bool _primed;
        float _loud;

        /// <summary>0..1 how loud this player is currently speaking (for UI).</summary>
        public float Loudness => _loud;

        void Awake()
        {
            _src = GetComponent<AudioSource>();
            _src.spatialBlend = 1f;
            _src.rolloffMode = AudioRolloffMode.Linear;
            _src.minDistance = 2.5f;
            _src.maxDistance = MaxHearDistance;
            _src.dopplerLevel = 0f;
            _src.loop = true;
            _src.playOnAwake = false;
        }

        void EnsureClip(int rate)
        {
            if (rate == _rate && _src.clip != null) return;
            _rate = rate;
            lock (_lock) { _w = _r = _count = 0; _primed = false; }
            _src.Stop();
            if (_src.clip != null) Destroy(_src.clip);
            _src.clip = AudioClip.Create("voice", rate, 1, rate, true, OnRead);
            _src.Play();
        }

        public void Push(byte[] mulaw, int rate)
        {
            EnsureClip(rate);
            float gain = Settings.VoiceVolume;
            float peak = 0;
            lock (_lock)
            {
                for (int i = 0; i < mulaw.Length; i++)
                {
                    float s = Mathf.Clamp(MuLaw.Decode(mulaw[i]) * gain, -1f, 1f);
                    peak = Mathf.Max(peak, Mathf.Abs(s));
                    _ring[_w] = s;
                    _w = (_w + 1) % _ring.Length;
                    if (_count < _ring.Length) _count++;
                    else _r = (_r + 1) % _ring.Length;
                }
                // Keep latency low: if more than 300 ms is queued, skip ahead.
                int maxQueued = rate * 3 / 10;
                if (_count > maxQueued)
                {
                    int drop = _count - rate / 10;
                    _r = (_r + drop) % _ring.Length;
                    _count -= drop;
                }
            }
            _loud = Mathf.Max(_loud, peak);
        }

        void Update() { _loud = Mathf.MoveTowards(_loud, 0, Time.deltaTime * 2f); }

        // Called by Unity's audio system to pull samples.
        void OnRead(float[] data)
        {
            lock (_lock)
            {
                if (!_primed && _count >= Mathf.Max(1, _rate * 6 / 100)) _primed = true; // 60 ms jitter buffer
                for (int i = 0; i < data.Length; i++)
                {
                    if (_primed && _count > 0)
                    {
                        data[i] = _ring[_r];
                        _r = (_r + 1) % _ring.Length;
                        _count--;
                    }
                    else
                    {
                        data[i] = 0f;
                        _primed = false;
                    }
                }
            }
        }
    }
}
