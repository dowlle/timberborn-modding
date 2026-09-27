using System;
using ArchipelagoIntegration;

// Standalone test executable; compile together with NeedTraps.cs.
// Values mirror Timberborn 1.1 Need.Beaver.Hunger / Need.Beaver.Thirst:
// minimum -3, maximum 1, effectiveness 1.
public static class NeedTrapsTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-5f;

    public static int Main()
    {
        // Trap name to need id; ids are the same for Folktails and Iron Teeth.
        Check(NeedTraps.TryGetNeedId("Hungry Beavers", out var hunger) && hunger == "Hunger", "hungry trap drains Hunger");
        Check(NeedTraps.TryGetNeedId("Thirsty Beavers", out var thirst) && thirst == "Thirst", "thirsty trap drains Thirst");
        Check(!NeedTraps.TryGetNeedId("Hazardous Weather", out var none) && none == null, "weather trap is not a need trap");

        // Selection: beavers with the need enabled; bots never have it.
        Check(NeedTraps.AppliesTo(true, true, "Beaver"), "beaver with the need is affected");
        Check(!NeedTraps.AppliesTo(false, true, "Beaver"), "manager without the need is skipped (bots)");
        Check(!NeedTraps.AppliesTo(true, true, "Bot"), "bot character type is skipped");
        Check(!NeedTraps.AppliesTo(true, false, "Beaver"), "disabled need is skipped");

        // Severity: critical (below 0) but well above the lethal minimum.
        float target = NeedTraps.TargetPoints(-3f, 1f);
        Check(Near(target, -0.5f), "target is -0.5 for vanilla range");
        Check(target < 0f, "target is in the critical state");
        Check(target - (-3f) >= 2.4f, "target keeps about three days before death at -0.8/day");

        // Full beaver: drop by 1.5 points.
        Check(NeedTraps.TryPlanDrop(1f, -3f, 1f, 1f, out var points, out var t1), "full need is lowered");
        Check(Near(points, -1.5f) && Near(t1, -0.5f), "effect moves 1 to -0.5");

        // Partly hungry beaver.
        Check(NeedTraps.TryPlanDrop(0.2f, -3f, 1f, 1f, out points, out _), "positive need is lowered");
        Check(Near(points, -0.7f), "effect moves 0.2 to -0.5");

        // Never helps a beaver that is already worse off.
        Check(!NeedTraps.TryPlanDrop(-0.5f, -3f, 1f, 1f, out points, out _) && points == 0f, "at target is unchanged");
        Check(!NeedTraps.TryPlanDrop(-2.5f, -3f, 1f, 1f, out points, out _) && points == 0f, "starving beaver is not raised");

        // Effectiveness scales instant effects in the game, so the plan divides by it.
        Check(NeedTraps.TryPlanDrop(1f, -3f, 1f, 0.5f, out points, out _) && Near(points, -3f), "effectiveness 0.5 doubles the effect");
        Check(!NeedTraps.TryPlanDrop(1f, -3f, 1f, 0f, out _, out _), "zero effectiveness cannot be driven");

        // A modded spec with a shallow range keeps the survival margin.
        float shallow = NeedTraps.TargetPoints(-1f, 1f);
        Check(Near(shallow, 0f), "shallow minimum keeps one point of margin");
        Check(!NeedTraps.TryPlanDrop(1f, -0.2f, 1f, 1f, out _, out var t2) || t2 > -0.2f, "never plans a lethal target");

        // Tally line.
        var tally = new NeedTraps.Tally { Lowered = 12, AlreadyCritical = 1, Skipped = 4, Failed = 0 };
        Check(tally.Describe() == "affected=12, alreadyCritical=1, skipped=4, failed=0", "tally line format");

        Console.WriteLine("NeedTrapsTests: all checks passed");
        return 0;
    }
}
