using System;
using System.Collections.Generic;
using ArchipelagoIntegration;

// Standalone test executable; compile together with ItemReplayTracker.cs.
// Simulates MultiClient.Net's order: for each packet of a server message the
// ReceivedItemsHelper raises one ItemReceived per item first, then our
// PacketReceived handler (subscribed later) sees the same packet.
public static class ItemReplayTrackerTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private sealed class Session
    {
        private readonly ItemReplayTracker _tracker = new();
        public readonly List<bool> Replays = new();

        public Session Begin(bool replayHistory) { _tracker.BeginSession(replayHistory); return this; }
        public Session Connected() { _tracker.AfterPacket(true); return this; }
        public Session Other() { _tracker.AfterPacket(false); return this; }

        public Session Items(int count)
        {
            for (int i = 0; i < count; i++) Replays.Add(_tracker.ClassifyItem());
            _tracker.AfterPacket(false);
            return this;
        }

        public Session End() { _tracker.EndSession(); return this; }
        public bool InHistory => _tracker.InHistoryBatch;
    }

    private static int Count(List<bool> values, bool value)
    {
        int n = 0;
        foreach (var v in values) if (v == value) n++;
        return n;
    }

    public static int Main()
    {
        // Fresh-save rule.
        Check(ItemReplayTracker.IsFreshSave(0, false, false), "no applied items and no bound slot is a new colony");
        Check(!ItemReplayTracker.IsFreshSave(0, true, false), "bound save with no items yet is not a new colony");
        Check(!ItemReplayTracker.IsFreshSave(5, true, false), "reloaded save is not a new colony");
        Check(!ItemReplayTracker.IsFreshSave(5, false, false), "applied items mean the history was applied");
        Check(ItemReplayTracker.IsFreshSave(0, true, true), "healed orphaned index replays history like a new colony");

        // New colony on a slot with 12 items; the log case (Plank, Hazardous Weather...).
        var fresh = new Session().Begin(true).Connected().Items(12).Other().Items(1).Items(2);
        Check(fresh.Replays.Count == 15, "all items delivered");
        Check(Count(fresh.Replays.GetRange(0, 12), true) == 12, "connect history replays on a new colony");
        Check(Count(fresh.Replays.GetRange(12, 3), false) == 3, "live items during play are not replays");

        // Chat or other packets after the history batch do not extend it.
        var chatter = new Session().Begin(true).Connected().Items(3).Other().Other().Items(1);
        Check(chatter.Replays[3] == false, "item after chatter is live");

        // Empty slot: the first live item also arrives with index 0 but is not history.
        var empty = new Session().Begin(true).Connected().Other().Items(1);
        Check(empty.Replays.Count == 1 && !empty.Replays[0], "first item of an empty slot is live");

        // Reload or reconnect: the library only reports items past ProcessedItemIndex to
        // the queue, and those were never applied to this save, so they are new.
        var reload = new Session().Begin(false).Connected().Items(4).Items(1);
        Check(Count(reload.Replays, false) == 5, "reload and reconnect never mark items as replays");

        // Reconnect after the first connect of a new colony: flag already cleared by the
        // manager, so items received while disconnected (traps included) apply as new.
        var reconnect = new Session().Begin(true).Connected().Items(2).End().Begin(false).Connected().Items(3);
        Check(Count(reconnect.Replays.GetRange(0, 2), true) == 2, "first connect replays");
        Check(Count(reconnect.Replays.GetRange(2, 3), false) == 3, "reconnect applies missed items as new");

        // Disconnect clears state; items raised without a Connected packet are live.
        var ended = new Session().Begin(true).Connected().End();
        Check(!ended.InHistory, "disconnect ends the history window");
        ended.Items(1);
        Check(!ended.Replays[0], "no replay after disconnect");

        // A later full resync within one session (index 0 after Sync) is not history.
        var resync = new Session().Begin(true).Connected().Items(2).Items(2);
        Check(!resync.Replays[2] && !resync.Replays[3], "in-session resync items are live");

        Console.WriteLine("ItemReplayTrackerTests passed");
        return 0;
    }
}
