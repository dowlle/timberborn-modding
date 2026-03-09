using System;
using System.Collections.Generic;
using Timberborn.Buildings;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Receives items from the Archipelago server and unlocks the corresponding
    /// buildings in-game via BuildingUnlockingService.
    /// BuildingUnlockToolRefresher handles the toolbar UI refresh reactively
    /// via the BuildingUnlockedEvent → TryToUnlock event bridge.
    /// </summary>
    public class ApItemReceiver : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly BuildingUnlockingService _buildingUnlockingService;
        private readonly TemplateNameMapper _templateNameMapper;
        private readonly ArchipelagoSaveData _saveData;

        // AP item name → BuildingSpec for fast lookup on each received item
        private readonly Dictionary<string, BuildingSpec> _itemNameToSpec = new();

        // Tracks specs currently being AP-unlocked so other hooks can skip them
        private static readonly HashSet<BuildingSpec> _apUnlocking = new();
        public static bool IsApUnlock(BuildingSpec spec) => _apUnlocking.Contains(spec);

        public ApItemReceiver(
            BuildingUnlockingService buildingUnlockingService,
            TemplateNameMapper templateNameMapper,
            ArchipelagoSaveData saveData)
        {
            _buildingUnlockingService = buildingUnlockingService;
            _templateNameMapper = templateNameMapper;
            _saveData = saveData;
        }

        public void Load()
        {
            foreach (var entry in ApBuildingLocations.AllEntries)
            {
                var templateName = entry.Key;
                var locationName = entry.Value; // "Science: Forester"

                // Derive item name: "Science: X" → "Blueprint: X"
                var buildingName = locationName.Replace("Science: ", "");
                var itemName = $"Blueprint: {buildingName}";

                if (!_templateNameMapper.TryGetTemplate(templateName, out var template))
                {
                    Debug.LogWarning($"[Archipelago] Template not found for item receiver: {templateName}");
                    continue;
                }

                var spec = template.GetSpec<BuildingSpec>();
                if (spec == null)
                {
                    Debug.LogWarning($"[Archipelago] No BuildingSpec on template: {templateName}");
                    continue;
                }

                _itemNameToSpec[itemName] = spec;
            }

            Debug.Log($"[Archipelago] Item receiver ready — {_itemNameToSpec.Count} buildings mapped.");
            ArchipelagoManager.OnItemReceived += HandleItem;
        }

        public void Unload()
        {
            ArchipelagoManager.OnItemReceived -= HandleItem;
        }

        private void HandleItem(ApItem item)
        {
            // Track all received items for tier gate evaluation and save persistence
            _saveData.ReceivedItems.Add(item.ItemName);

            // Handle Skip items (branching shop)
            if (item.ItemName == "Skip")
            {
                _saveData.SkipsAvailable++;
                Debug.Log($"[Archipelago] Received Skip item (total: {_saveData.SkipsAvailable}) from {item.SenderName}");
                ArchipelagoManager.PostLogMessage($"Received Skip from {item.SenderName} (total: {_saveData.SkipsAvailable})");
                return;
            }

            if (_itemNameToSpec.TryGetValue(item.ItemName, out var spec))
            {
                _apUnlocking.Add(spec);
                try
                {
                    _buildingUnlockingService.UnlockIgnoringCost(spec);
                    Debug.Log($"[Archipelago] Unlocked building: {item.ItemName} (from {item.SenderName})");
                    ArchipelagoManager.PostLogMessage($"Received {item.ItemName} from {item.SenderName}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Archipelago] Unlock THREW for {item.ItemName}: {ex}");
                }
                finally
                {
                    _apUnlocking.Remove(spec);
                }
            }
            else
            {
                Debug.Log($"[Archipelago] Received non-blueprint item: {item.ItemName} (from {item.SenderName})");
                ArchipelagoManager.PostLogMessage($"Received {item.ItemName} from {item.SenderName}");
            }
        }

    }
}
