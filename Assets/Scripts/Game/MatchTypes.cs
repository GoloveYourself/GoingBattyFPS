using UnityEngine;

namespace GoingBatty
{
    public enum GameMode : byte { Deathmatch = 0, CaptureTheBox = 1 }
    public enum Phase : byte { Lobby = 0, Playing = 1, PostMatch = 2 }
    public enum BoxStatus : byte { Home = 0, Carried = 1, Dropped = 2 }

    /// <summary>Match rules the host picks in the lobby.</summary>
    public class MatchSettings
    {
        public static readonly int[] KillOptions = { 5, 10, 15, 20, 25, 30 };
        public static readonly int[] CaptureOptions = { 3, 5, 7, 10 };
        public static readonly int[] TimeOptions = { 2, 5, 10, 0 }; // minutes, 0 = no limit
        public static readonly int[] BotOptions = { 0, 1, 2, 3, 4, 5, 6, 7 };

        public GameMode Mode = GameMode.Deathmatch;
        public int KillTarget = 15;
        public int CaptureTarget = 3;
        public int DmTimeMinutes = 5;
        public int CtbTimeMinutes = 5;
        /// <summary>AI players the host adds. Humans always take priority: bots never push the lobby past 8.</summary>
        public int Bots = 0;

        public int TimeMinutes => Mode == GameMode.Deathmatch ? DmTimeMinutes : CtbTimeMinutes;
        public int Target => Mode == GameMode.Deathmatch ? KillTarget : CaptureTarget;

        public static string TimeLabel(int m) => m == 0 ? "No Time Limit" : m + " minutes";

        public void Write(ByteWriter w)
        {
            w.U8((byte)Mode).U8((byte)KillTarget).U8((byte)CaptureTarget).U8((byte)DmTimeMinutes).U8((byte)CtbTimeMinutes).U8((byte)Bots);
        }

        public void Read(ByteReader r)
        {
            Mode = (GameMode)r.U8();
            KillTarget = r.U8();
            CaptureTarget = r.U8();
            DmTimeMinutes = r.U8();
            CtbTimeMinutes = r.U8();
            Bots = r.U8();
        }

        public static int Cycle(int[] options, int current, int dir)
        {
            int i = System.Array.IndexOf(options, current);
            if (i < 0) i = 0;
            i = (i + dir + options.Length) % options.Length;
            return options[i];
        }
    }

    public class PlayerInfo
    {
        public ulong ClientId;
        public byte Slot;
        public string Name = "";
        public byte Team;
        public int Score;
        public int Deaths;
        public bool Alive;
        public bool IsBot;

        // Host-side bookkeeping
        public Vector3 Pos;
        public float Yaw;
        public float RespawnAt = -1;
        public float SpawnedAt;
    }

    public class BoxState
    {
        public BoxStatus Status;
        public byte CarrierSlot = 255;
        public Vector3 Pos;
        public float DroppedAt;
    }

    /// <summary>Network message ids.</summary>
    public enum M : byte
    {
        // client -> host
        Hello = 1, State, Voice, Sonar, Shoot, Blast,
        // host -> clients
        Welcome = 50, Reject, Roster, PState, PVoice, PSonar, PShot, PBlast,
        Kill, Spawn, MatchStart, MatchEnd, Clock, Box, Capture, Rules, ToLobby,
    }
}
