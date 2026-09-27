using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Definition of a single milestone, parsed from slot_data.
    /// </summary>
    public class MilestoneDefinition
    {
        public string Name;        // "Population: Reach 15 Beavers"
        public long   LocationId;  // AP location ID
        public string Type;        // "population", "wellbeing", "survival", "wonder", "resource"
        public int    Threshold;   // numeric threshold (10, 5, 1, etc.)
        public string GoodId;      // game GoodId string for resource milestones (e.g. "Log", "MetalBlock")
    }

    /// <summary>
    /// Pure helpers for milestone GoodIds and the compact save format.
    /// No game or Unity dependencies so they can be tested standalone.
    /// </summary>
    public static class MilestoneCodec
    {
        // Mirrors _RESOURCE_GOOD_IDS in the APWorld (__init__.py). Used when a
        // milestone arrives without a good_id, e.g. from saves written before the
        // save format stored GoodId. Ids checked against 1.1 Goods blueprints.
        private static readonly Dictionary<string, string> ResourceGoodIds = new()
        {
            { "Logs",              "Log" },
            { "Planks",            "Plank" },
            { "Gears",             "Gear" },
            { "Bread",             "Bread" },
            { "Metal Blocks",      "MetalBlock" },
            { "Treated Planks",    "TreatedPlank" },
            { "Scrap Metal",       "ScrapMetal" },
            { "Pine Resin",        "PineResin" },
            { "Water",             "Water" },
            { "Berries",           "Berries" },
            { "Extract",           "Extract" },
            { "Explosives",        "Explosives" },
            { "Paper",             "Paper" },
            { "Grilled Potatoes",  "GrilledPotato" },
            { "Cattail Crackers",  "CattailCracker" },
            { "Maple Pastries",    "MaplePastry" },
            { "Books",             "Book" },
            { "Biofuel",           "Biofuel" },
            { "Antidote",          "Antidote" },
            { "Corn Rations",      "CornRation" },
            { "Fermented Cassava", "FermentedCassava" },
            { "Eggplant Rations",  "EggplantRation" },
            { "Fermented Soybean", "FermentedSoybean" },
            { "Kohlrabi",          "Kohlrabi" },
            { "Mangrove Fruit",    "MangroveFruit" },
            { "Metal Parts",       "MetalPart" },
            { "Coffee",            "Coffee" },
            { "Grease",            "Grease" },
        };

        private static readonly Regex ReachPattern = new(@"Reach \d+ (.+)$");

        /// <summary>
        /// Returns the GoodId for a resource milestone name such as
        /// "Resource: Reach 500 Planks", or "" if the name is not a known resource milestone.
        /// </summary>
        public static string GoodIdFromName(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("Resource:"))
                return "";
            var match = ReachPattern.Match(name);
            if (!match.Success)
                return "";
            return ResourceGoodIds.TryGetValue(match.Groups[1].Value, out var goodId) ? goodId : "";
        }

        /// <summary>Keeps a provided GoodId; otherwise derives it from the milestone name.</summary>
        public static string ResolveGoodId(string name, string goodId) =>
            string.IsNullOrEmpty(goodId) ? GoodIdFromName(name) : goodId;

        // -----------------------------------------------------------------
        // Compact save format
        // Format per milestone: "Name,LocationId,Type,Threshold,GoodId"
        // Milestones separated by ";". Older saves omit GoodId (4 fields).
        // -----------------------------------------------------------------

        public static string Serialize(List<MilestoneDefinition> milestones)
        {
            return string.Join(";", milestones.Select(m =>
                $"{m.Name},{m.LocationId},{m.Type},{m.Threshold},{m.GoodId ?? ""}"));
        }

        public static List<MilestoneDefinition> Deserialize(string raw)
        {
            var result = new List<MilestoneDefinition>();
            foreach (var entry in raw.Split(';'))
            {
                var parts = entry.Split(',');
                if (parts.Length < 4) continue;
                var name = parts[0];
                result.Add(new MilestoneDefinition
                {
                    Name = name,
                    LocationId = long.Parse(parts[1]),
                    Type = parts[2],
                    Threshold = int.Parse(parts[3]),
                    GoodId = ResolveGoodId(name, parts.Length > 4 ? parts[4] : ""),
                });
            }
            return result;
        }
    }
}
