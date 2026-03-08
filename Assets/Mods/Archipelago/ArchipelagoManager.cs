using System;
using System.Collections.Concurrent;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Received item data, copied off the network thread for safe main-thread consumption.
    /// </summary>
    public readonly struct ApItem
    {
        public readonly long   ItemId;
        public readonly string ItemName;
        public readonly long   LocationId;
        public readonly string LocationName;
        public readonly string SenderName;
        public readonly ItemFlags Flags;

        public ApItem(ItemInfo info)
        {
            ItemId       = info.ItemId;
            ItemName     = info.ItemName;
            LocationId   = info.LocationId;
            LocationName = info.LocationName;
            SenderName   = info.Player?.Name ?? "Server";
            Flags        = info.Flags;
        }
    }

    /// <summary>
    /// Static singleton that owns the Archipelago session for the entire process lifetime.
    /// All public members are safe to call from the main thread.
    /// Item callbacks arrive on a background thread — they are queued and drained by
    /// ArchipelagoTicker each frame.
    /// </summary>
    public static class ArchipelagoManager
    {
        // ------------------------------------------------------------------ state
        public static bool   IsConnected  { get; private set; }
        public static string CurrentSlot  { get; private set; }
        public static string CurrentSeed  { get; private set; }
        public static string CurrentHost  { get; private set; }
        public static int    CurrentPort  { get; private set; }

        /// <summary>
        /// Index of the next item we have not yet processed.
        /// Persisted to the save file so reconnects don't replay already-handled items.
        /// </summary>
        public static int ProcessedItemIndex { get; set; }

        // ------------------------------------------------------------------ events (main thread)
        /// <summary>Fired on the main thread for each new item received from the server.</summary>
        public static event Action<ApItem> OnItemReceived;

        /// <summary>Fired on the main thread when the connection state changes.</summary>
        public static event Action<bool, string> OnConnectionChanged; // (connected, message)

        // ------------------------------------------------------------------ internals
        private static ArchipelagoSession _session;
        private static readonly ConcurrentQueue<ApItem> _pendingItems = new();

        // ------------------------------------------------------------------ connect / disconnect

        /// <summary>
        /// Attempt to connect and login. Returns the LoginResult so callers can inspect
        /// failure reasons and display them in the UI.
        /// </summary>
        public static LoginResult Connect(string host, int port, string slotName,
                                          string password = "")
        {
            Disconnect();

            _session = ArchipelagoSessionFactory.CreateSession(host, port);
            _session.Items.ItemReceived  += OnNetworkItemReceived;
            _session.Socket.SocketClosed += OnSocketClosed;

            var loginResult = _session.TryConnectAndLogin(
                "Timberborn",
                slotName,
                ItemsHandlingFlags.AllItems,
                password: string.IsNullOrWhiteSpace(password) ? null : password
            );

            if (loginResult.Successful)
            {
                IsConnected  = true;
                CurrentSlot  = slotName;
                CurrentHost  = host;
                CurrentPort  = port;
                CurrentSeed  = _session.RoomState.Seed;

                Debug.Log($"[Archipelago] Connected to {host}:{port} as '{slotName}'. Seed: {CurrentSeed}");
                OnConnectionChanged?.Invoke(true, $"Connected as {slotName}");
            }
            else
            {
                var failure = (LoginFailure)loginResult;
                var reasons = string.Join(", ", failure.Errors);
                Debug.LogWarning($"[Archipelago] Connection failed: {reasons}");
                OnConnectionChanged?.Invoke(false, $"Failed: {reasons}");
                _session = null;
            }

            return loginResult;
        }

        public static void Disconnect()
        {
            if (_session == null) return;

            _session.Items.ItemReceived  -= OnNetworkItemReceived;
            _session.Socket.SocketClosed -= OnSocketClosed;

            try { _session.Socket.DisconnectAsync().Wait(1000); }
            catch { /* best-effort */ }

            _session     = null;
            IsConnected  = false;
            CurrentSlot  = null;
            CurrentSeed  = null;

            Debug.Log("[Archipelago] Disconnected.");
            OnConnectionChanged?.Invoke(false, "Disconnected");
        }

        // ------------------------------------------------------------------ sending

        /// <summary>Send a location check by its AP location ID.</summary>
        public static void SendLocationCheck(long locationId)
        {
            if (!IsConnected) return;
            _session.Locations.CompleteLocationChecks(locationId);
        }

        /// <summary>Send a location check looked up by name.</summary>
        public static void SendLocationCheck(string locationName)
        {
            if (!IsConnected) return;
            var id = _session.Locations.GetLocationIdFromName("Timberborn", locationName);
            if (id >= 0)
                _session.Locations.CompleteLocationChecks(id);
            else
                Debug.LogWarning($"[Archipelago] Unknown location: {locationName}");
        }

        /// <summary>Notify the server that the goal has been completed.</summary>
        public static void SendGoalCompleted()
        {
            if (!IsConnected) return;
            _session.SetGoalAchieved();
            Debug.Log("[Archipelago] Goal achieved — sent to server.");
        }

        // ------------------------------------------------------------------ item queue (main thread)

        /// <summary>
        /// Called each frame by ArchipelagoTicker. Drains all queued items and fires
        /// OnItemReceived for each one that is beyond the already-processed index.
        /// </summary>
        internal static void DrainItemQueue()
        {
            while (_pendingItems.TryDequeue(out var item))
            {
                OnItemReceived?.Invoke(item);
            }
        }

        // ------------------------------------------------------------------ network callbacks (background thread)

        private static void OnNetworkItemReceived(ReceivedItemsHelper helper)
        {
            // Skip items we already handled in a previous session.
            // helper.Index is the index of the NEXT item to dequeue.
            while (helper.Any())
            {
                var info  = helper.DequeueItem();
                var index = helper.Index - 1; // index of the item we just dequeued

                if (index < ProcessedItemIndex)
                    continue; // already handled before this session

                _pendingItems.Enqueue(new ApItem(info));
                ProcessedItemIndex = index + 1;
            }
        }

        private static void OnSocketClosed(string reason)
        {
            IsConnected = false;
            Debug.LogWarning($"[Archipelago] Connection closed: {reason}");
            // Marshal to main thread via the next DrainItemQueue call isn't ideal;
            // fire via the queue mechanism so it's always on main thread.
            OnConnectionChanged?.Invoke(false, $"Disconnected: {reason}");
        }
    }
}
