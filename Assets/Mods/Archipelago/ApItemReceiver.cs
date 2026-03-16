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
            string faction = ApBuildingLocations.GetFaction();
            foreach (var entry in ApBuildingLocations.GetEntries(faction))
            {
                var templateName = entry.Key;
                var buildingName = entry.Value;
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

            Debug.Log($"[Archipelago] Item receiver ready — {_itemNameToSpec.Count} {faction} buildings mapped.");
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

            // Handle progressive items — resolve to the next building in the chain
            if (_saveData.ProgressiveChains.TryGetValue(item.ItemName, out var chain))
            {
                if (!_saveData.ProgressiveCounters.ContainsKey(item.ItemName))
                    _saveData.ProgressiveCounters[item.ItemName] = 0;

                int idx = _saveData.ProgressiveCounters[item.ItemName];
                _saveData.ProgressiveCounters[item.ItemName]++;

                if (idx < chain.Count)
                {
                    var buildingName = chain[idx];
                    var blueprintName = $"Blueprint: {buildingName}";
                    // Also track the resolved blueprint name for any code checking specific buildings
                    _saveData.ReceivedItems.Add(blueprintName);

                    if (_itemNameToSpec.TryGetValue(blueprintName, out var progSpec))
                    {
                        _apUnlocking.Add(progSpec);
                        try
                        {
                            _buildingUnlockingService.UnlockIgnoringCost(progSpec);
                            Debug.Log($"[Archipelago] Progressive unlock: {item.ItemName} → {buildingName} (from {item.SenderName})");
                            ArchipelagoManager.PostLogMessage($"Received {item.ItemName} ({buildingName}) from {item.SenderName}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[Archipelago] Unlock THREW for {item.ItemName} → {buildingName}: {ex}");
                        }
                        finally
                        {
                            _apUnlocking.Remove(progSpec);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Archipelago] Progressive chain building not found: {blueprintName}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Archipelago] Progressive item {item.ItemName} received beyond chain length ({idx} >= {chain.Count})");
                }
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
                // Route non-blueprint items (filler, traps, boosts) to effect handler
                if (ApEffectHandler.Instance != null)
                {
                    ApEffectHandler.Instance.HandleEffect(item);
                }
                else
                {
                    Debug.Log($"[Archipelago] Received non-blueprint item: {item.ItemName} (from {item.SenderName})");
                    ArchipelagoManager.PostLogMessage($"Received {item.ItemName} from {item.SenderName}");
                }
            }
        }

    }
}
