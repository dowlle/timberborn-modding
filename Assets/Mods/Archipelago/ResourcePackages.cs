using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ArchipelagoIntegration
{
    /// <summary>A resource item resolved to the good and amount to deliver.</summary>
    public struct ResourceDelivery
    {
        public string GoodId;       // game GoodId, e.g. "Log"
        public int    Amount;       // amount after the package size percentage
        public string DisplayName;  // plural good name for messages, e.g. "Logs"
    }

    /// <summary>
    /// Resource items: "Package: Logs" (amount from slot_data or the base table
    /// scaled by resource_package_percent) and legacy "Filler: 50 Logs" (amount
    /// in the name). Pure helpers with no game or Unity dependencies so they can
    /// be tested standalone.
    /// </summary>
    public static class ResourcePackages
    {
        public const string PackagePrefix = "Package: ";
        public const string LegacyPrefix = "Filler: ";
        public const int DefaultPercent = 100;

        // Mirrors RESOURCE_PACKAGES in the APWorld (Items.py): display name ->
        // (GoodId, base amount at 100%). Used when slot_data has no resource_packages.
        private static readonly Dictionary<string, (string goodId, int baseAmount)> Packages = new()
        {
            { "Logs",                ("Log",                100) },
            { "Planks",              ("Plank",              50) },
            { "Gears",               ("Gear",               25) },
            { "Treated Planks",      ("TreatedPlank",       20) },
            { "Metal Blocks",        ("MetalBlock",         15) },
            { "Scrap Metal",         ("ScrapMetal",         30) },
            { "Pine Resin",          ("PineResin",          20) },
            { "Extract",             ("Extract",            15) },
            { "Explosives",          ("Explosives",         10) },
            { "Water",               ("Water",              60) },
            { "Berries",             ("Berries",            60) },
            { "Dirt",                ("Dirt",               40) },
            { "Paper",               ("Paper",              40) },
            { "Books",               ("Book",               10) },
            { "Antidote",            ("Antidote",           10) },
            { "Biofuel",             ("Biofuel",            25) },
            { "Bread",               ("Bread",              60) },
            { "Grilled Potatoes",    ("GrilledPotato",      60) },
            { "Cattail Crackers",    ("CattailCracker",     60) },
            { "Maple Pastries",      ("MaplePastry",        30) },
            { "Grilled Chestnuts",   ("GrilledChestnut",    40) },
            { "Grilled Spadderdock", ("GrilledSpadderdock", 45) },
            { "Carrots",             ("Carrot",             40) },
            { "Sunflower Seeds",     ("SunflowerSeeds",     30) },
            { "Metal Parts",         ("MetalPart",          10) },
            { "Coffee",              ("Coffee",             30) },
            { "Grease",              ("Grease",             10) },
            { "Corn Rations",        ("CornRation",         60) },
            { "Fermented Cassava",   ("FermentedCassava",   60) },
            { "Eggplant Rations",    ("EggplantRation",     60) },
            { "Fermented Soybean",   ("FermentedSoybean",   60) },
            { "Kohlrabi",            ("Kohlrabi",           40) },
            { "Mangrove Fruit",      ("MangroveFruit",      40) },
        };

        // Legacy fixed-amount filler items ("Filler: 50 Logs"): display name -> GoodId.
        private static readonly Dictionary<string, string> LegacyGoods = new()
        {
            { "Logs",           "Log" },
            { "Planks",         "Plank" },
            { "Gears",          "Gear" },
            { "Bread",          "Bread" },
            { "Metal Blocks",   "MetalBlock" },
            { "Treated Planks", "TreatedPlank" },
            { "Scrap Metal",    "ScrapMetal" },
        };

        private static readonly Regex LegacyPattern = new(@"^(\d+)\s+(.+)$");

        public static bool IsResourceItem(string itemName) =>
            itemName != null && (itemName.StartsWith(PackagePrefix) || itemName.StartsWith(LegacyPrefix));

        /// <summary>
        /// Base amount scaled by a percentage, rounded half up, at least 1.
        /// Same integer arithmetic as scale_package_amount in the APWorld.
        /// </summary>
        public static int ScaleAmount(int baseAmount, int percent) =>
            Math.Max(1, (int)(((long)baseAmount * percent + 50) / 100));

        /// <summary>
        /// Resolves a resource item. <paramref name="percent"/> is
        /// resource_package_percent from slot_data, or null when the seed has none
        /// (seeds generated before the option): that counts as 100%.
        /// <paramref name="slotPackages"/> is resource_packages from slot_data
        /// (item name -> GoodId and final amount), or null; its entries win over
        /// the built-in table.
        /// </summary>
        public static bool TryResolve(string itemName, int? percent,
            IReadOnlyDictionary<string, (string goodId, int amount)> slotPackages,
            out ResourceDelivery delivery)
        {
            delivery = default;
            if (string.IsNullOrEmpty(itemName))
                return false;
            int pct = percent ?? DefaultPercent;

            if (itemName.StartsWith(PackagePrefix))
            {
                var display = itemName.Substring(PackagePrefix.Length);
                if (slotPackages != null && slotPackages.TryGetValue(itemName, out var sent)
                    && !string.IsNullOrEmpty(sent.goodId) && sent.amount > 0)
                {
                    delivery = new ResourceDelivery { GoodId = sent.goodId, Amount = sent.amount, DisplayName = display };
                    return true;
                }
                if (!Packages.TryGetValue(display, out var entry))
                    return false;
                delivery = new ResourceDelivery
                {
                    GoodId = entry.goodId,
                    Amount = ScaleAmount(entry.baseAmount, pct),
                    DisplayName = display,
                };
                return true;
            }

            if (itemName.StartsWith(LegacyPrefix))
            {
                var match = LegacyPattern.Match(itemName.Substring(LegacyPrefix.Length));
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var named))
                    return false;
                var display = match.Groups[2].Value;
                if (!LegacyGoods.TryGetValue(display, out var goodId))
                    return false;
                delivery = new ResourceDelivery { GoodId = goodId, Amount = ScaleAmount(named, pct), DisplayName = display };
                return true;
            }

            return false;
        }

        /// <summary>Display name for a GoodId ("Plank" -> "Planks"), or the id itself.</summary>
        public static string GoodDisplayName(string goodId)
        {
            foreach (var entry in LegacyGoods)
                if (entry.Value == goodId)
                    return entry.Key;
            foreach (var entry in Packages)
                if (entry.Value.goodId == goodId)
                    return entry.Key;
            return goodId;
        }
    }
}
