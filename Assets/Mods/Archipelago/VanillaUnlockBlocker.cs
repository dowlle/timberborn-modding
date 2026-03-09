using System.Reflection;
using Timberborn.Buildings;
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

        public VanillaUnlockBlocker(TemplateNameMapper templateNameMapper)
        {
            _templateNameMapper = templateNameMapper;
        }

        public void Load()
        {
            int blocked = 0;
            foreach (var entry in ApBuildingLocations.AllEntries)
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

            Debug.Log($"[Archipelago] Blocked vanilla science unlocks for {blocked} buildings");
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
