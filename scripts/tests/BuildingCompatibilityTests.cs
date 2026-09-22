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
        Console.WriteLine("PASS: Forester and Metalsmith gates, faction coverage and legacy Valve identity");
        return 0;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
    }
}
