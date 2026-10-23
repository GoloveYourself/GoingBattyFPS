using System;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Captures the chosen microphone. Produces:
    ///  - Level: 0..1 loudness after the player's sensitivity setting (drives sonar).
    ///  - Frame events: 20 ms mu-law voice frames for proximity chat (only while speaking).
    /// </summary>
    public class MicInput : MonoBehaviour
    {
        public static MicInput I { get; private set; }

        public float Level { get; private set; }          // smoothed, 0..1
        public float InstantLevel { get; private set; }   // unsmoothed, 0..1
        public string ActiveDevice { get; private set; }
        public int SampleRate { get; private set; } = 16000;
        public bool HasMic => _clip != null;
        public bool Transmitting { get; private set; }

        /// <summary>(mu-law bytes, sampleRate)</summary>
        public event Action<byte[], int> Frame;
        /// <summary>Set by the game while connected so we don't encode voice in menus.</summary>
        public bool TransmitEnabled;

        AudioClip _clip;
        string _deviceArg;
        int _readPos;
        float[] _read = new float[16000];
        float[] _frame;
        int _frameFill;
        float _gateUntil;

        void Awake()
        {
            I = this;
            Restart();
        }

        public static string[] Devices
        {
            get { try { return Microphone.devices; } catch { return Array.Empty<string>(); } }
        }

        public void Restart()
        {
            StopMic();
            var devices = Devices;
            if (devices.Length == 0) { ActiveDevice = null; return; }

            string wanted = Settings.MicDevice;
            if (!string.IsNullOrEmpty(wanted) && Array.IndexOf(devices, wanted) < 0) wanted = "";
            _deviceArg = string.IsNullOrEmpty(wanted) ? null : wanted;
            ActiveDevice = _deviceArg ?? devices[0] + " (default)";

            Microphone.GetDeviceCaps(_deviceArg, out int minF, out int maxF);
            int rate = 16000;
            if (minF != 0 || maxF != 0) rate = Mathf.Clamp(16000, Mathf.Max(minF, 8000), Mathf.Max(maxF, minF));

            _clip = Microphone.Start(_deviceArg, true, 1, rate);
            if (_clip == null) { ActiveDevice = null; return; }
            SampleRate = _clip.frequency;
            _frame = new float[SampleRate / 50]; // 20 ms
            _frameFill = 0;
            _readPos = 0;
        }

        void StopMic()
        {
            if (_clip != null)
            {
                try { Microphone.End(_deviceArg); } catch { }
                Destroy(_clip);
            }
            _clip = null;
            Level = InstantLevel = 0;
        }

        void OnDestroy() => StopMic();

        static float GainFromSensitivity(float s)
        {
            // 0 -> -20 dB, 0.5 -> 0 dB, 1 -> +26 dB
            float db = s < 0.5f ? Mathf.Lerp(-20f, 0f, s * 2f) : Mathf.Lerp(0f, 26f, (s - 0.5f) * 2f);
            return Mathf.Pow(10f, db / 20f);
        }

        void Update()
        {
            if (_clip == null) return;
            int pos = Microphone.GetPosition(_deviceArg);
            int len = _clip.samples;
            int count = (pos - _readPos + len) % len;
            if (count <= 0) { Decay(); return; }
            if (_read.Length < count) _read = new float[count];

            // Read with wrap-around
            int first = Mathf.Min(count, len - _readPos);
            var a = new float[first];
            _clip.GetData(a, _readPos);
            Array.Copy(a, 0, _read, 0, first);
            if (count > first)
            {
                var b = new float[count - first];
                _clip.GetData(b, 0);
                Array.Copy(b, 0, _read, first, count - first);
            }
            _readPos = (_readPos + count) % len;

            float gain = GainFromSensitivity(Settings.MicSensitivity);
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                float v = Mathf.Clamp(_read[i] * gain, -1f, 1f);
                _read[i] = v;
                sum += v * v;
            }
            float rms = Mathf.Sqrt((float)(sum / count));
            float db = 20f * Mathf.Log10(rms + 1e-6f);
            InstantLevel = Mathf.InverseLerp(-50f, -6f, db);
            // Fast attack, slower release
            Level = InstantLevel > Level ? Mathf.Lerp(Level, InstantLevel, 0.7f) : Mathf.MoveTowards(Level, InstantLevel, Time.unscaledDeltaTime * 1.6f);

            // Voice gate with a short hangover so words aren't chopped
            if (InstantLevel > Tuning.TalkThreshold * 0.6f) _gateUntil = Time.unscaledTime + 0.35f;
            Transmitting = TransmitEnabled && Time.unscaledTime < _gateUntil;

            for (int i = 0; i < count; i++)
            {
                _frame[_frameFill++] = _read[i];
                if (_frameFill == _frame.Length)
                {
                    _frameFill = 0;
                    if (Transmitting) Frame?.Invoke(MuLaw.Encode(_frame), SampleRate);
                }
            }
        }

        void Decay()
        {
            Level = Mathf.MoveTowards(Level, 0, Time.unscaledDeltaTime * 1.6f);
        }
    }

    public static class MuLaw
    {
        const int Bias = 0x84, Clip = 32635;

        public static byte[] Encode(float[] pcm)
        {
            var o = new byte[pcm.Length];
            for (int i = 0; i < pcm.Length; i++) o[i] = EncodeSample((short)Mathf.Clamp(pcm[i] * 32767f, -32768f, 32767f));
            return o;
        }

        static byte EncodeSample(short s)
        {
            int sign = (s >> 8) & 0x80;
            int v = sign != 0 ? -s : s;
            if (v > Clip) v = Clip;
            v += Bias;
            int exponent = 7;
            for (int mask = 0x4000; (v & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
            int mantissa = (v >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static float Decode(byte b)
        {
            int u = ~b & 0xFF;
            int sign = u & 0x80, exponent = (u >> 4) & 0x07, mantissa = u & 0x0F;
            int s = ((mantissa << 3) + Bias) << exponent;
            s -= Bias;
            return (sign != 0 ? -s : s) / 32768f;
        }
    }
}
