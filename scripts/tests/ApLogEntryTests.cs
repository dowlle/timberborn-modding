using System;
using ArchipelagoIntegration;

// Standalone test executable; compile together with ApLogEntry.cs.
public static class ApLogEntryTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static int Main()
    {
        // Colors match Archipelago data/client.kv TextColors.
        Check(ApLogColors.HexFor(ApLogSegmentKind.OwnPlayer) == "EE00EE", "own player is magenta");
        Check(ApLogColors.HexFor(ApLogSegmentKind.OtherPlayer) == "FAFAD2", "other player is yellow");
        Check(ApLogColors.HexFor(ApLogSegmentKind.ItemProgression) == "AF99EF", "progression is plum");
        Check(ApLogColors.HexFor(ApLogSegmentKind.ItemUseful) == "6D8BE8", "useful is slateblue");
        Check(ApLogColors.HexFor(ApLogSegmentKind.ItemTrap) == "FA8072", "trap is salmon");
        Check(ApLogColors.HexFor(ApLogSegmentKind.ItemFiller) == "00EEEE", "filler is cyan");
        Check(ApLogColors.HexFor(ApLogSegmentKind.Location) == "00FF7F", "location is green");
        Check(ApLogColors.HexFor(ApLogSegmentKind.Entrance) == "6495ED", "entrance is blue");
        Check(ApLogColors.HexFor(ApLogSegmentKind.Text) == null, "plain text keeps the default color");

        // Item flag precedence follows NetUtils._handle_item_name.
        Check(ApLogColors.ItemKind(0) == ApLogSegmentKind.ItemFiller, "no flags is filler");
        Check(ApLogColors.ItemKind(1) == ApLogSegmentKind.ItemProgression, "advancement is progression");
        Check(ApLogColors.ItemKind(2) == ApLogSegmentKind.ItemUseful, "never_exclude is useful");
        Check(ApLogColors.ItemKind(4) == ApLogSegmentKind.ItemTrap, "trap flag is trap");
        Check(ApLogColors.ItemKind(3) == ApLogSegmentKind.ItemProgression, "progression beats useful");
        Check(ApLogColors.ItemKind(5) == ApLogSegmentKind.ItemProgression, "progression beats trap");
        Check(ApLogColors.ItemKind(6) == ApLogSegmentKind.ItemUseful, "useful beats trap");
        Check(ApLogColors.ItemKind(8) == ApLogSegmentKind.ItemFiller, "unknown bits fall back to filler");

        // Rich text output.
        var entry = new ApLogEntryBuilder()
            .Player("Dowlle", true).Text(" sent ").Item("Blueprint: Lodge", 1).Text(" to ")
            .Player("Other", false).Text(" (").Location("Shop A1").Text(")")
            .Build(true);
        entry.Timestamp = "12:34";
        var rich = ApLogColors.ToRichText(entry);
        Check(rich == "[12:34] <color=#EE00EE>Dowlle</color> sent <color=#AF99EF>Blueprint: Lodge</color> to "
                     + "<color=#FAFAD2>Other</color> (<color=#00FF7F>Shop A1</color>)", "rich text: " + rich);
        Check(entry.PlainText == "Dowlle sent Blueprint: Lodge to Other (Shop A1)", "plain text: " + entry.PlainText);

        // Names cannot inject tags.
        var sneaky = new ApLogEntryBuilder().Player("<b>x", false).Build(false);
        Check(ApLogColors.ToRichText(sneaky) == "<color=#FAFAD2><noparse><</noparse>b>x</color>", "escape: " + ApLogColors.ToRichText(sneaky));

        // Empty segments are dropped; local entries are plain and own.
        Check(new ApLogEntryBuilder().Text("").Item(null, 1).Build(true).Segments.Count == 0, "empty segments dropped");
        var local = ApLogEntry.Plain("Milestone: Reach 15 Beavers");
        Check(local.InvolvesSelf && local.Segments.Count == 1 && local.Segments[0].Kind == ApLogSegmentKind.Text, "plain local entry");

        // Own-feed classification.
        Check(ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.Local, false, false, false, false), "local events are own");
        Check(ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.ItemSend, true, false, false, true), "items I sent are own");
        Check(ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.ItemSend, false, true, false, true), "items sent to me are own");
        Check(ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.ItemSend, true, true, false, true), "my own checks are own");
        Check(!ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.ItemSend, false, false, false, false), "other players' items are not own");
        Check(ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.PlayerEvent, false, false, true, true), "my join/goal is own");
        Check(!ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.PlayerEvent, false, false, false, true), "others' chat is not own even if it names me");
        Check(ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.CommandResult, false, false, false, false), "command results are own");
        Check(ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.Other, false, false, false, true), "other messages naming me are own");
        Check(!ApLogFeedFilter.InvolvesSelf(ApLogMessageKind.Other, false, false, false, false), "server chatter is not own");

        // Filter visibility.
        var mine = ApLogEntry.Plain("mine");
        var theirs = new ApLogEntryBuilder().Text("theirs").Build(false);
        Check(ApLogFeedFilter.IsVisible(mine, false) && ApLogFeedFilter.IsVisible(theirs, false), "show all shows everything");
        Check(ApLogFeedFilter.IsVisible(mine, true) && !ApLogFeedFilter.IsVisible(theirs, true), "own feed hides others");
        Check(!ApLogFeedFilter.IsVisible(null, false), "null entry is never shown");

        Console.WriteLine("ApLogEntryTests passed");
        return 0;
    }
}
