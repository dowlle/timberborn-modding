using System;
using System.Collections.Generic;
using ArchipelagoIntegration;

// Standalone test executable; compile together with ResourcePackages.cs.
public static class ResourcePackageTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static ResourceDelivery Resolve(string name, int? percent,
        IReadOnlyDictionary<string, (string goodId, int amount)> slot = null)
    {
        Check(ResourcePackages.TryResolve(name, percent, slot, out var delivery), name + " resolves");
        return delivery;
    }

    public static int Main()
    {
        // Every RESOURCE_PACKAGES row in the APWorld (Items.py): GoodId and base amount.
        var expected = new Dictionary<string, (string goodId, int baseAmount)>
        {
            { "Package: Logs",                 ("Log", 100) },
            { "Package: Planks",               ("Plank", 50) },
            { "Package: Gears",                ("Gear", 25) },
            { "Package: Treated Planks",       ("TreatedPlank", 20) },
            { "Package: Metal Blocks",         ("MetalBlock", 15) },
            { "Package: Scrap Metal",          ("ScrapMetal", 30) },
            { "Package: Pine Resin",           ("PineResin", 20) },
            { "Package: Extract",              ("Extract", 15) },
            { "Package: Explosives",           ("Explosives", 10) },
            { "Package: Water",                ("Water", 60) },
            { "Package: Berries",              ("Berries", 60) },
            { "Package: Dirt",                 ("Dirt", 40) },
            { "Package: Paper",                ("Paper", 40) },
            { "Package: Books",                ("Book", 10) },
            { "Package: Antidote",             ("Antidote", 10) },
            { "Package: Biofuel",              ("Biofuel", 25) },
            { "Package: Bread",                ("Bread", 60) },
            { "Package: Grilled Potatoes",     ("GrilledPotato", 60) },
            { "Package: Cattail Crackers",     ("CattailCracker", 60) },
            { "Package: Maple Pastries",       ("MaplePastry", 30) },
            { "Package: Grilled Chestnuts",    ("GrilledChestnut", 40) },
            { "Package: Grilled Spadderdock",  ("GrilledSpadderdock", 45) },
            { "Package: Carrots",              ("Carrot", 40) },
            { "Package: Sunflower Seeds",      ("SunflowerSeeds", 30) },
            { "Package: Metal Parts",          ("MetalPart", 10) },
            { "Package: Coffee",               ("Coffee", 30) },
            { "Package: Grease",               ("Grease", 10) },
            { "Package: Corn Rations",         ("CornRation", 60) },
            { "Package: Fermented Cassava",    ("FermentedCassava", 60) },
            { "Package: Eggplant Rations",     ("EggplantRation", 60) },
            { "Package: Fermented Soybean",    ("FermentedSoybean", 60) },
            { "Package: Kohlrabi",             ("Kohlrabi", 40) },
            { "Package: Mangrove Fruit",       ("MangroveFruit", 40) },
        };
        Check(expected.Count == 33, "all 33 package items listed");
        foreach (var pair in expected)
        {
            var d = Resolve(pair.Key, null);
            Check(d.GoodId == pair.Value.goodId && d.Amount == pair.Value.baseAmount,
                pair.Key + " -> " + d.Amount + " " + d.GoodId);
            Check(d.DisplayName == pair.Key.Substring("Package: ".Length), pair.Key + " display name");
        }

        // Same rounding as scale_package_amount in the APWorld: half up, minimum 1.
        Check(ResourcePackages.ScaleAmount(100, 100) == 100, "100% unchanged");
        Check(ResourcePackages.ScaleAmount(15, 10) == 2, "1.5 rounds up");
        Check(ResourcePackages.ScaleAmount(25, 10) == 3, "2.5 rounds up");
        Check(ResourcePackages.ScaleAmount(10, 10) == 1, "10% of 10");
        Check(ResourcePackages.ScaleAmount(1, 10) == 1, "minimum 1");
        Check(ResourcePackages.ScaleAmount(45, 150) == 68, "67.5 rounds up");
        Check(ResourcePackages.ScaleAmount(60, 1000) == 600, "1000%");
        Check(ResourcePackages.ScaleAmount(10, 33) == 3, "3.3 rounds down");

        // Running co-op seed: no resource_package_percent, no resource_packages.
        // Legacy names keep delivering the amount in the name.
        var legacy = Resolve("Filler: 20 Planks", null);
        Check(legacy.GoodId == "Plank" && legacy.Amount == 20 && legacy.DisplayName == "Planks", "legacy 20 Planks");
        Check(Resolve("Filler: 50 Logs", null).Amount == 50, "legacy 50 Logs");
        Check(Resolve("Filler: 5 Scrap Metal", null).GoodId == "ScrapMetal", "legacy scrap");
        Check(Resolve("Filler: 20 Bread", null).GoodId == "Bread", "legacy bread");
        Check(Resolve("Filler: 20 Planks", 100).Amount == 20, "legacy at explicit 100%");
        Check(Resolve("Filler: 20 Planks", 250).Amount == 50, "legacy scaled when a percent exists");

        // New seeds: the percent scales the base table when slot_data has no amounts ...
        Check(Resolve("Package: Logs", 250).Amount == 250, "Logs at 250%");
        Check(Resolve("Package: Metal Blocks", 250).Amount == 38, "Metal Blocks at 250%");
        Check(Resolve("Package: Grease", 10).Amount == 1, "Grease at 10%");
        Check(Resolve("Package: Logs", 1000).Amount == 1000, "Logs at 1000%");

        // ... and the amounts sent in slot_data win over the local table.
        var slot = new Dictionary<string, (string goodId, int amount)>
        {
            { "Package: Logs", ("Log", 123) },
            { "Package: Kohlrabi", ("Kohlrabi", 7) },
            { "Package: Bread", ("", 5) },  // invalid entry falls back to the table
        };
        Check(Resolve("Package: Logs", 50, slot).Amount == 123, "slot_data amount used");
        Check(Resolve("Package: Kohlrabi", null, slot).Amount == 7, "slot_data amount without percent");
        Check(Resolve("Package: Bread", 50, slot).Amount == 30, "invalid slot entry ignored");
        Check(Resolve("Package: Planks", 50, slot).Amount == 25, "missing slot entry uses table");

        // Unknown and non-resource items are rejected.
        Check(!ResourcePackages.TryResolve("Package: Unobtainium", 100, null, out _), "unknown package");
        Check(!ResourcePackages.TryResolve("Filler: 5 Unobtainium", 100, null, out _), "unknown legacy good");
        Check(!ResourcePackages.TryResolve("Filler: Logs", 100, null, out _), "legacy without amount");
        Check(!ResourcePackages.TryResolve("Boost: Faster Working Speed", 100, null, out _), "boost");
        Check(!ResourcePackages.TryResolve(null, 100, null, out _), "null name");
        Check(ResourcePackages.IsResourceItem("Package: Logs") && ResourcePackages.IsResourceItem("Filler: 50 Logs"), "routing");
        Check(!ResourcePackages.IsResourceItem("Trap: Hungry Beavers"), "trap not routed");

        // Display names for pending-goods messages.
        Check(ResourcePackages.GoodDisplayName("Plank") == "Planks", "Plank display");
        Check(ResourcePackages.GoodDisplayName("CornRation") == "Corn Rations", "CornRation display");
        Check(ResourcePackages.GoodDisplayName("Mystery") == "Mystery", "unknown id passes through");

        Console.WriteLine("ResourcePackageTests passed");
        return 0;
    }
}
