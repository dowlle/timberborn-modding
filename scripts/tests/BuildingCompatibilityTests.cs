using System;
using System.Collections.Generic;
using System.Linq;
using ArchipelagoIntegration;

namespace ArchipelagoIntegration
{
    // Only the connection fallback needs a stand-in for this standalone test.
    internal static class ArchipelagoManager
    {
        public static Dictionary<string, object> SlotData;
    }
}

public static class BuildingCompatibilityTests
{
    public static int Main()
    {
        var received = new HashSet<string> { "Blueprint: Gear Workshop" };
        Check(!ApBuildingLocations.IsTierUnlocked(2, received), "Forester cannot be skipped");
        received.Add("Blueprint: Forester");
        Check(ApBuildingLocations.IsTierUnlocked(2, received), "Gear production should unlock tier 2");
        // Tier 5 mirrors Rules.tier_blueprints: Folktails bots need Biofuel from the Refinery.
        Check(ApBuildingLocations.GetTierBlueprints(5, "Folktails").Contains("Refinery"), "FT tier 5 needs Refinery");
        Check(!ApBuildingLocations.GetTierBlueprints(5, "IronTeeth").Contains("Refinery"), "IT tier 5 has no Refinery");
        var tier5 = new HashSet<string>(ApBuildingLocations.GetTierBlueprints(5, "Folktails").Select(b => "Blueprint: " + b));
        tier5.Remove("Blueprint: Refinery");
        Check(!ApBuildingLocations.IsTierUnlocked(5, tier5, "Folktails"), "FT tier 5 stays locked without Refinery");
        tier5.Add("Blueprint: Refinery");
        Check(ApBuildingLocations.IsTierUnlocked(5, tier5, "Folktails"), "FT tier 5 opens with Refinery");
        Check(!ApBuildingLocations.HasBuildingPrerequisites("Dance Pit", received, "IronTeeth"), "Dance Pit needs Metalsmith");
        Check(ApBuildingLocations.HasBuildingPrerequisites("Arch of Progress", received, "IronTeeth"), "Other buildings do not inherit Dance Pit's gate");
        received.Add("Blueprint: Metalsmith");
        Check(ApBuildingLocations.HasBuildingPrerequisites("Dance Pit", received, "IronTeeth"), "Metalsmith satisfies Metal Parts");
        Check(ApBuildingLocations.GetEntries("Folktails").Count() == 133, "FT mappings include 132 items and wonder");
        Check(ApBuildingLocations.GetEntries("IronTeeth").Count() == 132, "IT mappings include 131 items and wonder");
        foreach (var faction in new[] { "Folktails", "IronTeeth" })
        {
            Check(ApBuildingLocations.TryGetTemplateName("Blueprint: Airlock", out var template, faction)
                  && template == "Airlock." + faction, "Airlock faction mapping");
            Check(ApBuildingLocations.TryGetTemplateName("Blueprint: Valve", out template, faction)
                  && template == "Valve." + faction, "Valve identity stays compatible");
        }
        Check(!ApBuildingLocations.TryGetTemplateName("Blueprint: Hall of Abundance", out _, "IronTeeth"), "FT monument cannot leak to IT");
        Check(!ApBuildingLocations.TryGetTemplateName("Blueprint: Arch of Progress", out _, "Folktails"), "IT monument cannot leak to FT");
        // Badwater gating (#1): Explosives and Extract consumers need the badwater chain.
        // Same sets as the APWorld test_start_items_shop.py.
        Check(Same(ApBuildingLocations.GetBuildingPrerequisites("Dynamite", "Folktails"),
                   "Badwater Pump", "Explosives Factory", "Smelter", "Scavenger Flag", "Gear Workshop", "Forester"),
              "FT Dynamite needs Badwater Pump and Explosives Factory");
        Check(Same(ApBuildingLocations.GetBuildingPrerequisites("Memory", "IronTeeth"),
                   "Metalsmith", "Deep Badwater Pump", "Centrifuge", "Smelter", "Gear Workshop", "Forester"),
              "IT Memory needs Deep Badwater Pump, Metalsmith and Centrifuge");
        Check(Same(ApBuildingLocations.GetBuildingPrerequisites("Tunnel", "Folktails"),
                   "Badwater Pump", "Explosives Factory", "Centrifuge", "Smelter", "Scavenger Flag", "Gear Workshop", "Forester"),
              "Tunnel needs Explosives and Extract");
        Check(ApBuildingLocations.GetBuildingPrerequisites("Refinery", "Folktails").Count == 0, "Refinery has an Extract-free recipe");
        foreach (var b in new[] { "Dynamite", "Double Dynamite", "Triple Dynamite", "Tunnel", "Detonator", "Memory",
                                  "Pole Banner", "Square Banner", "Agora", "Detailer" })
            Check(ApBuildingLocations.GetBuildingPrerequisites(b, "Folktails").Contains("Badwater Pump"), b + " needs badwater (FT)");
        foreach (var b in new[] { "Dynamite", "Tunnel", "Detonator", "Memory", "Pole Banner", "Square Banner", "Detailer",
                                  "Decontamination Pod", "Advanced Breeding Pod", "Grease Factory" })
            Check(ApBuildingLocations.GetBuildingPrerequisites(b, "IronTeeth").Contains("Deep Badwater Pump"), b + " needs badwater (IT)");
        var ftMetal = new HashSet<string> { "Blueprint: Forester", "Blueprint: Gear Workshop", "Blueprint: Smelter",
                                            "Blueprint: Scavenger Flag", "Blueprint: Explosives Factory" };
        Check(!ApBuildingLocations.HasBuildingPrerequisites("Dynamite", ftMetal, "Folktails"), "no badwater, no Dynamite");
        ftMetal.Add("Blueprint: Badwater Pump");
        Check(ApBuildingLocations.HasBuildingPrerequisites("Dynamite", ftMetal, "Folktails"), "Badwater Pump completes Dynamite");

        // Shop lock reasons: what is missing, never the item.
        var none = new HashSet<string>();
        Check(ApBuildingLocations.DescribeShopLock(1, "Bench", true, true, 30, 50, none) == "", "first T1 slot with science needs nothing");
        Check(ApBuildingLocations.DescribeShopLock(1, "Bench", true, true, 120, 50, none) == "Needs: 120 science", "science only");
        Check(ApBuildingLocations.DescribeShopLock(1, "Bench", false, false, 120, 50, none)
              == "Needs: previous check, Forester, 120 science", "previous check and Forester");
        var forester = new HashSet<string> { "Blueprint: Forester" };
        Check(ApBuildingLocations.DescribeShopLock(3, "Bench", false, true, 100, 500, forester, "Folktails")
              == "Needs: Gear Workshop, Scavenger Flag, Smelter", "tier 3 lists only missing blueprints");
        Check(ApBuildingLocations.DescribeShopLock(3, "Bench", false, true, 100, 500, forester, "IronTeeth")
              == "Needs: Gear Workshop, Smelter", "IT tier 3 has free scrap");
        Check(ApBuildingLocations.DescribeShopLock(1, "Agora", true, true, 100, 500, ftMetal, "Folktails")
              == "Needs: Centrifuge", "Agora needs Extract");
        Check(!ApBuildingLocations.DescribeShopLock(1, "Agora", true, true, 100, 500, none, "Folktails").Contains("Agora"),
              "the reason never names the slot's building");

        Console.WriteLine("PASS: Forester and Metalsmith gates, faction coverage and legacy Valve identity");
        Console.WriteLine("PASS: badwater prerequisites and shop lock reasons");
        return 0;
    }

    private static bool Same(List<string> actual, params string[] expected)
        => new HashSet<string>(actual).SetEquals(expected) && actual.Count == expected.Length;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
    }
}
