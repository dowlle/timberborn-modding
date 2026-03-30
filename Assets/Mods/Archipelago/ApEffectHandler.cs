using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Timberborn.EntitySystem;
using Timberborn.HazardousWeatherSystem;
using Timberborn.SingletonSystem;
using Timberborn.WeatherSystem;
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
        private readonly WeatherService _weatherService;
        private readonly EntityComponentRegistry _entityComponentRegistry;
        private readonly ArchipelagoSaveData _saveData;

        // Weather scheduling state
        private readonly Queue<string> _weatherTrapQueue = new();

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
            { "Faster Beaver Growth",         ("GrowthSpeed", 0.50f) },
            { "Longer Life Expectancy",       ("LifeExpectancy", 0.25f) },
            { "Better Woodcutting Chance",    ("CuttingSuccessChance", 0.25f) },
        };

        public ApEffectHandler(
            HazardousWeatherService hazardousWeatherService,
            WeatherService weatherService,
            EntityComponentRegistry entityComponentRegistry,
            ArchipelagoSaveData saveData)
        {
            _hazardousWeatherService = hazardousWeatherService;
            _weatherService = weatherService;
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
                _pendingWeatherType = type;
                _pendingWeatherStartDay = currentDay + WEATHER_NOTICE_DAYS;
                string weatherName = type == "badtide" ? "Badtide" : "Drought";
                Debug.Log($"[Archipelago] Scheduled {type} to start on day {_pendingWeatherStartDay} (current: {currentDay}, notice: {WEATHER_NOTICE_DAYS} days)");
                ArchipelagoManager.PostLogMessage($"WARNING: {weatherName} approaching in {WEATHER_NOTICE_DAYS} days! Prepare your colony!");
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
            return _weatherService.CycleDay;
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
            return _weatherService.IsHazardousWeather;
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

            // NeedManager extends BaseComponent, not EntityComponent, so
            // EntityComponentRegistry.GetEnabled<T>() can't be used (constraint mismatch).
            var managers = UnityEngine.Object.FindObjectsByType(
                _needManagerType, FindObjectsSortMode.None);

            if (managers == null || managers.Length == 0)
            {
                Debug.LogWarning("[Archipelago] No NeedManager instances found");
                return 0;
            }

            int count = 0;
            foreach (var manager in managers)
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
            // Resolve GoodAmount type — GiveIgnoringCapacity takes a single GoodAmount param
            if (!_goodIdTypeSearched)
            {
                _goodIdTypeSearched = true;
                _goodIdType = FindType("Timberborn.Goods.GoodAmount");
            }

            if (_goodIdType == null)
            {
                Debug.LogWarning("[Archipelago] Could not find GoodAmount type for goods injection");
                return;
            }

            // GoodAmount(string goodId, int amount)
            var goodAmount = Activator.CreateInstance(_goodIdType, goodIdStr, amount);

            var inventoryType = FindType("Timberborn.InventorySystem.Inventory");
            if (inventoryType == null)
            {
                Debug.LogWarning("[Archipelago] Could not find Inventory type");
                return;
            }

            var inventories = UnityEngine.Object.FindObjectsByType(
                inventoryType, FindObjectsSortMode.None);

            if (inventories == null || inventories.Length == 0)
            {
                Debug.LogWarning("[Archipelago] No Inventory instances found");
                return;
            }

            bool injected = false;
            foreach (var inventory in inventories)
            {
                var giveMethod = inventory.GetType().GetMethod("GiveIgnoringCapacity");
                if (giveMethod == null) continue;

                try
                {
                    giveMethod.Invoke(inventory, new[] { goodAmount });
                    injected = true;
                    break; // GiveIgnoringCapacity handles the full amount
                }
                catch
                {
                    // This inventory can't accept this good, try next
                }
            }

            if (!injected)
                Debug.LogWarning($"[Archipelago] Could not inject {amount} {goodIdStr} — no accepting inventory found");
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

            // Idempotent: skip if already active (prevents double-application on replay or duplicate delivery)
            if (_saveData.ActiveBoosts.Contains(boostName))
            {
                Debug.Log($"[Archipelago] Boost '{boostName}' already active, skipping duplicate");
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

            // BonusManager extends BaseComponent, not EntityComponent, so
            // EntityComponentRegistry.GetEnabled<T>() can't be used (constraint mismatch).
            // Fall back to FindObjectsByType which finds all MonoBehaviour-derived instances.
            var managers = UnityEngine.Object.FindObjectsByType(
                _bonusManagerType, FindObjectsSortMode.None);

            if (managers == null || managers.Length == 0)
            {
                Debug.LogWarning("[Archipelago] No BonusManager instances found");
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
