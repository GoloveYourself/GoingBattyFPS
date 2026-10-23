using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// All menus and the HUD, drawn with Unity's immediate-mode GUI (pure code, no assets).
    /// Screens: Main, Browse, Create, Lobby, Options, In-game (HUD + pause + scoreboard).
    /// </summary>
    public class UIRoot : MonoBehaviour
    {
        public static UIRoot I { get; private set; }

        enum Screen { Main, Browse, Create, Connecting, Lobby, Options, InGame }

        Screen _screen = Screen.Main;
        Screen _optionsReturn = Screen.Main;
        bool _paused;
        string _status = "";
        string _nameField;
        string _lobbyName = "";
        bool _private;
        string _password = "";
        string _filter = "";
        string _joinCode = "";
        string _joinCodePassword = "";
        List<LobbyListing> _listings = new List<LobbyListing>();
        bool _loadingList;
        LobbyListing _passwordTarget;
        string _passwordPrompt = "";
        Vector2 _scroll, _micScroll;
        bool _quitConfirm;

        // Styles
        GUIStyle _title, _h1, _h2, _body, _small, _dim, _btn, _btnSel, _field, _box, _center, _feed, _big;
        Texture2D _white;
        float _scale = 1f;
        float VW => UnityEngine.Screen.width / _scale;
        float VH => UnityEngine.Screen.height / _scale;

        static readonly Color Accent = new Color(0.35f, 0.95f, 1f);
        static readonly Color Dim = new Color(1, 1, 1, 0.55f);

        public bool GameplayInputAllowed => _screen == Screen.InGame && !_paused && Application.isFocused;

        void Awake()
        {
            I = this;
            _nameField = Settings.PlayerName;
        }

        // ---------------------------------------------------------------- session callbacks

        public void OnJoinedSession()
        {
            _status = "";
            var gs = GameSession.I;
            _screen = gs.Phase == Phase.Lobby ? Screen.Lobby : Screen.InGame;
            _paused = false;
        }

        public void OnMatchStarted() { _screen = Screen.InGame; _paused = false; }
        public void OnReturnedToLobby() { _screen = Screen.Lobby; _paused = false; SonarSystem.I?.ClearAll(); }

        public void OnSessionEnded()
        {
            if (_screen == Screen.Main || _screen == Screen.Browse || _screen == Screen.Create) return;
            _screen = Screen.Main;
            _paused = false;
            SonarSystem.I?.ClearAll();
            _status = GameSession.I != null ? GameSession.I.DisconnectReason : "";
            GameSession.I?.ClearDisconnectReason();
        }

        // ---------------------------------------------------------------- input / cursor

        void Update()
        {
            bool lockCursor = GameplayInputAllowed;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;

            if (_screen == Screen.InGame && InputWrap.KeyDown(InputWrap.KeyName.Escape)) _paused = !_paused;
        }

        // ---------------------------------------------------------------- styles

        void EnsureStyles()
        {
            if (_title != null) return;
            _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply();
            Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

            _title = new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _big = new GUIStyle(_title) { fontSize = 64, alignment = TextAnchor.MiddleCenter };
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _h2 = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true, normal = { textColor = Color.white } };
            _small = new GUIStyle(_body) { fontSize = 18 };
            _dim = new GUIStyle(_small) { normal = { textColor = Dim } };
            _center = new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter };
            _feed = new GUIStyle(_small) { alignment = TextAnchor.UpperRight, wordWrap = false };

            var bg = Tex(new Color(0, 0, 0, 0.6f));
            var bgHover = Tex(new Color(0.35f, 0.95f, 1f, 0.18f));
            var bgActive = Tex(new Color(0.35f, 0.95f, 1f, 0.35f));
            _btn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 24, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(18, 18, 10, 10),
                normal = { background = bg, textColor = Color.white },
                hover = { background = bgHover, textColor = Color.white },
                active = { background = bgActive, textColor = Color.white },
                border = new RectOffset(0, 0, 0, 0),
            };
            _btnSel = new GUIStyle(_btn) { normal = { background = bgActive, textColor = Color.white } };
            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 24, padding = new RectOffset(12, 12, 8, 8),
                normal = { background = Tex(new Color(1, 1, 1, 0.08f)), textColor = Color.white },
                focused = { background = Tex(new Color(1, 1, 1, 0.14f)), textColor = Color.white },
                hover = { background = Tex(new Color(1, 1, 1, 0.11f)), textColor = Color.white },
            };
            _box = new GUIStyle(GUI.skin.box) { normal = { background = Tex(new Color(0, 0, 0, 0.78f)) }, padding = new RectOffset(28, 28, 24, 24) };
        }

        void Frame(Rect r, Color c, float t = 2f)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), _white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), _white);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), _white);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), _white);
            GUI.color = old;
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        bool Btn(string label, params GUILayoutOption[] o)
        {
            bool b = GUILayout.Button(label, _btn, o);
            if (b) Sfx.Ui(Sfx.Click, 0.6f);
            return b;
        }

        // ---------------------------------------------------------------- main draw

        void OnGUI()
        {
            EnsureStyles();
            _scale = UnityEngine.Screen.height / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_scale, _scale, 1));

            switch (_screen)
            {
                case Screen.Main: DrawMain(); break;
                case Screen.Browse: DrawBrowse(); break;
                case Screen.Create: DrawCreate(); break;
                case Screen.Connecting: DrawConnecting(); break;
                case Screen.Lobby: DrawLobby(); break;
                case Screen.Options: DrawOptions(); break;
                case Screen.InGame: DrawInGame(); break;
            }
        }

        // ---------------------------------------------------------------- main menu

        void DrawMain()
        {
            Fill(new Rect(0, 0, 760, VH), new Color(0, 0, 0, 0.55f));
            GUILayout.BeginArea(new Rect(110, 120, 600, VH - 200));
            GUILayout.Label("GOING BATTY", _title);
            GUILayout.Label("You are blind. Speak to see. Scream to see more.", _dim);
            GUILayout.Space(40);

            GUILayout.Label("Your name", _small);
            GUI.SetNextControlName("name");
            string n = GUILayout.TextField(_nameField, 20, _field, GUILayout.Width(460));
            if (n != _nameField) { _nameField = n; Settings.PlayerName = n; }
            GUILayout.Space(28);

            if (Btn("Find a Lobby", GUILayout.Width(460))) { _screen = Screen.Browse; RefreshList(); }
            GUILayout.Space(8);
            if (Btn("Create a Lobby", GUILayout.Width(460)))
            {
                _screen = Screen.Create;
                if (string.IsNullOrWhiteSpace(_lobbyName)) _lobbyName = Settings.PlayerName + "'s lobby";
            }
            GUILayout.Space(8);
            if (Btn("Options", GUILayout.Width(460))) { _optionsReturn = Screen.Main; _screen = Screen.Options; }
            GUILayout.Space(8);
            if (Btn("Quit", GUILayout.Width(460))) Application.Quit();

            GUILayout.Space(24);
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, _body, GUILayout.Width(560));
            if (!Bootstrap.Lobby.Ready && !string.IsNullOrEmpty(Bootstrap.Lobby.LastError))
                GUILayout.Label(Bootstrap.Lobby.LastError, _dim, GUILayout.Width(560));
            GUILayout.EndArea();

            GUI.Label(new Rect(110, VH - 60, 600, 30), $"v{Application.version}", _dim);
            DrawUpdateBanner();
        }

        void DrawUpdateBanner()
        {
            var u = UpdateChecker.I;
            if (u == null) return;
            if (u.Status != UpdateChecker.State.Available && u.Status != UpdateChecker.State.Downloading &&
                u.Status != UpdateChecker.State.Installing && u.Status != UpdateChecker.State.Failed) return;

            var r = new Rect(VW - 640, 40, 600, 210);
            Fill(r, new Color(0, 0, 0, 0.85f));
            Frame(r, Accent);
            GUILayout.BeginArea(new Rect(r.x + 24, r.y + 18, r.width - 48, r.height - 30));
            switch (u.Status)
            {
                case UpdateChecker.State.Available:
                    GUILayout.Label($"Update available: v{u.LatestVersion}", _h2);
                    GUILayout.Label($"You have v{Application.version}. Update now? The game will restart.", _small);
                    GUILayout.Space(12);
                    GUILayout.BeginHorizontal();
                    if (Btn("Yes, update", GUILayout.Width(250))) u.Accept();
                    GUILayout.Space(12);
                    if (Btn("No", GUILayout.Width(150))) u.Dismiss();
                    GUILayout.EndHorizontal();
                    break;
                case UpdateChecker.State.Downloading:
                    GUILayout.Label($"Downloading v{u.LatestVersion}…", _h2);
                    GUILayout.Space(10);
                    var bar = GUILayoutUtility.GetRect(540, 18);
                    Fill(bar, new Color(1, 1, 1, 0.15f));
                    Fill(new Rect(bar.x, bar.y, bar.width * u.Progress, bar.height), Accent);
                    GUILayout.Label(Mathf.RoundToInt(u.Progress * 100) + "%", _small);
                    break;
                case UpdateChecker.State.Installing:
                    GUILayout.Label("Installing… the game will restart.", _h2);
                    break;
                case UpdateChecker.State.Failed:
                    GUILayout.Label("Update failed", _h2);
                    GUILayout.Label(u.Error, _small);
                    GUILayout.BeginHorizontal();
                    if (Btn("Retry", GUILayout.Width(160))) u.Accept();
                    if (Btn("Open download page", GUILayout.Width(300))) Application.OpenURL(BuildInfo.ReleasesPage);
                    GUILayout.EndHorizontal();
                    break;
            }
            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- browse

        async void RefreshList()
        {
            if (_loadingList) return;
            _loadingList = true;
            _status = "";
            _listings = await Bootstrap.Lobby.ListAsync();
            _loadingList = false;
            if (!string.IsNullOrEmpty(Bootstrap.Lobby.LastError)) _status = Bootstrap.Lobby.LastError;
        }

        void DrawBrowse()
        {
            var r = new Rect((VW - 1100) / 2, 80, 1100, VH - 160);
            GUI.Box(r, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(r.x + 36, r.y + 28, r.width - 72, r.height - 56));
            GUILayout.Label("Find a Lobby", _h1);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", _small, GUILayout.Width(80));
            _filter = GUILayout.TextField(_filter, 40, _field, GUILayout.Width(420));
            GUILayout.FlexibleSpace();
            if (Btn(_loadingList ? "Refreshing…" : "Refresh", GUILayout.Width(200))) RefreshList();
            GUILayout.EndHorizontal();
            GUILayout.Space(14);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(r.height - 380));
            var shown = _listings.Where(l => string.IsNullOrEmpty(_filter) || (l.Name ?? "").IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (shown.Count == 0)
                GUILayout.Label(_loadingList ? "Looking for lobbies…" : "No lobbies found. Create one, or refresh.", _dim);
            foreach (var l in shown)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(l.Name, _body, GUILayout.Width(520));
                GUILayout.Label($"{l.Players}/{l.MaxPlayers}", _body, GUILayout.Width(90));
                GUILayout.Label(l.HasPassword ? "Private (password)" : "Public", l.HasPassword ? _small : _dim, GUILayout.Width(200));
                bool full = l.Players >= l.MaxPlayers || l.IsLocked;
                GUI.enabled = !full;
                if (Btn(full ? "Full" : "Join", GUILayout.Width(120)))
                {
                    if (l.HasPassword) { _passwordTarget = l; _passwordPrompt = ""; }
                    else Join(() => Bootstrap.Lobby.JoinAsync(l.Id, ""));
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(10);
            GUILayout.Label("Have a join code from a friend?", _small);
            GUILayout.BeginHorizontal();
            _joinCode = GUILayout.TextField(_joinCode, 12, _field, GUILayout.Width(220));
            GUILayout.Label("Password (if any)", _dim, GUILayout.Width(170));
            _joinCodePassword = GUILayout.PasswordField(_joinCodePassword, '•', 40, _field, GUILayout.Width(260));
            if (Btn("Join by code", GUILayout.Width(220)) && _joinCode.Trim().Length > 0)
                Join(() => Bootstrap.Lobby.JoinByCodeAsync(_joinCode, _joinCodePassword));
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, _body);
            if (Btn("Back", GUILayout.Width(200))) { _screen = Screen.Main; _status = ""; }
            GUILayout.EndArea();

            if (_passwordTarget != null) DrawPasswordModal();
        }

        void DrawPasswordModal()
        {
            Fill(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.6f));
            var r = new Rect((VW - 640) / 2, (VH - 300) / 2, 640, 300);
            GUI.Box(r, GUIContent.none, _box);
            Frame(r, Accent);
            GUILayout.BeginArea(new Rect(r.x + 32, r.y + 26, r.width - 64, r.height - 52));
            GUILayout.Label("Password for " + _passwordTarget.Name, _h2);
            GUILayout.Space(10);
            _passwordPrompt = GUILayout.PasswordField(_passwordPrompt, '•', 40, _field);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Btn("Join", GUILayout.Width(200)))
            {
                var target = _passwordTarget; var pw = _passwordPrompt;
                _passwordTarget = null;
                Join(() => Bootstrap.Lobby.JoinAsync(target.Id, pw));
            }
            GUILayout.Space(12);
            if (Btn("Cancel", GUILayout.Width(200))) _passwordTarget = null;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        async void Join(System.Func<Task<bool>> join)
        {
            _screen = Screen.Connecting;
            _status = "Joining…";
            bool ok = await join();
            if (!ok)
            {
                _screen = Screen.Browse;
                _status = Bootstrap.Lobby.LastError;
                return;
            }
            _status = "Connecting to host…";
            // GameSession calls OnJoinedSession once the host welcomes us.
            float timeout = Time.time + 20f;
            while (_screen == Screen.Connecting && Time.time < timeout) await Task.Yield();
            if (_screen == Screen.Connecting)
            {
                await Bootstrap.Lobby.LeaveAsync();
                _screen = Screen.Browse;
                _status = "Timed out connecting to the host.";
            }
        }

        // ---------------------------------------------------------------- create

        void DrawCreate()
        {
            var r = new Rect((VW - 820) / 2, 120, 820, 640);
            GUI.Box(r, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(r.x + 36, r.y + 28, r.width - 72, r.height - 56));
            GUILayout.Label("Create a Lobby", _h1);
            GUILayout.Space(16);
            GUILayout.Label("Lobby name (friends search for this)", _small);
            _lobbyName = GUILayout.TextField(_lobbyName, 32, _field);
            GUILayout.Space(18);
            GUILayout.Label("Who can join?", _small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Public", _private ? _btn : _btnSel, GUILayout.Width(220))) _private = false;
            GUILayout.Space(10);
            if (GUILayout.Button("Private (password)", _private ? _btnSel : _btn, GUILayout.Width(300))) _private = true;
            GUILayout.EndHorizontal();
            if (_private)
            {
                GUILayout.Space(12);
                GUILayout.Label("Password", _small);
                _password = GUILayout.PasswordField(_password, '•', 40, _field);
            }
            GUILayout.Space(14);
            GUILayout.Label("Up to 8 players. Game mode and rules are chosen in the lobby.", _dim);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, _body);
            GUILayout.BeginHorizontal();
            bool valid = !_private || _password.Length > 0;
            GUI.enabled = valid;
            if (Btn("Create", GUILayout.Width(220))) Host();
            GUI.enabled = true;
            GUILayout.Space(12);
            if (Btn("Back", GUILayout.Width(200))) { _screen = Screen.Main; _status = ""; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        async void Host()
        {
            _screen = Screen.Connecting;
            _status = "Creating lobby…";
            bool ok = await Bootstrap.Lobby.HostAsync(Settings.Sanitize(_lobbyName, 32), _private ? _password : "");
            if (!ok)
            {
                _screen = Screen.Create;
                _status = Bootstrap.Lobby.LastError;
            }
            // On success GameSession receives its own Welcome and moves us to the lobby screen.
        }

        void DrawConnecting()
        {
            var r = new Rect((VW - 700) / 2, (VH - 200) / 2, 700, 200);
            GUI.Box(r, GUIContent.none, _box);
            int dots = (int)(Time.time * 3) % 4;
            GUI.Label(new Rect(r.x, r.y + 50, r.width, 50), (_status.TrimEnd('…', '.')) + new string('.', dots), _center);
        }

        // ---------------------------------------------------------------- lobby room

        void DrawLobby()
        {
            var gs = GameSession.I;
            var lobby = Bootstrap.Lobby;
            bool host = Net.IsHost;
            var rules = gs.Rules;

            var r = new Rect((VW - 1400) / 2, 70, 1400, VH - 140);
            GUI.Box(r, GUIContent.none, _box);

            // Left: players
            GUILayout.BeginArea(new Rect(r.x + 36, r.y + 28, 640, r.height - 56));
            GUILayout.Label(lobby.CurrentLobbyName, _h1);
            GUILayout.Label((lobby.CurrentHasPassword ? "Private (password)" : "Public") +
                            (string.IsNullOrEmpty(lobby.CurrentJoinCode) ? "" : "   ·   Join code: " + lobby.CurrentJoinCode), _dim);
            GUILayout.Space(16);
            GUILayout.Label($"Players ({gs.Players.Count}/{Tuning.MaxPlayers})", _h2);

            if (rules.Mode == GameMode.CaptureTheBox)
            {
                for (int t = 0; t < 2; t++)
                {
                    var old = GUI.color; GUI.color = Look.TeamColors[t];
                    GUILayout.Label(Look.TeamNames[t] + " team", _body);
                    GUI.color = old;
                    foreach (var p in gs.Players.Values.Where(p => p.Team == t).OrderBy(p => p.Slot)) PlayerRow(p, host, true);
                    GUILayout.Space(8);
                }
                if (host && Btn("Shuffle teams", GUILayout.Width(260))) gs.HostShuffleTeams();
            }
            else
            {
                foreach (var p in gs.Players.Values.OrderBy(p => p.Slot)) PlayerRow(p, host, false);
            }
            GUILayout.FlexibleSpace();
            DrawMicMeter(560, true);
            GUILayout.EndArea();

            // Right: rules
            GUILayout.BeginArea(new Rect(r.x + 740, r.y + 28, 620, r.height - 56));
            GUILayout.Label("Match settings", _h2);
            GUILayout.Space(8);
            var edit = Copy(rules);
            bool changed = false;

            changed |= Picker("Game mode", rules.Mode == GameMode.Deathmatch ? "Deathmatch" : "Capture the Box", host,
                d => edit.Mode = edit.Mode == GameMode.Deathmatch ? GameMode.CaptureTheBox : GameMode.Deathmatch);
            if (rules.Mode == GameMode.Deathmatch)
            {
                changed |= Picker("Eliminations to win", rules.KillTarget.ToString(), host,
                    d => edit.KillTarget = MatchSettings.Cycle(MatchSettings.KillOptions, edit.KillTarget, d));
                changed |= Picker("Time limit", MatchSettings.TimeLabel(rules.DmTimeMinutes), host,
                    d => edit.DmTimeMinutes = MatchSettings.Cycle(MatchSettings.TimeOptions, edit.DmTimeMinutes, d));
                GUILayout.Label("Score a point for every elimination. First to the target — or most when time runs out — wins.", _dim);
            }
            else
            {
                changed |= Picker("Captures to win", rules.CaptureTarget.ToString(), host,
                    d => edit.CaptureTarget = MatchSettings.Cycle(MatchSettings.CaptureOptions, edit.CaptureTarget, d));
                changed |= Picker("Time limit", MatchSettings.TimeLabel(rules.CtbTimeMinutes), host,
                    d => edit.CtbTimeMinutes = MatchSettings.Cycle(MatchSettings.TimeOptions, edit.CtbTimeMinutes, d));
                GUILayout.Label("Grab the enemy team's Box (it plays elevator music — everyone can see it and its carrier) and bring it back to your base.", _dim);
            }
            if (changed) gs.HostSetRules(edit);

            GUILayout.FlexibleSpace();
            if (host)
            {
                if (gs.Players.Count < 2) GUILayout.Label("You can start alone to practise, or wait for friends.", _dim);
                if (Btn("Start match", GUILayout.Width(300))) gs.HostStartMatch();
            }
            else GUILayout.Label("Waiting for the host to start…", _body);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (Btn("Options", GUILayout.Width(200))) { _optionsReturn = Screen.Lobby; _screen = Screen.Options; }
            GUILayout.Space(10);
            if (Btn(host ? "Close lobby" : "Leave lobby", GUILayout.Width(260))) Leave();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void PlayerRow(PlayerInfo p, bool host, bool teams)
        {
            var gs = GameSession.I;
            GUILayout.BeginHorizontal();
            string you = p.Slot == gs.LocalSlot ? "  (you)" : "";
            string hostTag = p.ClientId == Unity.Netcode.NetworkManager.ServerClientId ? "  ★ host" : "";
            var rp = gs.Remote(p.Slot);
            bool talking = (rp != null && rp.Voice.Loudness > 0.05f) || (p.Slot == gs.LocalSlot && MicInput.I != null && MicInput.I.Transmitting);
            GUILayout.Label((talking ? "◉ " : "   ") + p.Name + you + hostTag, _body, GUILayout.Width(440));
            if (teams && host && Btn("Swap", GUILayout.Width(110))) gs.HostSetTeam(p.Slot, (byte)(1 - p.Team));
            GUILayout.EndHorizontal();
        }

        static MatchSettings Copy(MatchSettings s) => new MatchSettings
        {
            Mode = s.Mode, KillTarget = s.KillTarget, CaptureTarget = s.CaptureTarget,
            DmTimeMinutes = s.DmTimeMinutes, CtbTimeMinutes = s.CtbTimeMinutes,
        };

        bool Picker(string label, string value, bool editable, System.Action<int> change)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _body, GUILayout.Width(260));
            if (editable && Btn("<", GUILayout.Width(50))) { change(-1); changed = true; }
            GUILayout.Label(value, _h2, GUILayout.Width(220));
            if (editable && Btn(">", GUILayout.Width(50))) { change(1); changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            return changed;
        }

        async void Leave()
        {
            _paused = false;
            if (GameSession.I != null) GameSession.I.ExpectDisconnect = true;
            await Bootstrap.Lobby.LeaveAsync();
            _screen = Screen.Main;
            SonarSystem.I?.ClearAll();
        }

        // ---------------------------------------------------------------- options

        void DrawOptions()
        {
            var r = new Rect((VW - 1100) / 2, 60, 1100, VH - 120);
            GUI.Box(r, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(r.x + 36, r.y + 28, r.width - 72, r.height - 56));
            GUILayout.Label("Options", _h1);
            GUILayout.Space(10);

            GUILayout.Label("Microphone", _h2);
            var mic = MicInput.I;
            var devices = MicInput.Devices;
            if (devices.Length == 0) GUILayout.Label("No microphone found. Plug one in, then press Refresh.", _body);
            _micScroll = GUILayout.BeginScrollView(_micScroll, GUILayout.Height(Mathf.Min(220, 52 * (devices.Length + 1) + 8)));
            string current = Settings.MicDevice;
            if (GUILayout.Button("System default" + (devices.Length > 0 ? "  (" + devices[0] + ")" : ""), string.IsNullOrEmpty(current) ? _btnSel : _btn))
            { Settings.MicDevice = ""; mic?.Restart(); }
            foreach (var d in devices)
                if (GUILayout.Button(d, current == d ? _btnSel : _btn)) { Settings.MicDevice = d; mic?.Restart(); }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label("In use: " + (mic != null && mic.HasMic ? mic.ActiveDevice : "none"), _dim, GUILayout.Width(760));
            if (Btn("Refresh", GUILayout.Width(180))) mic?.Restart();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            Slider("Mic sensitivity", Settings.MicSensitivity, 0f, 1f, v => Settings.MicSensitivity = v, Mathf.RoundToInt(Settings.MicSensitivity * 100) + "%");
            DrawMicMeter(1000, true);
            GUILayout.Label("Talk normally — the bar should pass the first line (a sonar ping). Scream — it should pass the second line (a big blast). Adjust sensitivity until it does.", _dim);

            GUILayout.Space(14);
            GUILayout.Label("Game", _h2);
            Slider("Mouse sensitivity", Settings.MouseSensitivity, 0.1f, 5f, v => Settings.MouseSensitivity = v, Settings.MouseSensitivity.ToString("0.00"));
            Slider("Field of view", Settings.Fov, 70f, 120f, v => Settings.Fov = Mathf.Round(v), Mathf.RoundToInt(Settings.Fov).ToString());
            Slider("Master volume", Settings.MasterVolume, 0f, 1f, v => Settings.MasterVolume = v, Mathf.RoundToInt(Settings.MasterVolume * 100) + "%");
            Slider("Voice chat volume", Settings.VoiceVolume, 0f, 2f, v => Settings.VoiceVolume = v, Mathf.RoundToInt(Settings.VoiceVolume * 100) + "%");
            GUILayout.BeginHorizontal();
            Toggle("Auto bunny-hop (hold Space)", Settings.AutoHop, v => Settings.AutoHop = v);
            GUILayout.Space(20);
            Toggle("Keyboard sonar: hold Q (testing)", Settings.KeyboardSonar, v => Settings.KeyboardSonar = v);
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            if (Btn("Back", GUILayout.Width(200))) _screen = _optionsReturn;
            GUILayout.EndArea();
        }

        void Slider(string label, float value, float min, float max, System.Action<float> set, string display)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _body, GUILayout.Width(280));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(520), GUILayout.Height(30));
            GUILayout.Label(display, _body, GUILayout.Width(120));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(v, value)) set(v);
        }

        void Toggle(string label, bool value, System.Action<bool> set)
        {
            if (GUILayout.Button((value ? "[x] " : "[  ] ") + label, value ? _btnSel : _btn)) set(!value);
        }

        void DrawMicMeter(float width, bool labels)
        {
            var mic = MicInput.I;
            float level = mic != null ? mic.Level : 0f;
            var r = GUILayoutUtility.GetRect(width, 28, GUILayout.Width(width));
            Fill(r, new Color(1, 1, 1, 0.08f));
            Color barColor = level >= Tuning.ScreamThreshold ? new Color(1f, 0.45f, 0.35f) : level >= Tuning.TalkThreshold ? Accent : new Color(1, 1, 1, 0.4f);
            Fill(new Rect(r.x, r.y, r.width * level, r.height), barColor);
            Fill(new Rect(r.x + r.width * Tuning.TalkThreshold - 1, r.y - 4, 3, r.height + 8), Accent);
            Fill(new Rect(r.x + r.width * Tuning.ScreamThreshold - 1, r.y - 4, 3, r.height + 8), new Color(1f, 0.45f, 0.35f));
            if (labels)
            {
                GUI.Label(new Rect(r.x + r.width * Tuning.TalkThreshold + 6, r.y + 28, 200, 26), "talk = ping", _dim);
                GUI.Label(new Rect(r.x + r.width * Tuning.ScreamThreshold + 6, r.y + 28, 220, 26), "scream = blast", _dim);
                GUILayout.Space(28);
            }
        }

        // ---------------------------------------------------------------- in game

        void DrawInGame()
        {
            var gs = GameSession.I;
            if (gs == null) return;
            var local = gs.Local;

            // Crosshair + cooldowns
            float cx = VW / 2, cy = VH / 2;
            var ch = new Color(1, 1, 1, 0.85f);
            Fill(new Rect(cx - 12, cy - 1, 8, 2), ch); Fill(new Rect(cx + 4, cy - 1, 8, 2), ch);
            Fill(new Rect(cx - 1, cy - 12, 2, 8), ch); Fill(new Rect(cx - 1, cy + 4, 2, 8), ch);
            if (local != null && local.Alive)
            {
                Fill(new Rect(cx - 40, cy + 26, 80, 4), new Color(1, 1, 1, 0.15f));
                Fill(new Rect(cx - 40, cy + 26, 80 * local.FireReady01, 4), local.FireReady01 >= 1 ? Accent : new Color(1, 1, 1, 0.5f));
                Fill(new Rect(cx - 40, cy + 34, 80, 3), new Color(1, 1, 1, 0.1f));
                Fill(new Rect(cx - 40, cy + 34, 80 * local.BlastReady01, 3), new Color(1f, 0.75f, 0.4f, 0.8f));
            }

            DrawTopBar(gs);

            // Kill feed
            for (int i = 0; i < gs.Feed.Count; i++)
                GUI.Label(new Rect(VW - 760, 30 + i * 30, 720, 30), gs.Feed[i], _feed);

            // Mic meter bottom-left
            GUILayout.BeginArea(new Rect(40, VH - 110, 420, 90));
            GUILayout.Label("Voice", _dim);
            DrawMicMeter(380, false);
            GUILayout.EndArea();

            GUI.Label(new Rect(VW - 560, VH - 50, 520, 30), "LMB fire  ·  RMB blast-jump  ·  speak = sonar  ·  Tab scores  ·  Esc menu", new GUIStyle(_dim) { alignment = TextAnchor.MiddleRight, fontSize = 16 });

            if (gs.Rules.Mode == GameMode.CaptureTheBox) DrawBoxStatus(gs);

            // Death overlay
            if (local != null && !local.Alive && gs.Phase == Phase.Playing)
            {
                Fill(new Rect(0, VH * 0.36f, VW, 150), new Color(0, 0, 0, 0.6f));
                string by = string.IsNullOrEmpty(local.KilledBy) ? "" : "Blasted by " + local.KilledBy;
                GUI.Label(new Rect(0, VH * 0.36f + 20, VW, 70), by == "" ? "Get ready…" : by, _big);
                GUI.Label(new Rect(0, VH * 0.36f + 95, VW, 40), "Respawning…", _center);
            }

            bool tab = InputWrap.Key(InputWrap.KeyName.Tab);
            if (gs.Phase == Phase.PostMatch) DrawPostMatch(gs);
            else if (tab) DrawScoreboard(gs, VH * 0.2f);

            if (_paused) DrawPause();
        }

        void DrawTopBar(GameSession gs)
        {
            string time = gs.TimeLeft < 0 ? "∞" : $"{(int)gs.TimeLeft / 60}:{(int)gs.TimeLeft % 60:00}";
            var r = new Rect(VW / 2 - 260, 20, 520, 70);
            Fill(r, new Color(0, 0, 0, 0.55f));
            if (gs.Rules.Mode == GameMode.Deathmatch)
            {
                var me = gs.LocalInfo;
                var best = gs.Players.Values.OrderByDescending(p => p.Score).FirstOrDefault();
                GUI.Label(new Rect(r.x, r.y + 4, r.width, 34), time, new GUIStyle(_h2) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
                string s = $"You {me?.Score ?? 0}   ·   Leader {best?.Score ?? 0}   ·   First to {gs.Rules.KillTarget}";
                GUI.Label(new Rect(r.x, r.y + 38, r.width, 28), s, new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                var st = new GUIStyle(_h1) { alignment = TextAnchor.MiddleCenter };
                var old = GUI.color;
                GUI.color = Look.TeamColors[0]; GUI.Label(new Rect(r.x, r.y + 6, 160, 58), gs.TeamScore[0].ToString(), st);
                GUI.color = Look.TeamColors[1]; GUI.Label(new Rect(r.xMax - 160, r.y + 6, 160, 58), gs.TeamScore[1].ToString(), st);
                GUI.color = old;
                GUI.Label(new Rect(r.x, r.y + 4, r.width, 34), time, new GUIStyle(_h2) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
                GUI.Label(new Rect(r.x, r.y + 38, r.width, 28), $"First to {gs.Rules.CaptureTarget}", new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
            }
        }

        void DrawBoxStatus(GameSession gs)
        {
            var me = gs.LocalInfo;
            for (int t = 0; t < 2; t++)
            {
                var b = gs.Boxes[t];
                string s = b.Status switch
                {
                    BoxStatus.Home => "at base",
                    BoxStatus.Carried => "taken by " + gs.NameOf(b.CarrierSlot),
                    _ => "dropped!",
                };
                var old = GUI.color; GUI.color = Look.TeamColors[t];
                GUI.Label(new Rect(VW - 560, VH - 160 + t * 34, 520, 32), $"{Look.TeamNames[t]} box: {s}", new GUIStyle(_body) { alignment = TextAnchor.MiddleRight });
                GUI.color = old;
            }
            if (me != null && gs.Boxes[1 - me.Team].Status == BoxStatus.Carried && gs.Boxes[1 - me.Team].CarrierSlot == me.Slot)
                GUI.Label(new Rect(0, VH * 0.22f, VW, 40), "You have the Box — everyone can see you. Get it home!", _center);
        }

        void DrawScoreboard(GameSession gs, float top)
        {
            var r = new Rect((VW - 900) / 2, top, 900, 90 + gs.Players.Count * 40 + (gs.IsTeamMode ? 80 : 0));
            GUI.Box(r, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(r.x + 30, r.y + 20, r.width - 60, r.height - 30));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Player", _h2, GUILayout.Width(520));
            GUILayout.Label("Elims", _h2, GUILayout.Width(140));
            GUILayout.Label("Deaths", _h2, GUILayout.Width(140));
            GUILayout.EndHorizontal();
            IEnumerable<IGrouping<int, PlayerInfo>> groups = gs.IsTeamMode
                ? gs.Players.Values.GroupBy(p => (int)p.Team).OrderBy(g => g.Key)
                : gs.Players.Values.GroupBy(p => 0);
            foreach (var g in groups)
            {
                if (gs.IsTeamMode)
                {
                    var old = GUI.color; GUI.color = Look.TeamColors[g.Key];
                    GUILayout.Label($"{Look.TeamNames[g.Key]} — {gs.TeamScore[g.Key]} captures", _body);
                    GUI.color = old;
                }
                foreach (var p in g.OrderByDescending(p => p.Score))
                {
                    GUILayout.BeginHorizontal();
                    var style = p.Slot == gs.LocalSlot ? new GUIStyle(_body) { normal = { textColor = Accent } } : _body;
                    GUILayout.Label(p.Name, style, GUILayout.Width(520));
                    GUILayout.Label(p.Score.ToString(), style, GUILayout.Width(140));
                    GUILayout.Label(p.Deaths.ToString(), style, GUILayout.Width(140));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndArea();
        }

        void DrawPostMatch(GameSession gs)
        {
            Fill(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.6f));
            GUI.Label(new Rect(0, VH * 0.08f, VW, 90), gs.EndTitle, _big);
            GUI.Label(new Rect(0, VH * 0.08f + 90, VW, 40), gs.EndSubtitle, _center);
            DrawScoreboard(gs, VH * 0.25f);
            GUI.Label(new Rect(0, VH - 120, VW, 40), $"Back to the lobby in {Mathf.CeilToInt(gs.PostMatchSecondsLeft)}…", _center);
        }

        void DrawPause()
        {
            Fill(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.55f));
            var r = new Rect((VW - 520) / 2, (VH - 420) / 2, 520, 420);
            GUI.Box(r, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(r.x + 36, r.y + 28, r.width - 72, r.height - 56));
            GUILayout.Label("Paused", _h1);
            GUILayout.Label("(the match keeps going)", _dim);
            GUILayout.Space(16);
            if (Btn("Resume")) _paused = false;
            GUILayout.Space(8);
            if (Btn("Options")) { _optionsReturn = Screen.InGame; _screen = Screen.Options; _paused = false; }
            GUILayout.Space(8);
            if (!_quitConfirm)
            {
                if (Btn(Net.IsHost ? "Close lobby (ends match for everyone)" : "Leave match")) _quitConfirm = true;
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (Btn("Yes, leave")) { _quitConfirm = false; Leave(); }
                if (Btn("Cancel")) _quitConfirm = false;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
    }
}
