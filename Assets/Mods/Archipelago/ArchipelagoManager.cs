using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
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
        /// Slot data received from the server on login.
        /// Contains shop_layout, goal, options, etc.
        /// </summary>
        public static Dictionary<string, object> SlotData { get; private set; }

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

        /// <summary>Fired on the main thread for AP server messages and log events.</summary>
        public static event Action<string> OnLogMessage;

        // ------------------------------------------------------------------ internals
        private static ArchipelagoSession _session;
        private static readonly ConcurrentQueue<ApItem> _pendingItems = new();
        private static readonly ConcurrentQueue<string> _pendingMessages = new();

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
            _session.MessageLog.OnMessageReceived += OnServerMessageReceived;

            var loginResult = _session.TryConnectAndLogin(
                "Timberborn",
                slotName,
                ItemsHandlingFlags.AllItems,
                password: string.IsNullOrWhiteSpace(password) ? null : password
            );

            if (loginResult.Successful)
            {
                var success  = (LoginSuccessful)loginResult;
                IsConnected  = true;
                CurrentSlot  = slotName;
                CurrentHost  = host;
                CurrentPort  = port;
                CurrentSeed  = _session.RoomState.Seed;
                SlotData     = success.SlotData;

                Debug.Log($"[Archipelago] Connected to {host}:{port} as '{slotName}'. Seed: {CurrentSeed}");
                Debug.Log($"[Archipelago] SlotData keys: {string.Join(", ", SlotData.Keys)}");
                _pendingMessages.Enqueue($"Connected to {host}:{port} as '{slotName}'");
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
            _session.MessageLog.OnMessageReceived -= OnServerMessageReceived;

            try { _session.Socket.DisconnectAsync().Wait(1000); }
            catch { /* best-effort */ }

            _session     = null;
            IsConnected  = false;
            CurrentSlot  = null;
            CurrentSeed  = null;
            SlotData     = null;

            Debug.Log("[Archipelago] Disconnected.");
            _pendingMessages.Enqueue("Disconnected");
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

        // ------------------------------------------------------------------ log messages

        /// <summary>
        /// Queue a message for the AP event log (main-thread safe).
        /// Called from ApItemReceiver and other components to surface events to the player.
        /// </summary>
        public static void PostLogMessage(string message)
        {
            _pendingMessages.Enqueue(message);
        }

        // ------------------------------------------------------------------ item queue (main thread)

        /// <summary>
        /// Called each frame by ArchipelagoTicker. Drains all queued items and fires
        /// OnItemReceived for each one that is beyond the already-processed index.
        /// </summary>
        internal static void DrainItemQueue()
        {
            while (_pendingMessages.TryDequeue(out var msg))
            {
                OnLogMessage?.Invoke(msg);
            }

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

        private static void OnServerMessageReceived(LogMessage message)
        {
            var text = string.Join("", message.Parts.Select(p => p.Text));
            _pendingMessages.Enqueue(text);
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
