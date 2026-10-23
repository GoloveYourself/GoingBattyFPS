using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// The match. The host is authoritative over rules (kills, scores, respawns, the clock,
    /// teams, the Box). Each player moves themselves and reports position; the host relays.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        public static GameSession I { get; private set; }

        public Phase Phase { get; private set; } = Phase.Lobby;
        public readonly MatchSettings Rules = new MatchSettings();
        public readonly Dictionary<byte, PlayerInfo> Players = new Dictionary<byte, PlayerInfo>();
        public byte LocalSlot { get; private set; } = 255;
        public readonly int[] TeamScore = new int[2];
        public float TimeLeft { get; private set; } = -1;
        public readonly BoxState[] Boxes = { new BoxState(), new BoxState() };
        public string EndTitle = "", EndSubtitle = "";
        public float PostMatchSecondsLeft { get; private set; }
        public string DisconnectReason = "";
        /// <summary>Set when we leave on purpose, so we don't show a "disconnected" message.</summary>
        public bool ExpectDisconnect;

        public MapBuilder Map;
        public LocalPlayer Local { get; private set; }
        public readonly List<string> Feed = new List<string>();
        readonly List<float> _feedTimes = new List<float>();
        readonly Dictionary<byte, RemotePlayer> _remotes = new Dictionary<byte, RemotePlayer>();
        BoxObjective[] _boxViews;

        // Host-only state
        float _endTime, _clockTimer, _postMatchEnd;

        public bool InSession => Net.Active && LocalSlot != 255;
        public PlayerInfo LocalInfo => Players.TryGetValue(LocalSlot, out var p) ? p : null;
        public bool IsTeamMode => Rules.Mode == GameMode.CaptureTheBox;

        void Awake()
        {
            I = this;
            Net.Started += OnNetStarted;
            Net.Stopped += OnNetStopped;
            Net.HostReceive += OnHostReceive;
            Net.ClientReceive += OnClientReceive;
            Net.ClientLeft += OnClientLeft;
            if (MicInput.I != null) MicInput.I.Frame += OnMicFrame;
        }

        void Start()
        {
            if (MicInput.I != null) { MicInput.I.Frame -= OnMicFrame; MicInput.I.Frame += OnMicFrame; }
        }

        // ------------------------------------------------------------------ session lifecycle

        void OnNetStarted()
        {
            ResetSessionState();
            if (Net.IsHost)
            {
                var p = AddPlayer(Net.LocalId, Settings.PlayerName);
                SendWelcome(p);
            }
            else
            {
                var w = new ByteWriter().U8((byte)M.Hello).Str(Settings.PlayerName).Str(Application.version);
                Net.ToHost(w.ToArray(), true);
            }
            if (MicInput.I != null) MicInput.I.TransmitEnabled = true;
        }

        void OnNetStopped()
        {
            bool wasPlaying = LocalSlot != 255;
            ResetSessionState();
            if (MicInput.I != null) MicInput.I.TransmitEnabled = false;
            if (wasPlaying && !ExpectDisconnect && string.IsNullOrEmpty(DisconnectReason)) DisconnectReason = "Disconnected — the host left or the connection dropped.";
            ExpectDisconnect = false;
            UIRoot.I?.OnSessionEnded();
        }

        void ResetSessionState()
        {
            Phase = Phase.Lobby;
            LocalSlot = 255;
            Players.Clear();
            foreach (var r in _remotes.Values) if (r) Destroy(r.gameObject);
            _remotes.Clear();
            DestroyLocal();
            TeamScore[0] = TeamScore[1] = 0;
            TimeLeft = -1;
            Feed.Clear(); _feedTimes.Clear();
            for (int i = 0; i < 2; i++) { Boxes[i].Status = BoxStatus.Home; Boxes[i].CarrierSlot = 255; Boxes[i].Pos = Map != null ? Map.BoxHome[i] : Vector3.zero; }
            UpdateBoxViews();
        }

        public void ClearDisconnectReason() => DisconnectReason = "";

        // ------------------------------------------------------------------ host: players

        PlayerInfo AddPlayer(ulong clientId, string name)
        {
            byte slot = 0;
            while (Players.ContainsKey(slot)) slot++;
            var p = new PlayerInfo
            {
                ClientId = clientId,
                Slot = slot,
                Name = UniqueName(string.IsNullOrWhiteSpace(name) ? "Bat" : Settings.Sanitize(name, 20)),
                Team = SmallerTeam(),
                Alive = false,
            };
            Players[slot] = p;
            return p;
        }

        string UniqueName(string n)
        {
            string baseName = n; int k = 2;
            while (Players.Values.Any(p => p.Name == n)) n = baseName + " " + k++;
            return n;
        }

        byte SmallerTeam()
        {
            int a = Players.Values.Count(p => p.Team == 0), b = Players.Values.Count(p => p.Team == 1);
            return (byte)(a <= b ? 0 : 1);
        }

        PlayerInfo ByClient(ulong id) => Players.Values.FirstOrDefault(p => p.ClientId == id);

        void OnClientLeft(ulong clientId)
        {
            var p = ByClient(clientId);
            if (p == null) return;
            for (int t = 0; t < 2; t++)
                if (Boxes[t].Status == BoxStatus.Carried && Boxes[t].CarrierSlot == p.Slot) DropBox(t, p.Pos);
            Players.Remove(p.Slot);
            AddFeedAll(p.Name + " left");
            BroadcastRoster();
        }

        // ------------------------------------------------------------------ host: messages in

        void OnHostReceive(ulong sender, byte[] data)
        {
            var r = new ByteReader(data);
            var type = (M)r.U8();
            if (type == M.Hello) { HandleHello(sender, r); return; }

            var p = ByClient(sender);
            if (p == null) return;

            switch (type)
            {
                case M.State:
                {
                    p.Pos = r.V3();
                    p.Yaw = r.F32();
                    float pitch = r.F32();
                    var vel = r.V3();
                    var w = new ByteWriter(48).U8((byte)M.PState).U8(p.Slot).V3(p.Pos).F32(p.Yaw).F32(pitch).V3(vel);
                    Net.ToAll(w.ToArray(), false, sender);
                    break;
                }
                case M.Voice:
                {
                    int rate = r.I32();
                    var frame = r.Blob();
                    var w = new ByteWriter(frame.Length + 12).U8((byte)M.PVoice).U8(p.Slot).I32(rate).Blob(frame);
                    Net.ToAll(w.ToArray(), false, sender);
                    break;
                }
                case M.Sonar:
                {
                    if (!p.Alive || Phase != Phase.Playing) return;
                    var origin = r.V3(); var dir = r.V3(); float strength = Mathf.Clamp01(r.F32());
                    var w = new ByteWriter(40).U8((byte)M.PSonar).U8(p.Slot).V3(origin).V3(dir).F32(strength);
                    Net.ToAll(w.ToArray(), true, sender);
                    break;
                }
                case M.Blast:
                {
                    if (!p.Alive) return;
                    var pos = r.V3(); var dir = r.V3();
                    var w = new ByteWriter(32).U8((byte)M.PBlast).U8(p.Slot).V3(pos).V3(dir);
                    Net.ToAll(w.ToArray(), true, sender);
                    break;
                }
                case M.Shoot:
                {
                    if (!p.Alive || Phase != Phase.Playing) return;
                    var origin = r.V3(); var dir = r.V3(); int seed = r.I32();
                    int n = r.U8();
                    var claimed = new List<byte>();
                    for (int i = 0; i < n; i++) claimed.Add(r.U8());
                    var w = new ByteWriter(40).U8((byte)M.PShot).U8(p.Slot).V3(origin).V3(dir).I32(seed);
                    Net.ToAll(w.ToArray(), true, sender);
                    foreach (var victim in claimed) TryKill(p, victim, origin);
                    break;
                }
            }
        }

        void HandleHello(ulong sender, ByteReader r)
        {
            if (ByClient(sender) != null) return;
            string name = r.Str();
            string version = r.HasMore ? r.Str() : "?";
            if (version != Application.version)
            {
                Reject(sender, $"Version mismatch: the host is on v{Application.version}, you are on v{version}. Update the game and try again.");
                return;
            }
            if (Players.Count >= Tuning.MaxPlayers)
            {
                Reject(sender, "Lobby is full.");
                return;
            }
            var p = AddPlayer(sender, name);
            SendWelcome(p);
            BroadcastRoster(except: sender);
            AddFeedAll(p.Name + " joined");
            if (Phase == Phase.Playing) p.RespawnAt = Time.time + 0.5f;
        }

        void Reject(ulong id, string reason)
        {
            Net.ToClient(id, new ByteWriter().U8((byte)M.Reject).Str(reason).ToArray(), true);
            StartCoroutine(DisconnectSoon(id));
        }

        System.Collections.IEnumerator DisconnectSoon(ulong id)
        {
            yield return new WaitForSeconds(0.5f);
            Net.DisconnectClient(id);
        }

        // ------------------------------------------------------------------ host: messages out

        void WriteRoster(ByteWriter w)
        {
            w.U8((byte)Players.Count);
            foreach (var p in Players.Values)
                w.U8(p.Slot).U64(p.ClientId).Str(p.Name).U8(p.Team).I32(p.Score).I32(p.Deaths).Bool(p.Alive);
            w.I32(TeamScore[0]).I32(TeamScore[1]);
        }

        void SendWelcome(PlayerInfo p)
        {
            var w = new ByteWriter(512).U8((byte)M.Welcome).U8(p.Slot).U8((byte)Phase);
            Rules.Write(w);
            w.F32(TimeLeft);
            WriteRoster(w);
            for (int t = 0; t < 2; t++) WriteBox(w, t);
            Net.ToClient(p.ClientId, w.ToArray(), true);
        }

        void BroadcastRoster(ulong except = ulong.MaxValue)
        {
            var w = new ByteWriter(512).U8((byte)M.Roster);
            WriteRoster(w);
            Net.ToAll(w.ToArray(), true, except);
        }

        void WriteBox(ByteWriter w, int t)
        {
            var b = Boxes[t];
            w.U8((byte)t).U8((byte)b.Status).U8(b.CarrierSlot).V3(b.Pos);
        }

        void BroadcastBox(int t)
        {
            var w = new ByteWriter(32).U8((byte)M.Box);
            WriteBox(w, t);
            Net.ToAll(w.ToArray(), true);
        }

        void AddFeedAll(string text)
        {
            // Feed text goes out on the Capture message with team/slot = 255 (no capture).
            var w = new ByteWriter(64).U8((byte)M.Capture).U8(255).U8(255).Str(text);
            Net.ToAll(w.ToArray(), true);
        }

        // ------------------------------------------------------------------ host: rules

        /// <summary>Host: change lobby rules (only in the lobby).</summary>
        public void HostSetRules(MatchSettings s)
        {
            if (!Net.IsHost || Phase != Phase.Lobby) return;
            var w = new ByteWriter(16).U8((byte)M.Rules);
            s.Write(w);
            Net.ToAll(w.ToArray(), true);
        }

        public void HostSetTeam(byte slot, byte team)
        {
            if (!Net.IsHost || Phase != Phase.Lobby || !Players.TryGetValue(slot, out var p)) return;
            p.Team = team;
            BroadcastRoster();
        }

        public void HostShuffleTeams()
        {
            if (!Net.IsHost) return;
            var list = Players.Values.OrderBy(_ => Random.value).ToList();
            for (int i = 0; i < list.Count; i++) list[i].Team = (byte)(i % 2);
            BroadcastRoster();
        }

        public void HostStartMatch()
        {
            if (!Net.IsHost || Phase != Phase.Lobby) return;
            foreach (var p in Players.Values) { p.Score = 0; p.Deaths = 0; p.Alive = false; p.RespawnAt = Time.time + 0.2f; }
            TeamScore[0] = TeamScore[1] = 0;
            if (Rules.Mode == GameMode.CaptureTheBox)
            {
                // Keep host-chosen teams if balanced; otherwise rebalance randomly.
                int a = Players.Values.Count(p => p.Team == 0), b = Players.Values.Count(p => p.Team == 1);
                if (Mathf.Abs(a - b) > 1) HostShuffleTeams();
            }
            for (int t = 0; t < 2; t++) { Boxes[t].Status = BoxStatus.Home; Boxes[t].CarrierSlot = 255; Boxes[t].Pos = Map.BoxHome[t]; }
            _endTime = Rules.TimeMinutes > 0 ? Time.time + Rules.TimeMinutes * 60f : -1;
            var w = new ByteWriter(256).U8((byte)M.MatchStart);
            Rules.Write(w);
            w.F32(Rules.TimeMinutes > 0 ? Rules.TimeMinutes * 60f : -1);
            WriteRoster(w);
            Net.ToAll(w.ToArray(), true);
            for (int t = 0; t < 2; t++) BroadcastBox(t);
        }

        void TryKill(PlayerInfo killer, byte victimSlot, Vector3 shotOrigin)
        {
            if (!Players.TryGetValue(victimSlot, out var v) || !v.Alive || v == killer) return;
            if (IsTeamMode && v.Team == killer.Team) return;
            if (Time.time - v.SpawnedAt < Tuning.SpawnProtection * 0.5f) return;
            // Plausibility: within range of the shooter's reported position (lenient for latency)
            if (Vector3.Distance(killer.Pos, v.Pos) > Tuning.WeaponRange + 8f) return;
            if (Vector3.Distance(shotOrigin, killer.Pos + Vector3.up * Tuning.EyeHeight) > 6f) return;

            v.Alive = false;
            v.Deaths++;
            v.RespawnAt = Time.time + Tuning.RespawnDelay;
            killer.Score++; // eliminations (in Capture the Box these are shown but captures win)

            for (int t = 0; t < 2; t++)
                if (Boxes[t].Status == BoxStatus.Carried && Boxes[t].CarrierSlot == v.Slot) DropBox(t, v.Pos);

            var w = new ByteWriter(16).U8((byte)M.Kill).U8(killer.Slot).U8(v.Slot);
            Net.ToAll(w.ToArray(), true);
            BroadcastRoster();

            if (Rules.Mode == GameMode.Deathmatch && killer.Score >= Rules.KillTarget)
                EndMatch(killer.Name + " wins!", $"First to {Rules.KillTarget} eliminations");
        }

        void HostSpawn(PlayerInfo p)
        {
            Vector3 pos = PickSpawn(p);
            float yaw = Random.Range(0f, 360f);
            if (IsTeamMode)
            {
                // face toward the middle of the map
                yaw = p.Team == 0 ? 90f : 270f;
            }
            p.Alive = true;
            p.Pos = pos;
            p.RespawnAt = -1;
            p.SpawnedAt = Time.time;
            var w = new ByteWriter(24).U8((byte)M.Spawn).U8(p.Slot).V3(pos).F32(yaw);
            Net.ToAll(w.ToArray(), true);
        }

        Vector3 PickSpawn(PlayerInfo p)
        {
            var candidates = IsTeamMode ? Map.TeamSpawns[p.Team] : Map.DmSpawns;
            if (candidates.Count == 0) return Vector3.up;
            var enemies = Players.Values.Where(o => o != p && o.Alive && (!IsTeamMode || o.Team != p.Team)).Select(o => o.Pos).ToList();
            if (enemies.Count == 0) return candidates[Random.Range(0, candidates.Count)];
            // Pick randomly among the 4 spawns farthest from any enemy
            var scored = candidates.Select(c => (c, d: enemies.Min(e => (e - c).sqrMagnitude))).OrderByDescending(x => x.d).Take(4).ToList();
            return scored[Random.Range(0, scored.Count)].c;
        }

        void EndMatch(string title, string subtitle)
        {
            if (Phase != Phase.Playing) return;
            var w = new ByteWriter(128).U8((byte)M.MatchEnd).Str(title).Str(subtitle);
            WriteRoster(w);
            Net.ToAll(w.ToArray(), true);
            _postMatchEnd = Time.time + 10f;
        }

        void DropBox(int t, Vector3 pos)
        {
            var b = Boxes[t];
            b.Status = BoxStatus.Dropped;
            b.CarrierSlot = 255;
            b.Pos = pos + Vector3.up * 0.6f;
            b.DroppedAt = Time.time;
            BroadcastBox(t);
        }

        void HostUpdate()
        {
            if (Phase == Phase.PostMatch)
            {
                if (Time.time >= _postMatchEnd)
                {
                    foreach (var p in Players.Values) { p.Alive = false; p.RespawnAt = -1; }
                    Net.ToAll(new ByteWriter().U8((byte)M.ToLobby).ToArray(), true);
                    BroadcastRoster();
                }
                return;
            }
            if (Phase != Phase.Playing) return;

            foreach (var p in Players.Values.ToList())
                if (!p.Alive && p.RespawnAt > 0 && Time.time >= p.RespawnAt) HostSpawn(p);

            _clockTimer -= Time.deltaTime;
            if (_clockTimer <= 0)
            {
                _clockTimer = 1f;
                float left = _endTime > 0 ? Mathf.Max(0, _endTime - Time.time) : -1;
                var w = new ByteWriter(16).U8((byte)M.Clock).F32(left).I32(TeamScore[0]).I32(TeamScore[1]);
                Net.ToAll(w.ToArray(), false);
            }

            if (Rules.Mode == GameMode.CaptureTheBox) HostBoxLogic();

            if (_endTime > 0 && Time.time >= _endTime) HostTimeUp();
        }

        void HostTimeUp()
        {
            if (Rules.Mode == GameMode.Deathmatch)
            {
                var ordered = Players.Values.OrderByDescending(p => p.Score).ToList();
                if (ordered.Count == 0) { EndMatch("Time's up", ""); return; }
                if (ordered.Count > 1 && ordered[0].Score == ordered[1].Score) EndMatch("Draw!", "Time's up — tied on eliminations");
                else EndMatch(ordered[0].Name + " wins!", "Most eliminations when time ran out");
            }
            else
            {
                if (TeamScore[0] == TeamScore[1]) EndMatch("Draw!", "Time's up — captures tied");
                else
                {
                    int t = TeamScore[0] > TeamScore[1] ? 0 : 1;
                    EndMatch(Look.TeamNames[t] + " team wins!", "Most captures when time ran out");
                }
            }
        }

        void HostBoxLogic()
        {
            for (int t = 0; t < 2; t++)
            {
                var b = Boxes[t];
                if (b.Status == BoxStatus.Carried)
                {
                    if (!Players.TryGetValue(b.CarrierSlot, out var c) || !c.Alive) { DropBox(t, c != null ? c.Pos : b.Pos); continue; }
                    b.Pos = c.Pos + Vector3.up * 1.0f;
                    // Carrier reaches their own base -> capture
                    if (Map.BaseZone[c.Team].Contains(c.Pos + Vector3.up * 0.5f))
                    {
                        TeamScore[c.Team]++;
                        b.Status = BoxStatus.Home; b.CarrierSlot = 255; b.Pos = Map.BoxHome[t];
                        var w = new ByteWriter(64).U8((byte)M.Capture).U8(c.Team).U8(c.Slot).Str($"{c.Name} captured the {Look.TeamNames[t]} box!");
                        Net.ToAll(w.ToArray(), true);
                        BroadcastBox(t);
                        BroadcastRoster();
                        if (TeamScore[c.Team] >= Rules.CaptureTarget)
                            EndMatch(Look.TeamNames[c.Team] + " team wins!", $"First to {Rules.CaptureTarget} captures");
                    }
                    continue;
                }

                if (b.Status == BoxStatus.Dropped && Time.time - b.DroppedAt > Tuning.BoxAutoReturn)
                {
                    b.Status = BoxStatus.Home; b.Pos = Map.BoxHome[t];
                    BroadcastBox(t);
                    continue;
                }

                foreach (var p in Players.Values)
                {
                    if (!p.Alive) continue;
                    float d = Vector3.Distance(p.Pos + Vector3.up * 0.9f, b.Pos);
                    if (d > Tuning.BoxPickupRadius) continue;
                    if (p.Team != t)
                    {
                        // Can't carry both boxes at once
                        if (Boxes[1 - t].Status == BoxStatus.Carried && Boxes[1 - t].CarrierSlot == p.Slot) continue;
                        b.Status = BoxStatus.Carried; b.CarrierSlot = p.Slot;
                        BroadcastBox(t);
                        AddFeedAll($"{p.Name} grabbed the {Look.TeamNames[t]} box!");
                        break;
                    }
                    if (b.Status == BoxStatus.Dropped)
                    {
                        b.Status = BoxStatus.Home; b.Pos = Map.BoxHome[t];
                        BroadcastBox(t);
                        AddFeedAll($"{p.Name} returned the {Look.TeamNames[t]} box");
                        break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ everyone: messages in

        void ReadRoster(ByteReader r)
        {
            int n = r.U8();
            var seen = new HashSet<byte>();
            for (int i = 0; i < n; i++)
            {
                byte slot = r.U8();
                if (!Players.TryGetValue(slot, out var p)) { p = new PlayerInfo { Slot = slot }; Players[slot] = p; }
                p.ClientId = r.U64();
                p.Name = r.Str();
                p.Team = r.U8();
                p.Score = r.I32();
                p.Deaths = r.I32();
                p.Alive = r.Bool();
                seen.Add(slot);
            }
            TeamScore[0] = r.I32();
            TeamScore[1] = r.I32();
            foreach (var s in Players.Keys.ToList()) if (!seen.Contains(s)) Players.Remove(s);
            SyncRemotes();
        }

        void SyncRemotes()
        {
            foreach (var slot in _remotes.Keys.ToList())
                if (!Players.ContainsKey(slot) || slot == LocalSlot)
                {
                    if (_remotes[slot]) Destroy(_remotes[slot].gameObject);
                    _remotes.Remove(slot);
                }
            foreach (var p in Players.Values)
            {
                if (p.Slot == LocalSlot) continue;
                if (!_remotes.TryGetValue(p.Slot, out var rp) || rp == null)
                {
                    var go = new GameObject("Remote " + p.Name);
                    go.transform.SetParent(transform, false);
                    rp = go.AddComponent<RemotePlayer>();
                    rp.Slot = p.Slot;
                    _remotes[p.Slot] = rp;
                }
                rp.SetAlive(p.Alive && Phase == Phase.Playing);
            }
        }

        public RemotePlayer Remote(byte slot) => _remotes.TryGetValue(slot, out var r) ? r : null;
        public IEnumerable<RemotePlayer> Remotes => _remotes.Values;

        void OnClientReceive(byte[] data)
        {
            var r = new ByteReader(data);
            var type = (M)r.U8();
            switch (type)
            {
                case M.Welcome:
                {
                    LocalSlot = r.U8();
                    Phase = (Phase)r.U8();
                    Rules.Read(r);
                    TimeLeft = r.F32();
                    ReadRoster(r);
                    for (int i = 0; i < 2; i++) ReadBox(r);
                    if (Phase == Phase.Playing) EnsureLocal();
                    SyncRemotes();
                    UIRoot.I?.OnJoinedSession();
                    break;
                }
                case M.Reject:
                    DisconnectReason = r.Str();
                    _ = Bootstrap.Lobby.LeaveAsync();
                    break;
                case M.Roster:
                    ReadRoster(r);
                    break;
                case M.Rules:
                    Rules.Read(r);
                    break;
                case M.PState:
                {
                    byte slot = r.U8();
                    var rp = Remote(slot);
                    if (rp != null) rp.ReceiveState(r.V3(), r.F32(), r.F32(), r.V3());
                    break;
                }
                case M.PVoice:
                {
                    byte slot = r.U8();
                    int rate = r.I32();
                    var frame = r.Blob();
                    var rp = Remote(slot);
                    if (rp != null) rp.Voice.Push(frame, rate);
                    break;
                }
                case M.PSonar:
                {
                    byte slot = r.U8();
                    var origin = r.V3(); var dir = r.V3(); float s = r.F32();
                    if (slot == LocalSlot) break;
                    SonarSystem.I.Pulse(origin, dir, s, SonarColorFor(slot), slot);
                    break;
                }
                case M.PShot:
                {
                    byte slot = r.U8();
                    var origin = r.V3(); var dir = r.V3(); int seed = r.I32();
                    if (slot == LocalSlot) break;
                    SonarSystem.I.RemoteShot(origin, dir, seed, SonarColorFor(slot), slot);
                    break;
                }
                case M.PBlast:
                {
                    byte slot = r.U8();
                    var pos = r.V3(); var dir = r.V3();
                    if (slot == LocalSlot) break;
                    SonarSystem.I.RemoteBlast(pos, dir, SonarColorFor(slot), slot);
                    break;
                }
                case M.Kill:
                {
                    byte killer = r.U8(), victim = r.U8();
                    string kn = NameOf(killer), vn = NameOf(victim);
                    AddFeed($"{kn} blasted {vn}");
                    if (Players.TryGetValue(victim, out var vp)) vp.Alive = false;
                    if (victim == LocalSlot && Local != null) Local.Die(kn);
                    else Remote(victim)?.SetAlive(false);
                    if (killer == LocalSlot && victim != LocalSlot) Sfx.Ui(Sfx.KillConfirm, 0.8f);
                    break;
                }
                case M.Spawn:
                {
                    byte slot = r.U8();
                    var pos = r.V3(); float yaw = r.F32();
                    if (Players.TryGetValue(slot, out var sp)) sp.Alive = true;
                    if (slot == LocalSlot) { EnsureLocal(); Local.Spawn(pos, yaw); }
                    else { var rp = Remote(slot); if (rp != null) { rp.Teleport(pos, yaw); rp.SetAlive(true); } }
                    break;
                }
                case M.MatchStart:
                {
                    Rules.Read(r);
                    TimeLeft = r.F32();
                    ReadRoster(r);
                    Phase = Phase.Playing;
                    Feed.Clear(); _feedTimes.Clear();
                    EnsureLocal();
                    SyncRemotes();
                    UpdateBoxViews();
                    UIRoot.I?.OnMatchStarted();
                    break;
                }
                case M.MatchEnd:
                {
                    EndTitle = r.Str();
                    EndSubtitle = r.Str();
                    ReadRoster(r);
                    Phase = Phase.PostMatch;
                    PostMatchSecondsLeft = 10f;
                    if (Local != null) Local.Freeze();
                    Sfx.Ui(Sfx.Capture, 0.7f);
                    break;
                }
                case M.ToLobby:
                    Phase = Phase.Lobby;
                    DestroyLocal();
                    SyncRemotes();
                    UpdateBoxViews();
                    UIRoot.I?.OnReturnedToLobby();
                    break;
                case M.Clock:
                    TimeLeft = r.F32();
                    TeamScore[0] = r.I32();
                    TeamScore[1] = r.I32();
                    break;
                case M.Box:
                    ReadBox(r);
                    break;
                case M.Capture:
                {
                    byte team = r.U8(), slot = r.U8();
                    string text = r.Str();
                    AddFeed(text);
                    if (team != 255) Sfx.Ui(Sfx.Capture, 0.9f);
                    break;
                }
            }
        }

        void ReadBox(ByteReader r)
        {
            int t = r.U8();
            var b = Boxes[t];
            var prevStatus = b.Status;
            b.Status = (BoxStatus)r.U8();
            b.CarrierSlot = r.U8();
            b.Pos = r.V3();
            if (prevStatus != BoxStatus.Carried && b.Status == BoxStatus.Carried) Sfx.At(Sfx.Pickup, b.Pos, 1f, 40f);
            UpdateBoxViews();
        }

        void UpdateBoxViews()
        {
            if (_boxViews == null || Map == null) return;
            bool show = InSession && Phase != Phase.Lobby && Rules.Mode == GameMode.CaptureTheBox;
            for (int t = 0; t < 2; t++) _boxViews[t].SetVisible(show);
        }

        public void CreateBoxViews()
        {
            _boxViews = new BoxObjective[2];
            for (int t = 0; t < 2; t++)
            {
                var go = new GameObject("Box " + Look.TeamNames[t]);
                go.transform.SetParent(transform, false);
                _boxViews[t] = go.AddComponent<BoxObjective>();
                _boxViews[t].Init(t, Map);
            }
            UpdateBoxViews();
        }

        public string NameOf(byte slot) => Players.TryGetValue(slot, out var p) ? p.Name : "?";

        public Color SonarColorFor(byte slot)
        {
            if (IsTeamMode && Players.TryGetValue(slot, out var p)) return Look.TeamColors[p.Team % 2];
            return Look.ColorForSlot(slot);
        }

        void AddFeed(string s)
        {
            Feed.Add(s);
            _feedTimes.Add(Time.time);
            while (Feed.Count > 6) { Feed.RemoveAt(0); _feedTimes.RemoveAt(0); }
        }

        void EnsureLocal()
        {
            if (Local != null) return;
            var go = new GameObject("Local Player");
            go.transform.SetParent(transform, false);
            Local = go.AddComponent<LocalPlayer>();
            Local.Slot = LocalSlot;
        }

        void DestroyLocal()
        {
            if (Local != null) Destroy(Local.gameObject);
            Local = null;
            Bootstrap.ReturnCameraToMenu();
        }

        // ------------------------------------------------------------------ local player -> host

        public void SendState(Vector3 pos, float yaw, float pitch, Vector3 vel)
        {
            var w = new ByteWriter(48).U8((byte)M.State).V3(pos).F32(yaw).F32(pitch).V3(vel);
            Net.ToHost(w.ToArray(), false);
        }

        public void SendSonar(Vector3 origin, Vector3 dir, float strength)
        {
            Net.ToHost(new ByteWriter(40).U8((byte)M.Sonar).V3(origin).V3(dir).F32(strength).ToArray(), true);
        }

        public void SendShot(Vector3 origin, Vector3 dir, int seed, List<byte> hits)
        {
            var w = new ByteWriter(48).U8((byte)M.Shoot).V3(origin).V3(dir).I32(seed).U8((byte)hits.Count);
            foreach (var h in hits) w.U8(h);
            Net.ToHost(w.ToArray(), true);
        }

        public void SendBlast(Vector3 pos, Vector3 dir)
        {
            Net.ToHost(new ByteWriter(32).U8((byte)M.Blast).V3(pos).V3(dir).ToArray(), true);
        }

        void OnMicFrame(byte[] frame, int rate)
        {
            if (!InSession) return;
            var w = new ByteWriter(frame.Length + 12).U8((byte)M.Voice).I32(rate).Blob(frame);
            Net.ToHost(w.ToArray(), false);
        }

        // ------------------------------------------------------------------ tick

        void Update()
        {
            if (Net.IsHost && InSession) HostUpdate();

            if (Phase == Phase.Playing && TimeLeft > 0) TimeLeft = Mathf.Max(0, TimeLeft - Time.deltaTime);
            if (Phase == Phase.PostMatch) PostMatchSecondsLeft = Mathf.Max(0, PostMatchSecondsLeft - Time.deltaTime);

            for (int i = _feedTimes.Count - 1; i >= 0; i--)
                if (Time.time - _feedTimes[i] > 7f) { Feed.RemoveAt(i); _feedTimes.RemoveAt(i); }
        }

        /// <summary>Transform of whoever carries the box, for visuals. Null if nobody / unknown.</summary>
        public Transform CarrierTransform(byte slot)
        {
            if (slot == LocalSlot) return Local != null ? Local.transform : null;
            var r = Remote(slot);
            return r != null ? r.transform : null;
        }
    }
}
