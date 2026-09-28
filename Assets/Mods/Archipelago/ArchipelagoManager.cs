using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
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
        public readonly int    SenderSlot;
        public readonly ItemFlags Flags;
        public readonly int    ItemIndex;

        /// <summary>
        /// True when this item is a replay of the slot's history on a new colony (it was
        /// in the server's connect-time item batch and the save had not applied that
        /// history yet). Fixed on the network thread when the item arrives; see
        /// ItemReplayTracker. Consumers skip non-idempotent effects (traps, Skips).
        /// </summary>
        public readonly bool   IsReplay;

        public ApItem(ItemInfo info, int itemIndex, bool isReplay)
        {
            ItemId       = info.ItemId;
            ItemName     = info.ItemName;
            LocationId   = info.LocationId;
            LocationName = info.LocationName;
            SenderName   = info.Player?.Name ?? "Server";
            SenderSlot   = info.Player?.Slot ?? 0;
            Flags        = info.Flags;
            ItemIndex    = itemIndex;
            IsReplay     = isReplay;
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
        /// <summary>Set to true when connection is blocked (e.g. faction mismatch). Prevents reconnection.</summary>
        public static bool   ConnectionBlocked { get; set; }
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
        public static event Action<ApLogEntry> OnLogMessage;

        /// <summary>
        /// True while the loaded save has not applied its slot's item history yet (new
        /// colony). The next successful connect replays that history (traps and Skips
        /// skipped) and clears this. Set by ArchipelagoSaveData.Load.
        /// </summary>
        public static bool ReplayHistoryOnNextConnect { get; set; }

        private static readonly ItemReplayTracker _replayTracker = new();

        // ------------------------------------------------------------------ internals
        private static ArchipelagoSession _session;
        private static readonly ConcurrentQueue<ApItem> _pendingItems = new();
        private static readonly ConcurrentQueue<ApLogEntry> _pendingMessages = new();

        // Retry queue for failed location checks — drained each frame alongside items
        private static readonly Queue<long> _pendingLocationChecks = new();
        private static bool _goalPending;

        // ------------------------------------------------------------------ connect / disconnect

        /// <summary>
        /// Attempt to connect and login. Returns the LoginResult so callers can inspect
        /// failure reasons and display them in the UI.
        /// </summary>
        public static LoginResult Connect(string host, int port, string slotName,
                                          string password = "")
        {
            if (ConnectionBlocked)
            {
                Debug.LogWarning("[Archipelago] Connection blocked — cannot reconnect.");
                OnConnectionChanged?.Invoke(false, "Connection blocked. Start a new game with the correct faction.");
                return null;
            }

            Disconnect();

            // localhost goes straight to 127.0.0.1 over ws:// (slow ::1 and wss attempts timed out)
            var endpoint = ApHostAddress.ForConnect(host);
            if (endpoint != host)
                Debug.Log($"[Archipelago] Connecting to '{endpoint}' for host '{host}'.");
            _session = ArchipelagoSessionFactory.CreateSession(endpoint, port);
            _session.Items.ItemReceived  += OnNetworkItemReceived;
            _session.Socket.SocketClosed += OnSocketClosed;
            _session.MessageLog.OnMessageReceived += OnServerMessageReceived;
            // Subscribed after CreateSession, so it runs after ReceivedItemsHelper has
            // handled the same packet. The history batch is handled on the socket thread
            // while TryConnectAndLogin returns, so the flag must be set before login.
            _session.Socket.PacketReceived += OnSocketPacketReceived;
            _replayTracker.BeginSession(ReplayHistoryOnNextConnect);

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

                // The history batch belongs to this connect only; later connects of the
                // same save (reconnects) apply everything past ProcessedItemIndex as new.
                bool replaysHistory = _replayTracker.ReplaysHistory;
                ReplayHistoryOnNextConnect = false;

                Debug.Log($"[Archipelago] Connected to {host}:{port} as '{slotName}'. Seed: {CurrentSeed}, ProcessedItemIndex: {ProcessedItemIndex}, ReplayHistory: {replaysHistory}");
                Debug.Log($"[Archipelago] SlotData keys: {string.Join(", ", SlotData.Keys)}");
                _pendingMessages.Enqueue(ApLogEntry.Plain($"Connected to {host}:{port} as '{slotName}'"));
                OnConnectionChanged?.Invoke(true, $"Connected as {slotName}");
            }
            else
            {
                var failure = (LoginFailure)loginResult;
                var reasons = string.Join(", ", failure.Errors);
                Debug.LogWarning($"[Archipelago] Connection failed: {reasons}");
                OnConnectionChanged?.Invoke(false, $"Failed: {reasons}");
                _replayTracker.EndSession();
                _session.Socket.PacketReceived -= OnSocketPacketReceived;
                _session = null;
            }

            return loginResult;
        }

        public static void Disconnect()
        {
            DisconnectWithReason("Disconnected");
        }

        /// <summary>Disconnect with a custom reason shown in the shop panel status.</summary>
        public static void DisconnectWithReason(string reason)
        {
            if (_session == null) return;

            _session.Items.ItemReceived  -= OnNetworkItemReceived;
            _session.Socket.SocketClosed -= OnSocketClosed;
            _session.MessageLog.OnMessageReceived -= OnServerMessageReceived;
            _session.Socket.PacketReceived -= OnSocketPacketReceived;

            try { _session.Socket.DisconnectAsync().Wait(1000); }
            catch { /* best-effort */ }

            _session     = null;
            IsConnected  = false;
            CurrentSlot  = null;
            CurrentSeed  = null;
            SlotData     = null;
            _replayTracker.EndSession();

            // Drain queues tied to the dying session so a Disconnect→Reconnect
            // cycle doesn't replay items/messages from the old socket. The new
            // session will redeliver everything from scratch via the AP server's
            // ItemsHandlingFlags.AllItems resync.
            while (_pendingItems.TryDequeue(out _)) { }
            while (_pendingMessages.TryDequeue(out _)) { }
            _pendingLocationChecks.Clear();
            _goalPending = false;

            Debug.Log($"[Archipelago] {reason}");
            _pendingMessages.Enqueue(ApLogEntry.Plain(reason));
            OnConnectionChanged?.Invoke(false, reason);
        }

        /// <summary>
        /// Clears per-save static state that DisconnectWithReason intentionally
        /// preserves (ProcessedItemIndex is per-(save,slot,seed); ConnectionBlocked
        /// is per-Timberborn-process). Called by ArchipelagoSaveData.Unload on save
        /// switch so the next save starts from a clean slate. Connection-level
        /// state (queues, _session, IsConnected) is already handled by Disconnect.
        /// </summary>
        public static void ResetSessionState()
        {
            ProcessedItemIndex = 0;
            ReplayHistoryOnNextConnect = false;
            ConnectionBlocked = false;
        }

        // ------------------------------------------------------------------ sending

        /// <summary>Send a location check by its AP location ID. Queues for retry on failure.</summary>
        public static void SendLocationCheck(long locationId)
        {
            if (!IsConnected)
            {
                _pendingLocationChecks.Enqueue(locationId);
                return;
            }
            try
            {
                _session.Locations.CompleteLocationChecks(locationId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Archipelago] Failed to send location check {locationId}, queued for retry: {ex.Message}");
                _pendingLocationChecks.Enqueue(locationId);
            }
        }

        /// <summary>Send a location check looked up by name. Resolves to ID and queues for retry on failure.</summary>
        public static void SendLocationCheck(string locationName)
        {
            if (!IsConnected) return;
            try
            {
                var id = _session.Locations.GetLocationIdFromName("Timberborn", locationName);
                if (id >= 0)
                    SendLocationCheck(id);
                else
                    Debug.LogWarning($"[Archipelago] Unknown location: {locationName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Archipelago] Failed to resolve location '{locationName}': {ex.Message}");
            }
        }

        /// <summary>Notify the server that the goal has been completed. Retries on next tick if it fails.</summary>
        public static void SendGoalCompleted()
        {
            if (!IsConnected)
            {
                _goalPending = true;
                return;
            }
            try
            {
                _session.SetGoalAchieved();
                _goalPending = false;
                Debug.Log("[Archipelago] Goal achieved — sent to server.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Archipelago] Failed to send goal completion, will retry: {ex.Message}");
                _goalPending = true;
            }
        }

        /// <summary>
        /// Broadcasts location hints to the server for the given location IDs.
        /// Uses HintCreationPolicy.CreateAndAnnounce so all players in the multiworld
        /// see what items are at those locations. Fire-and-forget; errors are logged.
        /// Called when a Scout item is received to reveal a shop path's contents.
        /// </summary>
        public static void BroadcastLocationHints(long[] locationIds)
        {
            if (!IsConnected || _session == null || locationIds.Length == 0) return;
            try
            {
                // ScoutLocationsAsync is async; we fire-and-forget from the main thread.
                // The continuation only logs, so it's safe to discard the Task.
                var task = _session.Locations.ScoutLocationsAsync(
                    Archipelago.MultiClient.Net.Enums.HintCreationPolicy.CreateAndAnnounce,
                    locationIds);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Debug.LogWarning($"[Archipelago] BroadcastLocationHints failed: {t.Exception?.InnerException?.Message}");
                    else
                        Debug.Log($"[Archipelago] Broadcasted hints for {locationIds.Length} location(s)");
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] BroadcastLocationHints threw: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ server state queries

        /// <summary>Returns all location IDs the server considers checked for this slot.</summary>
        public static IReadOnlyCollection<long> GetAllCheckedLocations()
        {
            if (_session == null) return Array.Empty<long>();
            return _session.Locations.AllLocationsChecked;
        }

        /// <summary>Returns true if the server already considers the goal completed for this slot.</summary>
        public static bool IsGoalCompleted()
        {
            if (_session == null) return false;
            try
            {
                // The AP SDK exposes completion status through RoomState but the
                // exact API varies by version. Try common approaches via reflection.
                var roomState = _session.RoomState;
                var roomType = roomState.GetType();

                // Try GetSlotStatus(int slot) → ArchipelagoClientState
                var getStatus = roomType.GetMethod("GetSlotStatus");
                if (getStatus != null)
                {
                    var status = getStatus.Invoke(roomState, new object[] { _session.ConnectionInfo.Slot });
                    return Convert.ToInt32(status) >= 30; // ClientGoal = 30
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ log messages

        /// <summary>
        /// Queue a message for the AP event log (main-thread safe).
        /// Called from ApItemReceiver and other components to surface events to the player.
        /// </summary>
        public static void PostLogMessage(string message)
        {
            _pendingMessages.Enqueue(ApLogEntry.Plain(message));
        }

        /// <summary>Queue a colored entry for the AP event log (main-thread safe).</summary>
        public static void PostLogEntry(ApLogEntry entry)
        {
            if (entry != null) _pendingMessages.Enqueue(entry);
        }

        /// <summary>
        /// Log a received item as "Received {item} from {sender}{suffix}", with the item
        /// colored by its flags and the sender colored as own or other player.
        /// </summary>
        public static void PostReceivedItem(ApItem item, string itemText, string suffix = "")
        {
            var own = _session?.ConnectionInfo?.Slot ?? -1;
            PostLogEntry(new ApLogEntryBuilder()
                .Text("Received ")
                .Item(itemText, (int)item.Flags)
                .Text(" from ")
                .Player(item.SenderName, item.SenderSlot == own)
                .Text(suffix)
                .Build(true));
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
                try
                {
                    OnItemReceived?.Invoke(item);
                    // Advance the watermark only after the item has actually been
                    // dispatched. If a handler throws, the item will be re-delivered
                    // on the next reconnect (server resends full history with
                    // ItemsHandlingFlags.AllItems) and re-attempted.
                    if (item.ItemIndex >= ProcessedItemIndex)
                        ProcessedItemIndex = item.ItemIndex + 1;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Archipelago] Item dispatch threw for index {item.ItemIndex} ({item.ItemName}): {ex}");
                    // Don't advance watermark — let reconnect retry.
                }
            }

            // Retry any pending location checks that failed earlier
            if (IsConnected && _pendingLocationChecks.Count > 0)
            {
                int retryCount = _pendingLocationChecks.Count;
                for (int i = 0; i < retryCount; i++)
                {
                    var locId = _pendingLocationChecks.Dequeue();
                    try
                    {
                        _session.Locations.CompleteLocationChecks(locId);
                        Debug.Log($"[Archipelago] Retry succeeded for location {locId}");
                    }
                    catch
                    {
                        // Still failing — re-queue and stop retrying this frame
                        _pendingLocationChecks.Enqueue(locId);
                        break;
                    }
                }
            }

            // Retry pending goal completion
            if (IsConnected && _goalPending)
            {
                SendGoalCompleted();
            }
        }

        // ------------------------------------------------------------------ network callbacks (background thread)

        private static void OnNetworkItemReceived(ReceivedItemsHelper helper)
        {
            // Skip items we already applied locally (ProcessedItemIndex is the
            // watermark of items handed to HandleItem successfully — it advances
            // in DrainItemQueue after apply, NOT here. Advancing on enqueue
            // would lose items that get queued but never drained, e.g. when a
            // faction-mismatch disconnect runs before the next frame's drain.)
            while (helper.Any())
            {
                var info  = helper.DequeueItem();
                var index = helper.Index - 1; // index of the item we just dequeued

                if (index < ProcessedItemIndex)
                    continue;

                _pendingItems.Enqueue(new ApItem(info, index, _replayTracker.ClassifyItem()));
            }
        }

        private static void OnSocketPacketReceived(ArchipelagoPacketBase packet)
        {
            if (packet is ReceivedItemsPacket items && _replayTracker.InHistoryBatch)
            {
                Debug.Log($"[Archipelago] Connect history: {items.Items?.Length ?? 0} item(s) from index {items.Index}, " +
                          (_replayTracker.ReplaysHistory
                              ? "replayed on this new colony (traps and Skips skipped)"
                              : $"items from ProcessedItemIndex {ProcessedItemIndex} applied as new"));
            }
            _replayTracker.AfterPacket(packet is ConnectedPacket);
        }

        private static void OnServerMessageReceived(LogMessage message)
        {
            _pendingMessages.Enqueue(ToLogEntry(message));
        }

        /// <summary>Convert a server message into colored segments, following the AP text client.</summary>
        private static ApLogEntry ToLogEntry(LogMessage message)
        {
            var builder = new ApLogEntryBuilder();
            var mentionsSelf = false;
            foreach (var part in message.Parts)
            {
                switch (part)
                {
                    case PlayerMessagePart player:
                        mentionsSelf |= player.IsActivePlayer;
                        builder.Player(part.Text, player.IsActivePlayer);
                        break;
                    case ItemMessagePart item:
                        builder.Item(part.Text, (int)item.Flags);
                        break;
                    case LocationMessagePart _:
                        builder.Location(part.Text);
                        break;
                    case EntranceMessagePart _:
                        builder.Entrance(part.Text);
                        break;
                    default:
                        builder.Text(part.Text);
                        break;
                }
            }

            ApLogMessageKind kind;
            bool senderIsSelf = false, receiverIsSelf = false, playerIsSelf = false;
            switch (message)
            {
                case ItemSendLogMessage itemSend:
                    kind = ApLogMessageKind.ItemSend;
                    senderIsSelf = itemSend.IsSenderTheActivePlayer;
                    receiverIsSelf = itemSend.IsReceiverTheActivePlayer;
                    break;
                case PlayerSpecificLogMessage playerMessage:
                    kind = ApLogMessageKind.PlayerEvent;
                    playerIsSelf = playerMessage.IsActivePlayer;
                    break;
                case CommandResultLogMessage _:
                case AdminCommandResultLogMessage _:
                    kind = ApLogMessageKind.CommandResult;
                    break;
                default:
                    kind = ApLogMessageKind.Other;
                    break;
            }

            return builder.Build(ApLogFeedFilter.InvolvesSelf(kind, senderIsSelf, receiverIsSelf, playerIsSelf, mentionsSelf));
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
