using System;
using System.Collections.Generic;
using ArchipelagoIntegration;

// Standalone test executable; compile together with MilestoneDefinition.cs.
public static class MilestoneCodecTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static int Main()
    {
        // Every resource milestone location in the APWorld (Locations.py) maps to a
        // GoodId present in the Timberborn 1.1 Goods blueprints.
        var expected = new Dictionary<string, string>
        {
            { "Resource: Reach 500 Logs",              "Log" },
            { "Resource: Reach 1000 Logs",             "Log" },
            { "Resource: Reach 500 Planks",            "Plank" },
            { "Resource: Reach 1000 Planks",           "Plank" },
            { "Resource: Reach 100 Gears",             "Gear" },
            { "Resource: Reach 250 Gears",             "Gear" },
            { "Resource: Reach 500 Bread",             "Bread" },
            { "Resource: Reach 100 Metal Blocks",      "MetalBlock" },
            { "Resource: Reach 250 Metal Blocks",      "MetalBlock" },
            { "Resource: Reach 100 Treated Planks",    "TreatedPlank" },
            { "Resource: Reach 250 Treated Planks",    "TreatedPlank" },
            { "Resource: Reach 100 Scrap Metal",       "ScrapMetal" },
            { "Resource: Reach 250 Scrap Metal",       "ScrapMetal" },
            { "Resource: Reach 500 Corn Rations",      "CornRation" },
            { "Resource: Reach 100 Logs",              "Log" },
            { "Resource: Reach 250 Logs",              "Log" },
            { "Resource: Reach 50 Planks",             "Plank" },
            { "Resource: Reach 100 Planks",            "Plank" },
            { "Resource: Reach 10 Gears",              "Gear" },
            { "Resource: Reach 25 Gears",              "Gear" },
            { "Resource: Reach 50 Gears",              "Gear" },
            { "Resource: Reach 10 Treated Planks",     "TreatedPlank" },
            { "Resource: Reach 25 Treated Planks",     "TreatedPlank" },
            { "Resource: Reach 50 Treated Planks",     "TreatedPlank" },
            { "Resource: Reach 10 Metal Blocks",       "MetalBlock" },
            { "Resource: Reach 25 Metal Blocks",       "MetalBlock" },
            { "Resource: Reach 50 Metal Blocks",       "MetalBlock" },
            { "Resource: Reach 25 Scrap Metal",        "ScrapMetal" },
            { "Resource: Reach 50 Scrap Metal",        "ScrapMetal" },
            { "Resource: Reach 10 Pine Resin",         "PineResin" },
            { "Resource: Reach 25 Pine Resin",         "PineResin" },
            { "Resource: Reach 50 Pine Resin",         "PineResin" },
            { "Resource: Reach 100 Water",             "Water" },
            { "Resource: Reach 250 Water",             "Water" },
            { "Resource: Reach 250 Berries",           "Berries" },
            { "Resource: Reach 10 Extract",            "Extract" },
            { "Resource: Reach 25 Extract",            "Extract" },
            { "Resource: Reach 50 Extract",            "Extract" },
            { "Resource: Reach 10 Explosives",         "Explosives" },
            { "Resource: Reach 25 Explosives",         "Explosives" },
            { "Resource: Reach 10 Bread",              "Bread" },
            { "Resource: Reach 25 Bread",              "Bread" },
            { "Resource: Reach 50 Bread",              "Bread" },
            { "Resource: Reach 25 Paper",              "Paper" },
            { "Resource: Reach 50 Paper",              "Paper" },
            { "Resource: Reach 100 Paper",             "Paper" },
            { "Resource: Reach 25 Grilled Potatoes",   "GrilledPotato" },
            { "Resource: Reach 50 Grilled Potatoes",   "GrilledPotato" },
            { "Resource: Reach 100 Grilled Potatoes",  "GrilledPotato" },
            { "Resource: Reach 25 Cattail Crackers",   "CattailCracker" },
            { "Resource: Reach 50 Cattail Crackers",   "CattailCracker" },
            { "Resource: Reach 100 Cattail Crackers",  "CattailCracker" },
            { "Resource: Reach 10 Maple Pastries",     "MaplePastry" },
            { "Resource: Reach 25 Maple Pastries",     "MaplePastry" },
            { "Resource: Reach 50 Maple Pastries",     "MaplePastry" },
            { "Resource: Reach 10 Books",              "Book" },
            { "Resource: Reach 25 Books",              "Book" },
            { "Resource: Reach 10 Biofuel",            "Biofuel" },
            { "Resource: Reach 25 Biofuel",            "Biofuel" },
            { "Resource: Reach 50 Biofuel",            "Biofuel" },
            { "Resource: Reach 10 Antidote",           "Antidote" },
            { "Resource: Reach 25 Antidote",           "Antidote" },
            { "Resource: Reach 25 Corn Rations",       "CornRation" },
            { "Resource: Reach 50 Corn Rations",       "CornRation" },
            { "Resource: Reach 100 Corn Rations",      "CornRation" },
            { "Resource: Reach 25 Fermented Cassava",  "FermentedCassava" },
            { "Resource: Reach 50 Fermented Cassava",  "FermentedCassava" },
            { "Resource: Reach 100 Fermented Cassava", "FermentedCassava" },
            { "Resource: Reach 10 Eggplant Rations",   "EggplantRation" },
            { "Resource: Reach 25 Eggplant Rations",   "EggplantRation" },
            { "Resource: Reach 50 Eggplant Rations",   "EggplantRation" },
            { "Resource: Reach 50 Fermented Soybean",  "FermentedSoybean" },
            { "Resource: Reach 50 Kohlrabi",           "Kohlrabi" },
            { "Resource: Reach 100 Kohlrabi",          "Kohlrabi" },
            { "Resource: Reach 50 Mangrove Fruit",     "MangroveFruit" },
            { "Resource: Reach 100 Mangrove Fruit",    "MangroveFruit" },
            { "Resource: Reach 10 Metal Parts",        "MetalPart" },
            { "Resource: Reach 25 Metal Parts",        "MetalPart" },
            { "Resource: Reach 50 Metal Parts",        "MetalPart" },
            { "Resource: Reach 10 Coffee",             "Coffee" },
            { "Resource: Reach 25 Coffee",             "Coffee" },
            { "Resource: Reach 50 Coffee",             "Coffee" },
            { "Resource: Reach 10 Grease",             "Grease" },
            { "Resource: Reach 25 Grease",             "Grease" },
        };
        Check(expected.Count == 84, "all 84 resource milestone locations listed");
        foreach (var pair in expected)
            Check(MilestoneCodec.GoodIdFromName(pair.Key) == pair.Value,
                pair.Key + " resolves to " + MilestoneCodec.GoodIdFromName(pair.Key) + ", expected " + pair.Value);

        // Non-resource and unknown names get no GoodId.
        Check(MilestoneCodec.GoodIdFromName("Population: Reach 15 Beavers") == "", "population has no GoodId");
        Check(MilestoneCodec.GoodIdFromName("Resource: Reach 50 Unobtainium") == "", "unknown good has no GoodId");
        Check(MilestoneCodec.GoodIdFromName(null) == "", "null name has no GoodId");

        // A GoodId sent in slot_data wins over the name table.
        Check(MilestoneCodec.ResolveGoodId("Resource: Reach 500 Planks", "Custom") == "Custom", "slot_data GoodId kept");
        Check(MilestoneCodec.ResolveGoodId("Resource: Reach 500 Planks", null) == "Plank", "missing GoodId derived");

        // Saves written before GoodId was persisted (4 fields) resolve by name.
        var legacy = MilestoneCodec.Deserialize(
            "Population: Reach 15 Beavers,9700003,population,15;" +
            "Resource: Reach 500 Planks,9710002,resource,500;" +
            "Resource: Reach 250 Treated Planks,9710010,resource,250");
        Check(legacy.Count == 3, "legacy entries parsed");
        Check(legacy[0].GoodId == "", "legacy population GoodId empty");
        Check(legacy[1].GoodId == "Plank" && legacy[1].Threshold == 500 && legacy[1].LocationId == 9710002, "legacy planks");
        Check(legacy[2].GoodId == "TreatedPlank" && legacy[2].Type == "resource", "legacy treated planks");

        // New saves round-trip the GoodId.
        var source = new List<MilestoneDefinition>
        {
            new MilestoneDefinition { Name = "Wellbeing: Reach 5", LocationId = 9700010, Type = "wellbeing", Threshold = 5, GoodId = "" },
            new MilestoneDefinition { Name = "Resource: Reach 1000 Planks", LocationId = 9710003, Type = "resource", Threshold = 1000, GoodId = "Plank" },
            new MilestoneDefinition { Name = "Resource: Reach 100 Gears", LocationId = 9710004, Type = "resource", Threshold = 100, GoodId = null },
        };
        var raw = MilestoneCodec.Serialize(source);
        Check(raw.Contains("Resource: Reach 1000 Planks,9710003,resource,1000,Plank"), "GoodId written as fifth field: " + raw);
        var restored = MilestoneCodec.Deserialize(raw);
        Check(restored.Count == 3, "round trip count");
        Check(restored[0].GoodId == "" && restored[0].Threshold == 5, "round trip wellbeing");
        Check(restored[1].GoodId == "Plank" && restored[1].LocationId == 9710003, "round trip planks");
        Check(restored[2].GoodId == "Gear", "null GoodId resolved on reload");

        Console.WriteLine("MilestoneCodecTests passed");
        return 0;
    }
}
