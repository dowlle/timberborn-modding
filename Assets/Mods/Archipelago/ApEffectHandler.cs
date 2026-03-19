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

        // Weather scheduling state
        private readonly Queue<string> _weatherTrapQueue = new();
        private object _weatherService; // WeatherService resolved via reflection
        private PropertyInfo _isHazardousWeatherProp;
        private PropertyInfo _cycleDayProp;
        private bool _weatherServiceSearched;

        // Pending weather: scheduled with 3 in-game day notice
        private string _pendingWeatherType;
        private int _pendingWeatherStartDay = -1;
        private const int WEATHER_NOTICE_DAYS = 3;

        // GoodId type resolved via reflection (from Timberborn.Goods)
        private Type _goodIdType;
        private bool _goodIdTypeSearched;

        // BonusManager resolved via reflection
        private Type _bonusManagerType;
        private MethodInfo _addBonusMethod;
        private bool _bonusSystemSearched;

        // NeedManager resolved via reflection
        private Type _needManagerType;
        private MethodInfo _getNeedMethod;   // NeedManager.GetNeed(string)
        private MethodInfo _setPointsMethod; // Need.SetPoints(float)
        private bool _needSystemSearched;

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

        // Boost: item suffix → (BonusId string, multiplier delta)
        // BonusId strings match BonusTypeSpec.Id from game blueprint JSON specs
        private static readonly Dictionary<string, (string bonusId, float delta)> BoostMapping = new()
        {
            { "Faster Movement Speed",       ("MovementSpeed", 0.25f) },
            { "Increased Carrying Capacity",  ("CarryingCapacity", 0.50f) },
            { "Faster Working Speed",         ("WorkingSpeed", 0.25f) },
            { "Faster Tree Growth",           ("GrowthSpeed", 0.50f) },
            { "Longer Life Expectancy",       ("LifeExpectancy", 0.25f) },
            { "Better Woodcutting Chance",    ("CuttingSuccessChance", 0.25f) },
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
            DiscoverBonusIds();

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
                case "Thirsty Beavers":
                    TriggerThirstyBeavers();
                    break;
                default:
                    Debug.LogWarning($"[Archipelago] Unknown trap: {trapName}");
                    break;
            }
        }

        private void TriggerHazardousWeather(string type)
        {
            // Check if hazardous weather is already active or pending (scheduled but not yet started)
            if (IsHazardousWeatherActive() || _pendingWeatherType != null)
            {
                // Read trap_mode from slot data: 0=queue, 1=skip
                int trapMode = 0;
                if (ArchipelagoManager.SlotData != null
                    && ArchipelagoManager.SlotData.TryGetValue("trap_mode", out var modeObj))
                {
                    int.TryParse(modeObj?.ToString() ?? "0", out trapMode);
                }

                if (trapMode == 1)
                {
                    Debug.Log($"[Archipelago] Weather trap '{type}' skipped (weather active, trap_mode=skip)");
                    ArchipelagoManager.PostLogMessage($"Trap skipped: {type} (weather already active)");
                    return;
                }

                _weatherTrapQueue.Enqueue(type);
                Debug.Log($"[Archipelago] Weather trap '{type}' queued (weather active, queue size: {_weatherTrapQueue.Count})");
                ArchipelagoManager.PostLogMessage($"Trap queued: {type} (weather already active)");
                return;
            }

            ScheduleHazardousWeather(type);
        }

        /// <summary>
        /// Schedules hazardous weather with a 3 in-game day notice period.
        /// Sets CurrentCycleHazardousWeather immediately (so the game knows what's
        /// coming), then waits for WEATHER_NOTICE_DAYS before calling
        /// StartHazardousWeather(). The AP event log announces the incoming weather.
        /// </summary>
        private void ScheduleHazardousWeather(string type)
        {
            try
            {
                // Set the weather type on HazardousWeatherService so the game
                // knows which hazard is coming
                var serviceType = _hazardousWeatherService.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                string fieldName = type == "badtide" ? "_badtideWeather" : "_droughtWeather";
                var weatherField = serviceType.GetField(fieldName, flags);
                if (weatherField != null)
                {
                    var weatherInstance = weatherField.GetValue(_hazardousWeatherService);
                    var setCurrent = serviceType.GetProperty("CurrentCycleHazardousWeather", flags);
                    if (setCurrent != null && weatherInstance != null)
                    {
                        setCurrent.SetValue(_hazardousWeatherService, weatherInstance);
                    }
                }

                // Get current in-game day to schedule the start
                int currentDay = GetCurrentCycleDay();
                if (currentDay >= 0)
                {
                    _pendingWeatherType = type;
                    _pendingWeatherStartDay = currentDay + WEATHER_NOTICE_DAYS;
                    string weatherName = type == "badtide" ? "Badtide" : "Drought";
                    Debug.Log($"[Archipelago] Scheduled {type} to start on day {_pendingWeatherStartDay} (current: {currentDay}, notice: {WEATHER_NOTICE_DAYS} days)");
                    ArchipelagoManager.PostLogMessage($"WARNING: {weatherName} approaching in {WEATHER_NOTICE_DAYS} days! Prepare your colony!");
                }
                else
                {
                    // Fallback: can't read cycle day, trigger immediately
                    Debug.LogWarning("[Archipelago] Could not read CycleDay — triggering weather immediately");
                    ForceStartHazardousWeather(type);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Failed to schedule {type}: {ex.Message}");
                // Fallback to immediate
                ForceStartHazardousWeather(type);
            }
        }

        /// <summary>
        /// Force-starts hazardous weather immediately (fallback when scheduling fails,
        /// or called by ProcessWeatherQueue when notice period expires).
        /// </summary>
        private void ForceStartHazardousWeather(string type)
        {
            try
            {
                var serviceType = _hazardousWeatherService.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                var startMethod = serviceType.GetMethod("StartHazardousWeather", flags);
                if (startMethod == null)
                {
                    Debug.LogWarning("[Archipelago] Could not find StartHazardousWeather method");
                    return;
                }

                startMethod.Invoke(_hazardousWeatherService, null);
                string weatherName = type == "badtide" ? "Badtide" : "Drought";
                Debug.Log($"[Archipelago] Triggered hazardous weather ({type})");
                ArchipelagoManager.PostLogMessage($"The {weatherName} has begun!");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Failed to trigger {type}: {ex.Message}");
                ArchipelagoManager.PostLogMessage($"Trap: {type} (failed to trigger)");
            }
        }

        private int GetCurrentCycleDay()
        {
            EnsureWeatherServiceResolved();
            if (_weatherService == null || _cycleDayProp == null) return -1;
            try
            {
                return (int)_cycleDayProp.GetValue(_weatherService);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Called each tick by ArchipelagoTicker. Handles two things:
        /// 1. Pending scheduled weather: fires when the notice period expires
        /// 2. Queued weather traps: fires next queued trap when weather ends
        /// </summary>
        public void ProcessWeatherQueue()
        {
            // Check if a scheduled weather's notice period has expired
            if (_pendingWeatherType != null)
            {
                int currentDay = GetCurrentCycleDay();
                if (currentDay >= _pendingWeatherStartDay)
                {
                    var type = _pendingWeatherType;
                    _pendingWeatherType = null;
                    _pendingWeatherStartDay = -1;
                    Debug.Log($"[Archipelago] Weather notice period expired on day {currentDay} — starting {type}");
                    ForceStartHazardousWeather(type);
                }
                return; // Don't dequeue while a scheduled weather is pending
            }

            // Process queued weather traps when no weather is active
            if (_weatherTrapQueue.Count == 0) return;
            if (IsHazardousWeatherActive()) return;

            var next = _weatherTrapQueue.Dequeue();
            Debug.Log($"[Archipelago] Dequeuing weather trap: {next} (remaining: {_weatherTrapQueue.Count})");
            ScheduleHazardousWeather(next);
        }

        private bool IsHazardousWeatherActive()
        {
            EnsureWeatherServiceResolved();
            if (_weatherService == null || _isHazardousWeatherProp == null)
                return false;

            try
            {
                return (bool)_isHazardousWeatherProp.GetValue(_weatherService);
            }
            catch
            {
                return false;
            }
        }

        private void EnsureWeatherServiceResolved()
        {
            if (_weatherServiceSearched) return;
            _weatherServiceSearched = true;

            // WeatherService is not directly injected — find it via the HazardousWeatherService's
            // containing service or via EntityComponentRegistry
            var weatherServiceType = FindType("Timberborn.WeatherSystem.WeatherService");
            if (weatherServiceType == null)
            {
                Debug.LogWarning("[Archipelago] Could not find WeatherService type");
                return;
            }

            _isHazardousWeatherProp = weatherServiceType.GetProperty("IsHazardousWeather",
                BindingFlags.Public | BindingFlags.Instance);
            _cycleDayProp = weatherServiceType.GetProperty("CycleDay",
                BindingFlags.Public | BindingFlags.Instance);

            // Try to find the WeatherService instance — it should be a singleton in the game scene
            // Look for it on any GameObject
            _weatherService = UnityEngine.Object.FindFirstObjectByType(weatherServiceType);

            Debug.Log($"[Archipelago] WeatherService resolved: instance={_weatherService != null}, " +
                      $"IsHazardousWeather={_isHazardousWeatherProp != null}, CycleDay={_cycleDayProp != null}");
        }

        private void TriggerHungryBeavers()
        {
            try
            {
                // Set Hunger need to -0.5 (critical state, range is -3.0 to 1.0)
                int affected = SetNeedOnAllBeavers("Hunger", -0.5f);
                Debug.Log($"[Archipelago] Hungry Beavers: set hunger to critical on {affected} entities");
                ArchipelagoManager.PostLogMessage($"Trap activated: Hungry Beavers! ({affected} beavers affected)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Hungry Beavers trap failed: {ex.Message}");
                ArchipelagoManager.PostLogMessage("Trap: Hungry Beavers (failed to apply)");
            }
        }

        private void TriggerThirstyBeavers()
        {
            try
            {
                // Set Thirst need to -0.5 (critical state, range is -3.0 to 1.0)
                int affected = SetNeedOnAllBeavers("Thirst", -0.5f);
                Debug.Log($"[Archipelago] Thirsty Beavers: set thirst to critical on {affected} entities");
                ArchipelagoManager.PostLogMessage($"Trap activated: Thirsty Beavers! ({affected} beavers affected)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Thirsty Beavers trap failed: {ex.Message}");
                ArchipelagoManager.PostLogMessage("Trap: Thirsty Beavers (failed to apply)");
            }
        }

        /// <summary>
        /// Sets a need to a specific point value on all beavers via NeedManager.
        /// </summary>
        private int SetNeedOnAllBeavers(string needId, float points)
        {
            EnsureNeedSystemResolved();

            if (_needManagerType == null || _getNeedMethod == null || _setPointsMethod == null)
            {
                Debug.LogWarning("[Archipelago] NeedSystem not resolved — cannot set need");
                return 0;
            }

            object managers = null;
            var genericMethod = _entityComponentRegistry.GetType().GetMethod("GetEnabled");
            if (genericMethod != null && genericMethod.IsGenericMethod)
            {
                managers = genericMethod.MakeGenericMethod(_needManagerType)
                    .Invoke(_entityComponentRegistry, null);
            }
            else
            {
                var nonGeneric = _entityComponentRegistry.GetType()
                    .GetMethod("GetEnabled", new[] { typeof(Type) });
                if (nonGeneric != null)
                    managers = nonGeneric.Invoke(_entityComponentRegistry, new object[] { _needManagerType });
            }

            if (managers == null)
            {
                Debug.LogWarning("[Archipelago] Could not enumerate NeedManager entities");
                return 0;
            }

            int count = 0;
            foreach (var manager in (System.Collections.IEnumerable)managers)
            {
                try
                {
                    var need = _getNeedMethod.Invoke(manager, new object[] { needId });
                    if (need != null)
                    {
                        _setPointsMethod.Invoke(need, new object[] { points });
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Archipelago] SetNeed('{needId}') failed: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            return count;
        }

        private void EnsureNeedSystemResolved()
        {
            if (_needSystemSearched) return;
            _needSystemSearched = true;

            _needManagerType = FindType("Timberborn.NeedSystem.NeedManager");
            var needType = FindType("Timberborn.NeedSystem.Need");

            if (_needManagerType != null)
            {
                _getNeedMethod = _needManagerType.GetMethod("GetNeed",
                    new[] { typeof(string) });
            }

            if (needType != null)
            {
                _setPointsMethod = needType.GetMethod("SetPoints",
                    new[] { typeof(float) });
            }

            Debug.Log($"[Archipelago] NeedSystem resolved: Manager={_needManagerType != null}, " +
                      $"GetNeed={_getNeedMethod != null}, SetPoints={_setPointsMethod != null}");
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

            if (_bonusManagerType == null || _addBonusMethod == null)
            {
                Debug.LogWarning("[Archipelago] BonusSystem not resolved — cannot apply bonus");
                return 0;
            }

            // Find all entities with BonusManager
            object managers = null;

            var genericMethod = _entityComponentRegistry.GetType().GetMethod("GetEnabled");
            if (genericMethod != null && genericMethod.IsGenericMethod)
            {
                managers = genericMethod.MakeGenericMethod(_bonusManagerType)
                    .Invoke(_entityComponentRegistry, null);
            }
            else
            {
                var nonGeneric = _entityComponentRegistry.GetType()
                    .GetMethod("GetEnabled", new[] { typeof(Type) });
                if (nonGeneric != null)
                    managers = nonGeneric.Invoke(_entityComponentRegistry, new object[] { _bonusManagerType });
            }

            if (managers == null)
            {
                Debug.LogWarning("[Archipelago] Could not enumerate BonusManager entities");
                return 0;
            }
            int count = 0;

            // AddBonus takes (string bonusId, float multiplierDelta)
            foreach (var manager in (System.Collections.IEnumerable)managers)
            {
                try
                {
                    _addBonusMethod.Invoke(manager, new object[] { bonusIdStr, multiplierDelta });
                    count++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Archipelago] AddBonus('{bonusIdStr}', {multiplierDelta}) failed on entity: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            return count;
        }

        private void DiscoverBonusIds()
        {
            try
            {
                var serviceType = FindType("Timberborn.BonusSystem.BonusTypeSpecService");
                if (serviceType == null) return;

                var bonusIdsProp = serviceType.GetProperty("BonusIds",
                    BindingFlags.Public | BindingFlags.Instance);
                if (bonusIdsProp == null) return;

                // BonusTypeSpecService is a singleton — find it via EntityComponentRegistry or direct search
                // We can't easily get the instance here, but log the spec type info for debugging
                Debug.Log("[Archipelago] BonusTypeSpec IDs available via BonusTypeSpecService.BonusIds at runtime");
                Debug.Log($"[Archipelago] Configured boost BonusIds: {string.Join(", ", BoostMapping.Values)}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] BonusId discovery failed: {ex.Message}");
            }
        }

        private void EnsureBonusSystemResolved()
        {
            if (_bonusSystemSearched) return;
            _bonusSystemSearched = true;

            _bonusManagerType = FindType("Timberborn.BonusSystem.BonusManager");

            if (_bonusManagerType != null)
            {
                // AddBonus(string bonusId, float multiplierDelta)
                _addBonusMethod = _bonusManagerType.GetMethod("AddBonus",
                    new[] { typeof(string), typeof(float) });

                // Fallback: search by name if exact signature not found
                _addBonusMethod ??= _bonusManagerType.GetMethod("AddBonus",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            Debug.Log($"[Archipelago] BonusSystem resolved: Manager={_bonusManagerType != null}, " +
                      $"AddBonus={_addBonusMethod != null}");
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
