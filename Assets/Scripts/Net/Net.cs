using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Message bus on top of Netcode for GameObjects' custom messaging.
    /// The host is a player too: anything the host "sends to itself" is delivered locally.
    /// The game never touches Relay/Steam directly — only this class and the lobby service do —
    /// so swapping the transport later only touches the Net folder.
    /// </summary>
    public static class Net
    {
        const string ToHostChannel = "gb.h";
        const string ToClientChannel = "gb.c";

        public static NetworkManager NM => NetworkManager.Singleton;
        public static bool Active => NM != null && NM.IsListening;
        public static bool IsHost => NM != null && NM.IsServer;
        public static ulong LocalId => NM != null ? NM.LocalClientId : 0;

        /// <summary>Host only: (sender, payload).</summary>
        public static event Action<ulong, byte[]> HostReceive;
        /// <summary>Every peer (host included): payload from the host.</summary>
        public static event Action<byte[]> ClientReceive;
        /// <summary>Host only: a client finished connecting.</summary>
        public static event Action<ulong> ClientJoined;
        /// <summary>Host only: a client disconnected.</summary>
        public static event Action<ulong> ClientLeft;
        /// <summary>Local: we connected (as host or client) and messaging is ready.</summary>
        public static event Action Started;
        /// <summary>Local: our connection ended (host left, kicked, network error, or we left).</summary>
        public static event Action Stopped;

        static bool _registered;

        public static void Init(NetworkManager nm)
        {
            nm.OnServerStarted += Register;
            nm.OnClientStarted += Register;
            nm.OnClientConnectedCallback += id =>
            {
                if (nm.IsServer && id != nm.LocalClientId) ClientJoined?.Invoke(id);
                if (!nm.IsServer && id == nm.LocalClientId) Started?.Invoke();
            };
            nm.OnClientDisconnectCallback += id =>
            {
                if (nm.IsServer && id != nm.LocalClientId) ClientLeft?.Invoke(id);
                else if (!nm.IsServer && (id == nm.LocalClientId || id == NetworkManager.ServerClientId)) Stopped?.Invoke();
            };
            nm.OnClientStopped += _ => { _registered = false; Stopped?.Invoke(); };
            nm.OnServerStopped += _ => { _registered = false; };
        }

        static void Register()
        {
            if (_registered || NM == null || NM.CustomMessagingManager == null) return;
            _registered = true;
            NM.CustomMessagingManager.RegisterNamedMessageHandler(ToHostChannel, (sender, reader) =>
            {
                var data = ReadPayload(reader);
                if (data != null && IsHost) HostReceive?.Invoke(sender, data);
            });
            NM.CustomMessagingManager.RegisterNamedMessageHandler(ToClientChannel, (sender, reader) =>
            {
                var data = ReadPayload(reader);
                if (data != null) ClientReceive?.Invoke(data);
            });
            if (NM.IsServer) Started?.Invoke();
        }

        static byte[] ReadPayload(FastBufferReader reader)
        {
            try
            {
                reader.ReadValueSafe(out int len);
                if (len <= 0 || len > 1 << 20) return null;
                var data = new byte[len];
                reader.ReadBytesSafe(ref data, len);
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] bad payload: " + e.Message);
                return null;
            }
        }

        static void SendRaw(string channel, ulong to, byte[] data, bool reliable)
        {
            if (!Active || NM.CustomMessagingManager == null) return;
            using var w = new FastBufferWriter(data.Length + 8, Allocator.Temp, data.Length + 64);
            w.WriteValueSafe(data.Length);
            w.WriteBytesSafe(data, data.Length);
            NetworkDelivery d = !reliable ? NetworkDelivery.UnreliableSequenced
                : data.Length > 900 ? NetworkDelivery.ReliableFragmentedSequenced
                : NetworkDelivery.ReliableSequenced;
            try { NM.CustomMessagingManager.SendNamedMessage(channel, to, w, d); }
            catch (Exception e) { Debug.LogWarning("[Net] send failed: " + e.Message); }
        }

        /// <summary>Client (or host) to host.</summary>
        public static void ToHost(byte[] data, bool reliable)
        {
            if (!Active) return;
            if (IsHost) HostReceive?.Invoke(LocalId, data);
            else SendRaw(ToHostChannel, NetworkManager.ServerClientId, data, reliable);
        }

        /// <summary>Host to a single peer.</summary>
        public static void ToClient(ulong id, byte[] data, bool reliable)
        {
            if (!IsHost) return;
            if (id == LocalId) ClientReceive?.Invoke(data);
            else SendRaw(ToClientChannel, id, data, reliable);
        }

        /// <summary>Host to everyone (including itself), optionally skipping one peer.</summary>
        public static void ToAll(byte[] data, bool reliable, ulong except = ulong.MaxValue)
        {
            if (!IsHost) return;
            bool selfToo = false;
            foreach (var id in NM.ConnectedClientsIds)
            {
                if (id == except) continue;
                if (id == LocalId) { selfToo = true; continue; }
                SendRaw(ToClientChannel, id, data, reliable);
            }
            if (selfToo || (LocalId != except && !Contains(NM.ConnectedClientsIds, LocalId)))
                ClientReceive?.Invoke(data);
        }

        static bool Contains(System.Collections.Generic.IReadOnlyList<ulong> ids, ulong v)
        {
            for (int i = 0; i < ids.Count; i++) if (ids[i] == v) return true;
            return false;
        }

        public static void DisconnectClient(ulong id)
        {
            if (IsHost && id != LocalId) NM.DisconnectClient(id);
        }
    }
}
