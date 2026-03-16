using System;
using System.Reflection;
using Timberborn.Buildings;
using Timberborn.FactionSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Prevents players from unlocking AP-managed buildings through the vanilla
    /// science unlock UI.  Sets ScienceCost to int.MaxValue for every AP building
    /// at game load time.  AP-driven unlocks use UnlockIgnoringCost and are
    /// unaffected by the inflated cost.
    /// </summary>
    public class VanillaUnlockBlocker : ILoadableSingleton
    {
        private readonly TemplateNameMapper _templateNameMapper;
        private readonly FactionUnlockingService _factionUnlockingService;

        public VanillaUnlockBlocker(TemplateNameMapper templateNameMapper,
                                    FactionUnlockingService factionUnlockingService)
        {
            _templateNameMapper = templateNameMapper;
            _factionUnlockingService = factionUnlockingService;
        }

        public void Load()
        {
            // Auto-unlock all factions so players don't need vanilla well-being 8
            // to access Iron Teeth for AP games
            try
            {
                _factionUnlockingService.UnlockAllFactions();
                Debug.Log("[Archipelago] All factions unlocked for Archipelago play");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Could not auto-unlock factions: {ex.Message}");
            }

            string faction = ApBuildingLocations.GetFaction();
            int blocked = 0;
            foreach (var entry in ApBuildingLocations.GetEntries(faction))
            {
                var templateName = entry.Key;
                if (!_templateNameMapper.TryGetTemplate(templateName, out var template))
                {
                    Debug.LogWarning($"[Archipelago] VanillaUnlockBlocker: template '{templateName}' not found");
                    continue;
                }

                var spec = template.GetSpec<BuildingSpec>();
                if (spec == null) continue;

                if (TrySetScienceCost(spec, int.MaxValue))
                    blocked++;
            }

            Debug.Log($"[Archipelago] Blocked vanilla science unlocks for {blocked} {faction} buildings");
        }

        private static FieldInfo _scienceCostField;
        private static bool _fieldSearched;

        private static bool TrySetScienceCost(BuildingSpec spec, int value)
        {
            if (!_fieldSearched)
            {
                _fieldSearched = true;
                // Try common backing field names
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                _scienceCostField = typeof(BuildingSpec).GetField("_scienceCost", flags)
                    ?? typeof(BuildingSpec).GetField("<ScienceCost>k__BackingField", flags);

                if (_scienceCostField == null)
                {
                    Debug.LogWarning("[Archipelago] VanillaUnlockBlocker: could not find ScienceCost backing field. " +
                                     "Vanilla unlock blocking will not work.");
                }
            }

            if (_scienceCostField == null) return false;

            _scienceCostField.SetValue(spec, value);
            return true;
        }
    }
}
