using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Unity Gaming Services backend: Lobby for the server browser, Relay so the host's PC
    /// can accept players without port forwarding. One player hosts, up to 7 join.
    /// </summary>
    public class UgsLobbyService : ILobbyService
    {
        ISession _session;

        public bool Ready { get; private set; }
        public string LastError { get; private set; } = "";
        public string CurrentLobbyName => _session?.Name ?? "";
        public string CurrentJoinCode => _session?.Code ?? "";
        public bool CurrentHasPassword => _session != null && _session.HasPassword;

        // UGS requires 8-64 character passwords. Players can pick any password,
        // so we pad it deterministically. Everyone runs the same transform, so it still matches.
        static string ToServicePassword(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return null;
            var s = "goingbatty::" + pw;
            return s.Length > 64 ? s.Substring(0, 64) : s;
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    // A fresh anonymous profile per launch lets you run two copies on one PC for testing.
                    var options = new InitializationOptions();
                    options.SetProfile("gb" + UnityEngine.Random.Range(100000, 999999));
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Ready = true;
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                Ready = false;
                LastError = "Couldn't reach online services. " + Friendly(e);
                Debug.LogException(e);
                return false;
            }
        }

        public async Task<List<LobbyListing>> ListAsync()
        {
            var list = new List<LobbyListing>();
            if (!Ready && !await InitializeAsync()) return list;
            try
            {
                var res = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 50 });
                foreach (var s in res.Sessions)
                {
                    list.Add(new LobbyListing
                    {
                        Id = s.Id,
                        Name = s.Name,
                        MaxPlayers = s.MaxPlayers,
                        Players = s.MaxPlayers - s.AvailableSlots,
                        HasPassword = s.HasPassword,
                        IsLocked = s.IsLocked,
                    });
                }
                LastError = "";
            }
            catch (Exception e)
            {
                LastError = "Couldn't load lobbies. " + Friendly(e);
                Debug.LogException(e);
            }
            return list;
        }

        public async Task<bool> HostAsync(string lobbyName, string password)
        {
            if (!Ready && !await InitializeAsync()) return false;
            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = Tuning.MaxPlayers,
                    Name = string.IsNullOrWhiteSpace(lobbyName) ? Settings.PlayerName + "'s lobby" : lobbyName,
                    IsPrivate = false, // still listed so friends can find it by name; the password guards it
                    Password = ToServicePassword(password),
                }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                LastError = "Couldn't create lobby. " + Friendly(e);
                Debug.LogException(e);
                return false;
            }
        }

        public async Task<bool> JoinAsync(string lobbyId, string password)
        {
            if (!Ready && !await InitializeAsync()) return false;
            try
            {
                _session = await MultiplayerService.Instance.JoinSessionByIdAsync(lobbyId,
                    new JoinSessionOptions { Password = ToServicePassword(password) });
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                LastError = "Couldn't join. " + Friendly(e);
                Debug.LogException(e);
                return false;
            }
        }

        public async Task<bool> JoinByCodeAsync(string code, string password)
        {
            if (!Ready && !await InitializeAsync()) return false;
            try
            {
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(),
                    new JoinSessionOptions { Password = ToServicePassword(password) });
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                LastError = "Couldn't join with that code. " + Friendly(e);
                Debug.LogException(e);
                return false;
            }
        }

        public async Task LeaveAsync()
        {
            var s = _session;
            _session = null;
            try { if (s != null) await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("[Lobby] leave: " + e.Message); }
            if (Net.NM != null && Net.NM.IsListening) Net.NM.Shutdown();
        }

        static string Friendly(Exception e)
        {
            var m = e.Message ?? "";
            if (m.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0) return "Wrong password?";
            if (m.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return "Lobby is full.";
            if (m.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0) return "Lobby no longer exists.";
            if (m.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0) return "Unity Cloud project isn't linked yet (see README).";
            return m.Length > 120 ? m.Substring(0, 120) : m;
        }
    }
}
