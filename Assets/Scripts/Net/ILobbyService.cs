using System.Collections.Generic;
using System.Threading.Tasks;

namespace GoingBatty
{
    public class LobbyListing
    {
        public string Id;
        public string Name;
        public int Players;
        public int MaxPlayers;
        public bool HasPassword;
        public bool IsLocked;
    }

    /// <summary>
    /// Everything the menus need from an online backend. Today: Unity Relay + Lobby
    /// (UgsLobbyService). To move to Steam later, write SteamLobbyService implementing this
    /// interface, swap the transport on the NetworkManager, and change one line in Bootstrap.
    /// </summary>
    public interface ILobbyService
    {
        bool Ready { get; }
        string LastError { get; }
        string CurrentLobbyName { get; }
        /// <summary>Short code friends can type to join directly (may be empty).</summary>
        string CurrentJoinCode { get; }
        bool CurrentHasPassword { get; }

        Task<bool> InitializeAsync();
        Task<List<LobbyListing>> ListAsync();
        /// <summary>Creates a lobby and starts this machine as the host.</summary>
        Task<bool> HostAsync(string lobbyName, string passwordOrEmpty);
        Task<bool> JoinAsync(string lobbyId, string passwordOrEmpty);
        Task<bool> JoinByCodeAsync(string code, string passwordOrEmpty);
        Task LeaveAsync();
    }
}
