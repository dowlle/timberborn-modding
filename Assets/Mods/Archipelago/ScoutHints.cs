using System;
using System.Collections.Generic;
using System.Linq;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Decides which shop locations a Scout item hints. A scouted path reveals and
    /// hints only its next unbought slot; later slots stay hidden until the path
    /// advances to them. Each location is hinted at most once per save.
    /// Pure logic, no Unity or Timberborn types, so it is contract-tested.
    /// </summary>
    public static class ScoutHints
    {
        /// <summary>
        /// Location id of the lowest-level slot on <paramref name="path"/> that is not
        /// checked yet, or null when the path is complete or unknown. Matches the slot
        /// the shop card shows (slots ordered by Level).
        /// </summary>
        public static long? NextSlot(
            IEnumerable<(string Path, int Level, long LocationId)> layout,
            string path,
            ICollection<string> checkedLocations)
        {
            if (layout == null || path == null) return null;
            foreach (var slot in layout.Where(s => s.Path == path).OrderBy(s => s.Level))
            {
                if (checkedLocations == null || !checkedLocations.Contains(slot.LocationId.ToString()))
                    return slot.LocationId;
            }
            return null;
        }

        /// <summary>
        /// The next unbought slot of every scouted path, minus locations already hinted.
        /// At most one location per scouted path. Ordered by path letter.
        /// </summary>
        public static List<long> LocationsToHint(
            IEnumerable<(string Path, int Level, long LocationId)> layout,
            ICollection<string> scoutedPaths,
            ICollection<string> checkedLocations,
            ICollection<long> hintedLocations)
        {
            var result = new List<long>();
            if (layout == null || scoutedPaths == null || scoutedPaths.Count == 0) return result;
            var slots = layout.ToList();
            foreach (var path in scoutedPaths.OrderBy(p => p, StringComparer.Ordinal))
            {
                var next = NextSlot(slots, path, checkedLocations);
                if (next == null) continue;
                if (hintedLocations != null && hintedLocations.Contains(next.Value)) continue;
                if (!result.Contains(next.Value)) result.Add(next.Value);
            }
            return result;
        }

        /// <summary>Save format for the hinted set: ids joined with '|'.</summary>
        public static string Serialize(IEnumerable<long> hinted)
        {
            return hinted == null ? "" : string.Join("|", hinted.OrderBy(id => id));
        }

        /// <summary>Reads the save format; skips empty or malformed entries.</summary>
        public static HashSet<long> Deserialize(string raw)
        {
            var result = new HashSet<long>();
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (var part in raw.Split('|'))
            {
                if (long.TryParse(part, out var id))
                    result.Add(id);
            }
            return result;
        }
    }
}
