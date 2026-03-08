using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.Buildings;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Receives items from the Archipelago server and unlocks the corresponding
    /// buildings in-game via BuildingUnlockingService.
    /// After unlocking, forces all toolbar buttons to re-evaluate their lock state
    /// so the UI updates immediately (BuildingToolLocker doesn't listen to events).
    /// </summary>
    public class ApItemReceiver : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly BuildingUnlockingService _buildingUnlockingService;
        private readonly ToolUnlockingService _toolUnlockingService;
        private readonly ToolButtonService _toolButtonService;
        private readonly TemplateNameMapper _templateNameMapper;
        private readonly ArchipelagoSaveData _saveData;

        // AP item name → BuildingSpec for fast lookup on each received item
        private readonly Dictionary<string, BuildingSpec> _itemNameToSpec = new();

        // Cached reflection access to ToolButtonService._toolToButtonMap
        // (contains ALL tool buttons including building buttons nested in groups)
        private FieldInfo _toolToButtonMapField;

        // Tracks specs currently being AP-unlocked so other hooks can skip them
        private static readonly HashSet<BuildingSpec> _apUnlocking = new();
        public static bool IsApUnlock(BuildingSpec spec) => _apUnlocking.Contains(spec);

        public ApItemReceiver(
            BuildingUnlockingService buildingUnlockingService,
            ToolUnlockingService toolUnlockingService,
            ToolButtonService toolButtonService,
            TemplateNameMapper templateNameMapper,
            ArchipelagoSaveData saveData)
        {
            _buildingUnlockingService = buildingUnlockingService;
            _toolUnlockingService = toolUnlockingService;
            _toolButtonService = toolButtonService;
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

            // Cache reflection field for toolbar refresh
            _toolToButtonMapField = typeof(ToolButtonService).GetField(
                "_toolToButtonMap", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_toolToButtonMapField == null)
                Debug.LogWarning("[Archipelago] Could not find _toolToButtonMap field — toolbar won't refresh on unlock.");

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

            if (_itemNameToSpec.TryGetValue(item.ItemName, out var spec))
            {
                _apUnlocking.Add(spec);
                try
                {
                    _buildingUnlockingService.UnlockIgnoringCost(spec);
                    Debug.Log($"[Archipelago] Unlocked building: {item.ItemName} (from {item.SenderName})");
                    RefreshToolbarLockStates();
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
            }
        }

        private void RefreshToolbarLockStates()
        {
            // Access _toolToButtonMap via reflection — it contains ALL tool buttons
            // including building buttons nested in tool groups. The public ToolButtons
            // property only returns top-level category buttons.
            if (_toolToButtonMapField == null) return;

            var map = _toolToButtonMapField.GetValue(_toolButtonService) as IDictionary;
            if (map == null) return;

            foreach (var key in map.Keys)
            {
                if (key is ITool tool)
                    _toolUnlockingService.LockIfNeeded(tool);
            }
        }
    }
}
