using System;
using ArchipelagoIntegration;

// Standalone test executable; compile together with HazardSurvival.cs.
public static class HazardSurvivalTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static int Main()
    {
        Check(HazardSurvival.Drought == "DroughtWeather" && HazardSurvival.Badtide == "BadtideWeather",
              "hazard ids match the game's HazardousWeather ids");

        // Not bound to a slot: no counting, and comparisons see 0.
        int count = HazardSurvival.InitialCount(false, -1, 7, true);
        Check(count == HazardSurvival.NotTracking, "unbound save does not count");
        Check(HazardSurvival.AfterEnded(count) == HazardSurvival.NotTracking, "unbound save ignores ended hazards");
        Check(HazardSurvival.Survived(count) == 0, "unbound save has survived 0");

        // Newly bound save: hazards from before connecting never count.
        count = HazardSurvival.InitialCount(true, -1, 7, false);
        Check(count == 0, "new binding starts at 0");

        // Day 1 of cycle 1 rolled a drought: the rolled count is 1 but nothing survived yet.
        count = HazardSurvival.InitialCount(true, 1, 1, true);
        Check(count == 0, "rolled but not ended does not count");
        count = HazardSurvival.AfterEnded(count);
        Check(count == 1, "the drought counts when it ends");

        // Older client save: baseline 2, rolled total now 6, current cycle rolled this hazard.
        Check(HazardSurvival.InitialCount(true, 2, 6, true) == 3, "migration leaves out the current cycle's hazard");
        Check(HazardSurvival.InitialCount(true, 2, 6, false) == 4, "migration counts every ended hazard");
        Check(HazardSurvival.InitialCount(true, 5, 5, true) == 0, "migration never goes negative");

        Check(HazardSurvival.IsDrought("DroughtWeather") && !HazardSurvival.IsDrought("BadtideWeather"), "drought id");
        Check(HazardSurvival.IsBadtide("BadtideWeather") && !HazardSurvival.IsBadtide(null), "badtide id");

        Console.WriteLine("PASS: hazard ids, unbound saves, new binding, count on end, migration from baselines");
        return 0;
    }
}
