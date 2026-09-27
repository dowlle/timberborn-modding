namespace ArchipelagoIntegration
{
    /// <summary>
    /// Rules for the Hungry Beavers and Thirsty Beavers traps.
    ///
    /// Timberborn 1.1 defines one "Hunger" and one "Thirst" need
    /// (Needs/Need.Beaver.*.blueprint.json, CharacterType "Beaver") and both
    /// factions load them from NeedCollection.Common, so the ids do not depend on
    /// the faction. Bots get bot needs only (NeedManager.GetNeeds picks
    /// GetBotNeeds when the entity has a BotSpec), so they never have either need.
    ///
    /// Both needs range from -3 (MinimumValue) to 1 and are critical below 0.
    /// A beaver dies when a critical need reaches its minimum
    /// (MortalNeeder listens to NeedChangedIsAtMinimumState). The trap drops the
    /// need to CriticalPoints: the beaver is in the critical state at once, and at
    /// the vanilla decay rates (-0.8 and -0.7 per day) it has about three days to
    /// eat or drink before it dies. Beavers already at or below that level are not
    /// changed, so the trap never helps a beaver.
    ///
    /// Pure logic without Unity or Timberborn types so it can be contract-tested.
    /// </summary>
    public static class NeedTraps
    {
        public const string HungerNeedId = "Hunger";
        public const string ThirstNeedId = "Thirst";
        public const string BeaverCharacterType = "Beaver";

        /// <summary>Need points after the trap: critical (below 0), far from death.</summary>
        public const float CriticalPoints = -0.5f;

        /// <summary>Minimum distance to the lethal minimum kept for modded specs.</summary>
        public const float MinimumSurvivalMargin = 1f;

        /// <summary>Maps a trap name (without "Trap: ") to the need it drains.</summary>
        public static bool TryGetNeedId(string trapName, out string needId)
        {
            switch (trapName)
            {
                case "Hungry Beavers": needId = HungerNeedId; return true;
                case "Thirsty Beavers": needId = ThirstNeedId; return true;
                default: needId = null; return false;
            }
        }

        /// <summary>True when the trap should act on this need of this character.</summary>
        public static bool AppliesTo(bool hasNeed, bool needEnabled, string characterType)
        {
            return hasNeed && needEnabled && characterType == BeaverCharacterType;
        }

        /// <summary>
        /// Target points for a need with the given range: CriticalPoints, kept at
        /// least MinimumSurvivalMargin above the minimum and not above the maximum.
        /// </summary>
        public static float TargetPoints(float minimum, float maximum)
        {
            float target = CriticalPoints;
            float floor = minimum + MinimumSurvivalMargin;
            if (target < floor) target = floor;
            if (target > maximum) target = maximum;
            return target;
        }

        /// <summary>
        /// Decides the instant-effect points that move the need from current to the
        /// target. The game multiplies instant effects by NeedSpec.Effectiveness,
        /// so the delta is divided by it. Returns false when the need is already at
        /// or below the target, or when the spec cannot be driven by an effect.
        /// </summary>
        public static bool TryPlanDrop(float current, float minimum, float maximum, float effectiveness,
                                       out float effectPoints, out float target)
        {
            target = TargetPoints(minimum, maximum);
            effectPoints = 0f;
            if (!(effectiveness > 0f)) return false;
            if (!(current > target)) return false;
            if (!(target > minimum)) return false;
            effectPoints = (target - current) / effectiveness;
            return effectPoints < 0f;
        }

        /// <summary>Per-trap counters, logged as one line.</summary>
        public struct Tally
        {
            public int Lowered;
            public int AlreadyCritical;
            public int Skipped;
            public int Failed;

            public string Describe()
            {
                return $"affected={Lowered}, alreadyCritical={AlreadyCritical}, skipped={Skipped}, failed={Failed}";
            }
        }
    }
}
