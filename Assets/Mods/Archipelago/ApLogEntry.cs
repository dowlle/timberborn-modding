using System;
using System.Collections.Generic;
using System.Text;

namespace ArchipelagoIntegration
{
    /// <summary>What a piece of log text refers to. Decides its Archipelago text color.</summary>
    public enum ApLogSegmentKind
    {
        Text,
        OwnPlayer,
        OtherPlayer,
        ItemFiller,
        ItemProgression,
        ItemUseful,
        ItemTrap,
        Location,
        Entrance,
    }

    public readonly struct ApLogSegment
    {
        public readonly string Text;
        public readonly ApLogSegmentKind Kind;

        public ApLogSegment(string text, ApLogSegmentKind kind)
        {
            Text = text ?? "";
            Kind = kind;
        }
    }

    /// <summary>
    /// One line in the AP event log: colored segments plus whether it concerns this slot.
    /// Built off the main thread for server messages, so it holds plain data only.
    /// </summary>
    public sealed class ApLogEntry
    {
        public IReadOnlyList<ApLogSegment> Segments { get; }

        /// <summary>True when the entry involves this slot (sent by or to it, its own checks, its own events).</summary>
        public bool InvolvesSelf { get; }

        public string Timestamp { get; set; } = "";

        public ApLogEntry(IReadOnlyList<ApLogSegment> segments, bool involvesSelf)
        {
            Segments = segments ?? Array.Empty<ApLogSegment>();
            InvolvesSelf = involvesSelf;
        }

        /// <summary>Uncolored local event. Local mod events are always about this slot.</summary>
        public static ApLogEntry Plain(string text)
        {
            return new ApLogEntry(new[] { new ApLogSegment(text, ApLogSegmentKind.Text) }, true);
        }

        public string PlainText
        {
            get
            {
                var builder = new StringBuilder();
                foreach (var segment in Segments) builder.Append(segment.Text);
                return builder.ToString();
            }
        }
    }

    /// <summary>Fluent builder for colored log entries.</summary>
    public sealed class ApLogEntryBuilder
    {
        private readonly List<ApLogSegment> _segments = new();

        public ApLogEntryBuilder Text(string text) => Add(text, ApLogSegmentKind.Text);
        public ApLogEntryBuilder Player(string name, bool isSelf) =>
            Add(name, isSelf ? ApLogSegmentKind.OwnPlayer : ApLogSegmentKind.OtherPlayer);
        public ApLogEntryBuilder Item(string name, int flags) => Add(name, ApLogColors.ItemKind(flags));
        public ApLogEntryBuilder Location(string name) => Add(name, ApLogSegmentKind.Location);
        public ApLogEntryBuilder Entrance(string name) => Add(name, ApLogSegmentKind.Entrance);

        public ApLogEntryBuilder Add(string text, ApLogSegmentKind kind)
        {
            if (!string.IsNullOrEmpty(text)) _segments.Add(new ApLogSegment(text, kind));
            return this;
        }

        public ApLogEntry Build(bool involvesSelf) => new(_segments.ToArray(), involvesSelf);
    }

    /// <summary>
    /// Archipelago text client colors, copied from the Archipelago source:
    /// data/client.kv (TextColors) and NetUtils.JSONtoTextParser (which node gets which color).
    /// </summary>
    public static class ApLogColors
    {
        public const string Magenta = "EE00EE";   // your slot/player
        public const string Yellow = "FAFAD2";    // other slots/players
        public const string Plum = "AF99EF";      // progression item
        public const string SlateBlue = "6D8BE8"; // useful item
        public const string Salmon = "FA8072";    // trap item
        public const string Cyan = "00EEEE";      // regular (filler) item
        public const string Green = "00FF7F";     // location
        public const string Blue = "6495ED";      // entrance

        public const int FlagProgression = 0b001;
        public const int FlagUseful = 0b010;
        public const int FlagTrap = 0b100;

        /// <summary>Same precedence as NetUtils._handle_item_name: progression, then useful, then trap.</summary>
        public static ApLogSegmentKind ItemKind(int flags)
        {
            if ((flags & FlagProgression) != 0) return ApLogSegmentKind.ItemProgression;
            if ((flags & FlagUseful) != 0) return ApLogSegmentKind.ItemUseful;
            if ((flags & FlagTrap) != 0) return ApLogSegmentKind.ItemTrap;
            return ApLogSegmentKind.ItemFiller;
        }

        /// <summary>Hex color (RRGGBB) for a segment kind, or null for default text color.</summary>
        public static string HexFor(ApLogSegmentKind kind)
        {
            switch (kind)
            {
                case ApLogSegmentKind.OwnPlayer: return Magenta;
                case ApLogSegmentKind.OtherPlayer: return Yellow;
                case ApLogSegmentKind.ItemProgression: return Plum;
                case ApLogSegmentKind.ItemUseful: return SlateBlue;
                case ApLogSegmentKind.ItemTrap: return Salmon;
                case ApLogSegmentKind.ItemFiller: return Cyan;
                case ApLogSegmentKind.Location: return Green;
                case ApLogSegmentKind.Entrance: return Blue;
                default: return null;
            }
        }

        /// <summary>
        /// UI Toolkit rich text for the entry. Segment text is escaped so names containing
        /// '&lt;' can never open a tag.
        /// </summary>
        public static string ToRichText(ApLogEntry entry)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(entry.Timestamp))
                builder.Append('[').Append(entry.Timestamp).Append("] ");
            foreach (var segment in entry.Segments)
            {
                var hex = HexFor(segment.Kind);
                if (hex != null) builder.Append("<color=#").Append(hex).Append('>');
                builder.Append(Escape(segment.Text));
                if (hex != null) builder.Append("</color>");
            }
            return builder.ToString();
        }

        public static string Escape(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : text.Replace("<", "<noparse><</noparse>");
        }
    }

    /// <summary>Kinds of server log messages, as far as the own-feed filter cares.</summary>
    public enum ApLogMessageKind
    {
        Local,
        ItemSend,
        PlayerEvent,
        CommandResult,
        Other,
    }

    /// <summary>Decides which entries belong to this slot's own feed.</summary>
    public static class ApLogFeedFilter
    {
        /// <param name="senderIsSelf">Item messages: this slot found the item.</param>
        /// <param name="receiverIsSelf">Item messages: this slot receives the item.</param>
        /// <param name="playerIsSelf">Player events (join, leave, chat, goal, release): the acting player is this slot.</param>
        /// <param name="mentionsSelf">Any player name in the message is this slot.</param>
        public static bool InvolvesSelf(ApLogMessageKind kind, bool senderIsSelf, bool receiverIsSelf,
                                        bool playerIsSelf, bool mentionsSelf)
        {
            switch (kind)
            {
                case ApLogMessageKind.Local:
                case ApLogMessageKind.CommandResult:
                    return true;
                case ApLogMessageKind.ItemSend:
                    return senderIsSelf || receiverIsSelf;
                case ApLogMessageKind.PlayerEvent:
                    return playerIsSelf;
                default:
                    return mentionsSelf;
            }
        }

        public static bool IsVisible(ApLogEntry entry, bool ownFeedOnly)
        {
            return entry != null && (!ownFeedOnly || entry.InvolvesSelf);
        }
    }
}
