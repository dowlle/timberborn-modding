namespace ArchipelagoIntegration
{
    /// <summary>
    /// Decides which received items are replays of the slot's item history.
    ///
    /// On login the AP server answers with one message holding the Connected packet
    /// followed, when the slot already has items, by ReceivedItems(index 0) with that
    /// whole history. MultiClient.Net handles the packets of a message in order on its
    /// socket thread, and its ReceivedItemsHelper raises ItemReceived while it handles
    /// the packet, before any handler subscribed later sees that packet. So the items
    /// raised while the packet right after Connected is being handled are exactly the
    /// history the server had at connect time. Items that arrive later are live, even
    /// when their packet also starts at index 0 (the first item of an empty slot).
    ///
    /// History items only count as replays for a save that never applied this slot's
    /// history (a new colony). Reloads and reconnects apply everything past
    /// ProcessedItemIndex as new items, so traps sent while offline still fire.
    ///
    /// Pure logic without Unity or MultiClient types so it can be contract-tested.
    /// </summary>
    public sealed class ItemReplayTracker
    {
        private volatile bool _replayHistory;
        private volatile bool _historyPacketNext;

        /// <summary>True between the Connected packet and the end of the next packet.</summary>
        public bool InHistoryBatch => _historyPacketNext;

        /// <summary>True when this session replays the connect-time history.</summary>
        public bool ReplaysHistory => _replayHistory;

        /// <summary>
        /// True for a save that has not yet applied its slot's item history: no applied
        /// items (ProcessedItemIndex 0) and never bound to a slot, or repaired by the
        /// orphaned-index heal, which wipes the applied items.
        /// </summary>
        public static bool IsFreshSave(int processedItemIndex, bool boundToSlot, bool healedOrphanedIndex)
        {
            return processedItemIndex == 0 && (!boundToSlot || healedOrphanedIndex);
        }

        /// <summary>
        /// True for items whose replay must not be applied again: traps (unfair on a
        /// fresh start) and Skips (a Skip spent in an earlier colony stays spent).
        /// Everything else, blueprints and starting items above all, is idempotent or
        /// needed and is applied on replay: the connect-time history of a new colony
        /// starts with the slot's starting inventory (Forester, Stairs, Platform).
        /// </summary>
        public static bool SkipsOnReplay(string itemName)
        {
            return itemName == "Skip" || (itemName != null && itemName.StartsWith("Trap: "));
        }

        /// <summary>Call before login, on the main thread.</summary>
        public void BeginSession(bool replayHistory)
        {
            _historyPacketNext = false;
            _replayHistory = replayHistory;
        }

        /// <summary>Call on disconnect or failed login.</summary>
        public void EndSession()
        {
            _replayHistory = false;
            _historyPacketNext = false;
        }

        /// <summary>
        /// Socket thread, after the library has handled a packet (our PacketReceived
        /// handler is subscribed after ReceivedItemsHelper's).
        /// </summary>
        public void AfterPacket(bool isConnectedPacket)
        {
            _historyPacketNext = isConnectedPacket;
        }

        /// <summary>Socket thread, for each item ReceivedItemsHelper raises.</summary>
        public bool ClassifyItem()
        {
            return _replayHistory && _historyPacketNext;
        }
    }
}
