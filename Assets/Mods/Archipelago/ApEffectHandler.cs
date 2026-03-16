using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Timberborn.EntitySystem;
using Timberborn.HazardousWeatherSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Handles non-blueprint AP item effects: traps, filler (resource injection),
    /// and boosts (permanent stat bonuses).
    /// </summary>
    public class ApEffectHandler : ILoadableSingleton, IUnloadableSingleton
    {
        internal static ApEffectHandler Instance { get; private set; }

        private readonly HazardousWeatherService _hazardousWeatherService;
        private readonly EntityComponentRegistry _entityComponentRegistry;
        private readonly ArchipelagoSaveData _saveData;

        // GoodId type resolved via reflection (from Timberborn.Goods)
        private Type _goodIdType;
        private bool _goodIdTypeSearched;

        // BonusManager + BonusId types resolved via reflection
        private Type _bonusManagerType;
        private Type _bonusIdType;
        private MethodInfo _addBonusMethod;
        private bool _bonusSystemSearched;

        // Filler: display name → game GoodId string
        private static readonly Dictionary<string, string> FillerGoodMapping = new()
        {
            { "Logs", "Log" },
            { "Planks", "Plank" },
            { "Gears", "Gear" },
            { "Bread", "Bread" },
            { "Metal Blocks", "MetalBlock" },
            { "Treated Planks", "TreatedPlank" },
            { "Scrap Metal", "ScrapMetal" },
        };

        // Boost: item suffix → (BonusId string to try, multiplier delta)
        // BonusId strings are best-guesses — will be verified at runtime
        private static readonly Dictionary<string, (string bonusId, float delta)> BoostMapping = new()
        {
            { "Faster Movement Speed",       ("MovementSpeed", 0.25f) },
            { "Increased Carrying Capacity",  ("CarryingCapacity", 0.50f) },
            { "Faster Working Speed",         ("WorkSpeed", 0.25f) },
            { "Faster Tree Growth",           ("TreeGrowth", 0.50f) },
            { "Longer Life Expectancy",       ("Longevity", 0.25f) },
            { "Better Woodcutting Chance",    ("WoodcuttingYield", 0.25f) },
        };

        public ApEffectHandler(
            HazardousWeatherService hazardousWeatherService,
            EntityComponentRegistry entityComponentRegistry,
            ArchipelagoSaveData saveData)
        {
            _hazardousWeatherService = hazardousWeatherService;
            _entityComponentRegistry = entityComponentRegistry;
            _saveData = saveData;
        }

        public void Load()
        {
            Instance = this;

            // Re-apply persisted boosts to all existing entities
            if (_saveData.ActiveBoosts.Count > 0)
            {
                Debug.Log($"[Archipelago] Re-applying {_saveData.ActiveBoosts.Count} active boosts");
                foreach (var boostName in _saveData.ActiveBoosts)
                    ApplyBoostToAllEntities(boostName);
            }
        }

        public void Unload()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Called by ApItemReceiver for non-blueprint items.
        /// </summary>
        public void HandleEffect(ApItem item)
        {
            if (item.ItemName.StartsWith("Trap: "))
                HandleTrap(item);
            else if (item.ItemName.StartsWith("Filler: "))
                HandleFiller(item);
            else if (item.ItemName.StartsWith("Boost: "))
                HandleBoost(item);
            else
                Debug.Log($"[Archipelago] Unknown effect item: {item.ItemName}");
        }

        // =================================================================
        // Traps
        // =================================================================

        private void HandleTrap(ApItem item)
        {
            var trapName = item.ItemName.Substring("Trap: ".Length);

            switch (trapName)
            {
                case "Early Drought":
                    TriggerHazardousWeather("drought");
                    break;
                case "Badwater Leak":
                    TriggerHazardousWeather("badtide");
                    break;
                case "Hungry Beavers":
                    TriggerHungryBeavers();
                    break;
                default:
                    Debug.LogWarning($"[Archipelago] Unknown trap: {trapName}");
                    break;
            }
        }

        private void TriggerHazardousWeather(string type)
        {
            try
            {
                // StartHazardousWeather triggers the next hazardous weather event
                var method = _hazardousWeatherService.GetType().GetMethod(
                    "StartHazardousWeather",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (method == null)
                {
                    Debug.LogWarning("[Archipelago] Could not find StartHazardousWeather method");
                    return;
                }

                method.Invoke(_hazardousWeatherService, null);
                Debug.Log($"[Archipelago] Triggered hazardous weather ({type})");
                ArchipelagoManager.PostLogMessage($"Trap activated: {type} incoming!");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Failed to trigger {type}: {ex.Message}");
                ArchipelagoManager.PostLogMessage($"Trap: {type} (failed to trigger)");
            }
        }

        private void TriggerHungryBeavers()
        {
            // Apply a food consumption multiplier to all current beavers
            // Uses the BonusSystem to double food consumption
            try
            {
                int affected = ApplyBonusToAllEntities("FoodConsumption", 1.0f);
                Debug.Log($"[Archipelago] Hungry Beavers: doubled food consumption on {affected} entities");
                ArchipelagoManager.PostLogMessage($"Trap activated: Hungry Beavers! ({affected} beavers affected)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Hungry Beavers trap failed: {ex.Message}");
                ArchipelagoManager.PostLogMessage("Trap: Hungry Beavers (failed to apply)");
            }
        }

        // =================================================================
        // Filler (resource injection)
        // =================================================================

        private void HandleFiller(ApItem item)
        {
            // Parse "Filler: 50 Logs" → amount=50, goodDisplayName="Logs"
            var fillerText = item.ItemName.Substring("Filler: ".Length);
            var match = Regex.Match(fillerText, @"^(\d+)\s+(.+)$");
            if (!match.Success)
            {
                Debug.LogWarning($"[Archipelago] Could not parse filler: {item.ItemName}");
                return;
            }

            int amount = int.Parse(match.Groups[1].Value);
            string displayName = match.Groups[2].Value;

            if (!FillerGoodMapping.TryGetValue(displayName, out var goodIdStr))
            {
                Debug.LogWarning($"[Archipelago] Unknown filler good: {displayName}");
                return;
            }

            try
            {
                InjectGoods(goodIdStr, amount);
                Debug.Log($"[Archipelago] Injected {amount} {goodIdStr} into stockpiles");
                ArchipelagoManager.PostLogMessage($"Received {amount} {displayName}!");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Failed to inject {amount} {goodIdStr}: {ex.Message}");
                ArchipelagoManager.PostLogMessage($"Received {amount} {displayName} (delivery failed)");
            }
        }

        private void InjectGoods(string goodIdStr, int amount)
        {
            // Resolve GoodId type via reflection
            if (!_goodIdTypeSearched)
            {
                _goodIdTypeSearched = true;
                _goodIdType = Type.GetType("Timberborn.Goods.GoodId, Timberborn.Goods");
                if (_goodIdType == null)
                {
                    // Try loading from any loaded assembly
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        _goodIdType = asm.GetType("Timberborn.Goods.GoodId");
                        if (_goodIdType != null) break;
                    }
                }
            }

            if (_goodIdType == null)
            {
                Debug.LogWarning("[Archipelago] Could not find GoodId type for goods injection");
                return;
            }

            var goodId = Activator.CreateInstance(_goodIdType, goodIdStr);

            // Find Inventory components on stockpile entities and inject goods
            // Look for entities with Inventory component that accepts this good type
            var inventoryType = FindType("Timberborn.InventorySystem.Inventory");
            if (inventoryType == null)
            {
                Debug.LogWarning("[Archipelago] Could not find Inventory type");
                return;
            }

            // Use EntityComponentRegistry to find all inventories
            object inventories = null;
            var genericMethod = _entityComponentRegistry.GetType().GetMethod("GetEnabled");
            if (genericMethod != null && genericMethod.IsGenericMethod)
            {
                inventories = genericMethod.MakeGenericMethod(inventoryType)
                    .Invoke(_entityComponentRegistry, null);
            }
            else
            {
                var nonGeneric = _entityComponentRegistry.GetType()
                    .GetMethod("GetEnabled", new[] { typeof(Type) });
                if (nonGeneric != null)
                    inventories = nonGeneric.Invoke(_entityComponentRegistry, new object[] { inventoryType });
            }

            if (inventories == null)
            {
                Debug.LogWarning("[Archipelago] Could not enumerate Inventory entities");
                return;
            }

            var enumerator = ((System.Collections.IEnumerable)inventories).GetEnumerator();

            int remaining = amount;
            while (enumerator.MoveNext() && remaining > 0)
            {
                var inventory = enumerator.Current;

                // Try GiveIgnoringCapacity(goodId, amount)
                var giveMethod = inventory.GetType().GetMethod("GiveIgnoringCapacity");
                if (giveMethod == null) continue;

                try
                {
                    // Check if this inventory accepts this good type
                    var givesMethod = inventory.GetType().GetMethod("Gives");
                    if (givesMethod != null)
                    {
                        bool gives = (bool)givesMethod.Invoke(inventory, new[] { goodId });
                        if (!gives) continue;
                    }

                    giveMethod.Invoke(inventory, new object[] { goodId, remaining });
                    remaining = 0; // Assume GiveIgnoringCapacity takes all
                }
                catch
                {
                    // This inventory can't accept this good, try next
                }
            }

            if (remaining > 0)
                Debug.LogWarning($"[Archipelago] Could not inject all goods: {remaining} remaining");
        }

        // =================================================================
        // Boosts
        // =================================================================

        private void HandleBoost(ApItem item)
        {
            var boostName = item.ItemName.Substring("Boost: ".Length);

            if (!BoostMapping.ContainsKey(boostName))
            {
                Debug.LogWarning($"[Archipelago] Unknown boost: {boostName}");
                ArchipelagoManager.PostLogMessage($"Received boost: {boostName} (effect not yet implemented)");
                return;
            }

            // Persist the boost
            _saveData.ActiveBoosts.Add(boostName);

            // Apply to all current entities
            ApplyBoostToAllEntities(boostName);

            ArchipelagoManager.PostLogMessage($"Boost activated: {boostName}!");
            Debug.Log($"[Archipelago] Boost activated: {boostName}");
        }

        private void ApplyBoostToAllEntities(string boostName)
        {
            if (!BoostMapping.TryGetValue(boostName, out var mapping))
                return;

            int affected = ApplyBonusToAllEntities(mapping.bonusId, mapping.delta);
            Debug.Log($"[Archipelago] Applied boost '{boostName}' to {affected} entities");
        }

        /// <summary>
        /// Apply a bonus to all entities that have a BonusManager component.
        /// </summary>
        private int ApplyBonusToAllEntities(string bonusIdStr, float multiplierDelta)
        {
            EnsureBonusSystemResolved();

            if (_bonusManagerType == null || _addBonusMethod == null || _bonusIdType == null)
            {
                Debug.LogWarning("[Archipelago] BonusSystem not resolved — cannot apply bonus");
                return 0;
            }

            // Construct BonusId
            object bonusId;
            try
            {
                bonusId = Activator.CreateInstance(_bonusIdType, bonusIdStr);
            }
            catch
            {
                Debug.LogWarning($"[Archipelago] Could not construct BonusId('{bonusIdStr}')");
                return 0;
            }

            // Find all entities with BonusManager
            // EntityComponentRegistry.GetEnabled may be generic or take a Type param
            MethodInfo getEnabledMethod = null;
            object managers = null;

            // Try generic version first
            var genericMethod = _entityComponentRegistry.GetType().GetMethod("GetEnabled");
            if (genericMethod != null && genericMethod.IsGenericMethod)
            {
                getEnabledMethod = genericMethod.MakeGenericMethod(_bonusManagerType);
                managers = getEnabledMethod.Invoke(_entityComponentRegistry, null);
            }
            else
            {
                // Try non-generic version with Type parameter
                getEnabledMethod = _entityComponentRegistry.GetType()
                    .GetMethod("GetEnabled", new[] { typeof(Type) });
                if (getEnabledMethod != null)
                    managers = getEnabledMethod.Invoke(_entityComponentRegistry, new object[] { _bonusManagerType });
            }

            if (managers == null)
            {
                Debug.LogWarning("[Archipelago] Could not enumerate BonusManager entities");
                return 0;
            }
            int count = 0;

            foreach (var manager in (System.Collections.IEnumerable)managers)
            {
                try
                {
                    _addBonusMethod.Invoke(manager, new[] { bonusId, (object)multiplierDelta });
                    count++;
                }
                catch
                {
                    // This entity may not support this bonus type — skip
                }
            }

            return count;
        }

        private void EnsureBonusSystemResolved()
        {
            if (_bonusSystemSearched) return;
            _bonusSystemSearched = true;

            _bonusManagerType = FindType("Timberborn.BonusSystem.BonusManager");
            _bonusIdType = FindType("Timberborn.BonusSystem.BonusId");

            if (_bonusManagerType != null)
            {
                _addBonusMethod = _bonusManagerType.GetMethod("AddBonus",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            Debug.Log($"[Archipelago] BonusSystem resolved: Manager={_bonusManagerType != null}, " +
                      $"Id={_bonusIdType != null}, AddBonus={_addBonusMethod != null}");
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }
    }
}
