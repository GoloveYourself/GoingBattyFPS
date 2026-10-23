using UnityEngine;

namespace GoingBatty
{
    /// <summary>Player options, saved with PlayerPrefs.</summary>
    public static class Settings
    {
        const string P = "gb.";

        public static string PlayerName
        {
            get => PlayerPrefs.GetString(P + "name", "Bat" + Random.Range(100, 999));
            set { PlayerPrefs.SetString(P + "name", Sanitize(value, 20)); PlayerPrefs.Save(); }
        }

        /// <summary>Microphone device name. Empty = system default.</summary>
        public static string MicDevice
        {
            get => PlayerPrefs.GetString(P + "mic", "");
            set { PlayerPrefs.SetString(P + "mic", value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>0..1 slider. 0.5 = no gain. Maps to -20dB..+30dB of input gain.</summary>
        public static float MicSensitivity
        {
            get => PlayerPrefs.GetFloat(P + "micSens", 0.5f);
            set { PlayerPrefs.SetFloat(P + "micSens", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(P + "mouse", 1.0f);
            set { PlayerPrefs.SetFloat(P + "mouse", Mathf.Clamp(value, 0.1f, 5f)); PlayerPrefs.Save(); }
        }

        public static float Fov
        {
            get => PlayerPrefs.GetFloat(P + "fov", 100f);
            set { PlayerPrefs.SetFloat(P + "fov", Mathf.Clamp(value, 70f, 120f)); PlayerPrefs.Save(); }
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(P + "vol", 0.8f);
            set { PlayerPrefs.SetFloat(P + "vol", Mathf.Clamp01(value)); AudioListener.volume = value; PlayerPrefs.Save(); }
        }

        public static float VoiceVolume
        {
            get => PlayerPrefs.GetFloat(P + "voiceVol", 1.0f);
            set { PlayerPrefs.SetFloat(P + "voiceVol", Mathf.Clamp(value, 0f, 2f)); PlayerPrefs.Save(); }
        }

        /// <summary>Hold Q to ping without a mic. Off by default; useful for testing.</summary>
        public static bool KeyboardSonar
        {
            get => PlayerPrefs.GetInt(P + "keySonar", 0) == 1;
            set { PlayerPrefs.SetInt(P + "keySonar", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool AutoHop
        {
            get => PlayerPrefs.GetInt(P + "autoHop", 1) == 1;
            set { PlayerPrefs.SetInt(P + "autoHop", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static string Sanitize(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
            if (s.Length > max) s = s.Substring(0, max);
            return s;
        }
    }
}
