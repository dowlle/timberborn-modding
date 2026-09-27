using System;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Counting rules for survived droughts and badtides (survival milestones and
    /// the Droughts and Badtides goals).
    ///
    /// A hazard counts when it ends (HazardousWeatherEndedEvent), so a check means the
    /// colony lived through it. The game's own HazardousWeatherHistory counts a hazard
    /// when the cycle's weather is rolled on day 1, before it happens, and it includes
    /// hazards from before the save was connected to Archipelago. These counts start at
    /// 0 when the save is bound to a slot and only grow while it is.
    ///
    /// Pure logic without Unity or game types so it can be contract-tested.
    /// </summary>
    public static class HazardSurvival
    {
        public const string Drought = "DroughtWeather";
        public const string Badtide = "BadtideWeather";

        /// <summary>Stored value meaning "not counting yet": the save is not bound to a slot.</summary>
        public const int NotTracking = -1;

        /// <summary>
        /// Starting count for a save without a stored count.
        /// A save not bound to a slot does not count. A newly bound save starts at 0.
        /// A save from an older client version has a baseline (the game's rolled count
        /// when it was first connected) but no survived count: every hazard rolled since
        /// then has ended, except the current cycle's own hazard, which has not.
        /// </summary>
        public static int InitialCount(bool boundToSlot, int baseline, int rolledTotal,
                                       bool currentCycleIsThisHazard)
        {
            if (!boundToSlot) return NotTracking;
            if (baseline < 0) return 0;
            return Math.Max(0, rolledTotal - baseline - (currentCycleIsThisHazard ? 1 : 0));
        }

        /// <summary>Count after a hazard of this type ended.</summary>
        public static int AfterEnded(int count) => count < 0 ? count : count + 1;

        /// <summary>Count for comparisons; a save that does not count yet has survived 0.</summary>
        public static int Survived(int count) => Math.Max(0, count);

        public static bool IsDrought(string hazardId) => hazardId == Drought;
        public static bool IsBadtide(string hazardId) => hazardId == Badtide;
    }
}
