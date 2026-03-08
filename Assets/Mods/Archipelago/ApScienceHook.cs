using System.Collections.Generic;
using Timberborn.Buildings;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Subscribes to BuildingUnlockedEvent and sends the corresponding AP location check.
    ///
    /// At Load() we walk every template name we care about, retrieve the associated
    /// BuildingSpec object reference via TemplateNameMapper.GetSpec, and store it in a
    /// reverse dictionary.  The event handler is then just a dictionary lookup.
    /// </summary>
    public class ApScienceHook : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly EventBus          _eventBus;
        private readonly TemplateNameMapper _templateNameMapper;

        // BuildingSpec object reference → AP location name
        private readonly Dictionary<BuildingSpec, string> _specToLocation = new();

        public ApScienceHook(EventBus eventBus, TemplateNameMapper templateNameMapper)
        {
            _eventBus           = eventBus;
            _templateNameMapper = templateNameMapper;
        }

        public void Load()
        {
            // Build the reverse map once at scene load.
            // TemplateNameMapper.TryGetTemplate gives us the TemplateSpec for a named template.
            // TemplateSpec.GetSpec<T>() then fetches the typed ComponentSpec from that template.
            foreach (var entry in ApBuildingLocations.AllEntries)
            {
                var templateName = entry.Key;
                var locationName = entry.Value;

                if (!_templateNameMapper.TryGetTemplate(templateName, out var template))
                {
                    Debug.LogWarning($"[Archipelago] Template not found: {templateName}");
                    continue;
                }

                var spec = template.GetSpec<BuildingSpec>();
                if (spec == null)
                {
                    Debug.LogWarning($"[Archipelago] No BuildingSpec on template: {templateName}");
                    continue;
                }

                _specToLocation[spec] = locationName;
            }

            Debug.Log($"[Archipelago] Science hook ready — {_specToLocation.Count} buildings mapped.");
            _eventBus.Register(this);
        }

        public void Unload() => _eventBus.Unregister(this);

        [OnEvent]
        public void OnBuildingUnlocked(BuildingUnlockedEvent e)
        {
            if (!ArchipelagoManager.IsConnected)
                return;

            if (!_specToLocation.TryGetValue(e.BuildingSpec, out var locationName))
            {
                // Not a Folktails AP location (IronTeeth building, free building, etc.)
                return;
            }

            Debug.Log($"[Archipelago] Science check: {locationName}");
            ArchipelagoManager.SendLocationCheck(locationName);
        }
    }
}
