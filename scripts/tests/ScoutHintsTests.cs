using System;
using System.Collections.Generic;
using ArchipelagoIntegration;

// Standalone test executable; compile together with ScoutHints.cs.
public static class ScoutHintsTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static bool Same(List<long> actual, params long[] expected)
    {
        if (actual.Count != expected.Length) return false;
        for (int i = 0; i < expected.Length; i++) if (actual[i] != expected[i]) return false;
        return true;
    }

    public static int Main()
    {
        // Two paths, deliberately listed out of level order.
        var layout = new List<(string Path, int Level, long LocationId)>
        {
            ("A", 2, 103), ("A", 0, 101), ("A", 1, 102),
            ("B", 0, 201), ("B", 1, 202),
        };
        var none = new HashSet<string>();
        var noneHinted = new HashSet<long>();

        // Next slot follows Level, not list order, and skips checked slots.
        Check(ScoutHints.NextSlot(layout, "A", none) == 101, "first slot of a fresh path");
        Check(ScoutHints.NextSlot(layout, "A", new HashSet<string> { "101" }) == 102, "advances after a buy");
        Check(ScoutHints.NextSlot(layout, "A", new HashSet<string> { "101", "102", "103" }) == null, "complete path has no next slot");
        Check(ScoutHints.NextSlot(layout, "C", none) == null, "unknown path has no next slot");

        // Receiving a scout hints only the next slot, not the whole path (was 33 hints).
        var scoutedA = new HashSet<string> { "A" };
        Check(Same(ScoutHints.LocationsToHint(layout, scoutedA, none, noneHinted), 101), "scout hints one location");

        // Unscouted paths never hint.
        Check(ScoutHints.LocationsToHint(layout, none, none, noneHinted).Count == 0, "no scouted paths, no hints");

        // Already hinted: reconnect, reload or replay does not hint again.
        var hinted = new HashSet<long> { 101 };
        Check(ScoutHints.LocationsToHint(layout, scoutedA, none, hinted).Count == 0, "hinted slot is not hinted twice");

        // Path advances (buy or skip, also while offline): the new next slot gets hinted.
        var checkedA1 = new HashSet<string> { "101" };
        Check(Same(ScoutHints.LocationsToHint(layout, scoutedA, checkedA1, hinted), 102), "advance hints the new next slot");

        // Several advances while offline hint only the current next slot, never skipped-over ones.
        var checkedA2 = new HashSet<string> { "101", "102" };
        Check(Same(ScoutHints.LocationsToHint(layout, scoutedA, checkedA2, hinted), 103), "offline advances hint only the current slot");

        // Completed path hints nothing.
        var checkedAll = new HashSet<string> { "101", "102", "103" };
        Check(ScoutHints.LocationsToHint(layout, scoutedA, checkedAll, hinted).Count == 0, "completed path hints nothing");

        // Two scouted paths: one location each, ordered by path.
        var scoutedBA = new HashSet<string> { "B", "A" };
        Check(Same(ScoutHints.LocationsToHint(layout, scoutedBA, checkedA1, noneHinted), 102, 201), "one location per scouted path");

        // Old save: path scouted with the old code, no hinted set yet. Only the next slot.
        Check(Same(ScoutHints.LocationsToHint(layout, new HashSet<string> { "B" }, none, noneHinted), 201), "old save hints only the next slot");

        // No layout yet (first connect before slot_data): nothing to hint.
        Check(ScoutHints.LocationsToHint(null, scoutedA, none, noneHinted).Count == 0, "no layout, no hints");

        // Save format round trip and heal of bad entries.
        var saved = ScoutHints.Serialize(new[] { 202L, 101L });
        Check(saved == "101|202", "serialized sorted: " + saved);
        var loaded = ScoutHints.Deserialize(saved);
        Check(loaded.Count == 2 && loaded.Contains(101) && loaded.Contains(202), "round trip");
        var healed = ScoutHints.Deserialize("101||abc|202|");
        Check(healed.Count == 2 && healed.Contains(101) && healed.Contains(202), "malformed entries are skipped");
        Check(ScoutHints.Deserialize(null).Count == 0 && ScoutHints.Deserialize("").Count == 0, "empty input");
        Check(ScoutHints.Serialize(null) == "", "null serializes empty");

        Console.WriteLine("ScoutHintsTests passed");
        return 0;
    }
}
