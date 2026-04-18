using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Timberborn.BonusSystem;
using Timberborn.EntitySystem;
using Timberborn.GameCycleSystem;
using Timberborn.HazardousWeatherSystem;
using Timberborn.Population;
using Timberborn.SingletonSystem;
using Timberborn.WeatherSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Handles non-blueprint AP item effects: traps, filler (resource injection),
    /// and boosts (permanent stat bonuses).
    /// </summary>
    public class ApEffectHandler : ILoadableSingleton, IPostLoadableSingleton, IUnloadableSingleton
    {
        internal static ApEffectHandler Instance { get; private set; }

        private readonly HazardousWeatherService _hazardousWeatherService;
        private readonly WeatherService _weatherService;
        private readonly TemperateWeatherDurationService _temperateWeatherDurationService;
        private readonly GameCycleService _gameCycleService;
        private readonly EntityComponentRegistry _entityComponentRegistry;
        private readonly EntityRegistry _entityRegistry;
        private readonly BonusTypeSpecService _bonusTypeSpecService;
        private readonly PopulationService _populationService;
        private readonly ArchipelagoSaveData _saveData;

        // BaseComponent.GetComponent<T>() open generic method definition — used to
        // look up BaseComponent-derived types (BonusManager, Inventory, …) per-entity.
        // MakeGenericMethod is safe here: GetComponent<T> has no constraints.
        private Type _baseComponentType;
        private MethodInfo _getComponentOpen;
        private bool _baseComponentSearched;

        // Weather trap queue: additional Hazardous Weather traps that arrive
        // while a hazardous cycle is already active or pending. Drained by
        // ProcessWeatherQueue() once the current cycle ends.
        private readonly Queue<string> _weatherTrapQueue = new();

        // In-game days of warning the player gets before a queued hazardous
        // cycle starts. Used to shorten the current temperate cycle so the
        // natural transition happens sooner but still after a visible countdown.
        private const int WEATHER_NOTICE_DAYS = 3;

        // Filler injection pipeline: GoodAmount struct + Inventory.GiveIgnoringCapacity
        // resolved via reflection. Inventory extends BaseComponent (not UnityEngine.Object),
        // so entities must be located via EntityComponentRegistry, not FindObjectsByType.
        private Type _goodAmountType;
        private Type _inventoryType;
        private MethodInfo _giveIgnoringCapacityMethod;
        private MethodInfo _givesMethod;
        private bool _inventorySystemSearched;

        // BonusManager resolved via reflection
        private Type _bonusManagerType;
        private MethodInfo _addBonusMethod;
        private bool _bonusSystemSearched;

        // NeedManager resolved via reflection
        private Type _needManagerType;
        private FieldInfo _allNeedsField;       // NeedManager.<AllNeeds>k__BackingField
        private PropertyInfo _needIdProperty;   // Need.NeedId
        private MethodInfo _setPointsMethod;    // Need.SetPoints(float)
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
            TemperateWeatherDurationService temperateWeatherDurationService,
            GameCycleService gameCycleService,
            EntityComponentRegistry entityComponentRegistry,
            EntityRegistry entityRegistry,
            BonusTypeSpecService bonusTypeSpecService,
            PopulationService populationService,
            ArchipelagoSaveData saveData)
        {
            _hazardousWeatherService = hazardousWeatherService;
            _weatherService = weatherService;
            _temperateWeatherDurationService = temperateWeatherDurationService;
            _gameCycleService = gameCycleService;
            _entityComponentRegistry = entityComponentRegistry;
            _entityRegistry = entityRegistry;
            _bonusTypeSpecService = bonusTypeSpecService;
            _populationService = populationService;
            _saveData = saveData;
        }

        /// <summary>
        /// Resolve BaseComponent.GetComponent{T}() as an open generic MethodInfo.
        /// This is the entity-traversal primitive for finding BaseComponent-derived
        /// types (BonusManager, Inventory, etc.) on game entities. Works because
        /// GetComponent{T} has no generic constraints — MakeGenericMethod is safe.
        ///
        /// Contrast with EntityComponentRegistry.GetEnabled{T} which requires
        /// T : BaseComponent, IRegisteredComponent. BonusManager and Inventory do
        /// NOT implement IRegisteredComponent, so MakeGenericMethod against the
        /// registry's method throws ArgumentException at runtime.
        /// </summary>
        private void EnsureBaseComponentResolved()
        {
            if (_baseComponentSearched) return;
            _baseComponentSearched = true;

            _baseComponentType = FindType("Timberborn.BaseComponentSystem.BaseComponent");
            if (_baseComponentType == null)
            {
                Debug.LogWarning("[Archipelago] BaseComponent type not found — cannot traverse entity components");
                return;
            }

            _getComponentOpen = _baseComponentType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetComponent"
                                  && m.IsGenericMethodDefinition
                                  && m.GetParameters().Length == 0);

            Debug.Log($"[Archipelago] BaseComponent resolved: Type={_baseComponentType != null}, " +
                      $"GetComponent<T>={_getComponentOpen != null}");
        }

        /// <summary>
        /// Iterate every game entity and collect the first non-null component of
        /// the given BaseComponent-derived type. Used for both BonusManager (boosts)
        /// and Inventory (filler) lookups.
        /// </summary>
        private List<object> FindEntityComponents(Type componentType)
        {
            var result = new List<object>();
            EnsureBaseComponentResolved();
            if (_getComponentOpen == null) return result;

            MethodInfo typedGetComponent;
            try
            {
                typedGetComponent = _getComponentOpen.MakeGenericMethod(componentType);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] MakeGenericMethod(GetComponent<{componentType.Name}>) failed: {ex.Message}");
                return result;
            }

            // ReadOnlyList<EntityComponent> is a struct — always safe to iterate.
            // Empty lists just produce zero iterations.
            foreach (var entity in _entityRegistry.Entities)
            {
                object comp = null;
                try { comp = typedGetComponent.Invoke(entity, null); }
                catch { /* entity can't host this type */ }
                if (comp != null) result.Add(comp);
            }
            return result;
        }

        public void Load()
        {
            Instance = this;
            DiscoverBonusIds();
            RunReflectionDryRuns();
            // NOTE: Boost re-application deferred to PostLoad — at Load() time on a
            // save-reload, entities from the save haven't been instantiated yet, so
            // FindEntityComponents returns 0 and the re-apply is a no-op. That
            // happened in the 2026-04-18 playtest: beaver MovementSpeed reverted
            // from 1.25 to 1.05 because re-apply found 0 BonusManagers.
        }

        /// <summary>
        /// Runs after every ILoadableSingleton.Load() in the scene — by this point
        /// the EntityRegistry is populated with all restored entities, so we can
        /// re-apply persisted boosts and log the real entity counts.
        /// </summary>
        public void PostLoad()
        {
            LogPostLoadState();

            // Re-apply persisted boosts now that entities actually exist.
            if (_saveData.ActiveBoosts.Count > 0)
            {
                Debug.Log($"[Archipelago] PostLoad: re-applying {_saveData.ActiveBoosts.Count} active boost(s)");
                foreach (var boostName in _saveData.ActiveBoosts)
                    ApplyBoostToAllEntities(boostName);
            }
        }

        /// <summary>
        /// Diagnostic snapshot of the entity population at PostLoad. Used to verify
        /// that entity counts match what the game displays — especially important
        /// for large saves (e.g., 150 beavers + 100 bots test saves) and for
        /// sanity-checking the BonusManager / Inventory / NeedManager lookup paths.
        /// </summary>
        private void LogPostLoadState()
        {
            int totalEntities = 0;
            try { foreach (var _ in _entityRegistry.Entities) totalEntities++; }
            catch (Exception ex) { Debug.LogWarning($"[Archipelago] PostLoad entity count failed: {ex.Message}"); }

            int bonusMgrs = _bonusManagerType != null ? FindEntityComponents(_bonusManagerType).Count : -1;
            int inventories = _inventoryType != null ? FindEntityComponents(_inventoryType).Count : -1;
            int needMgrs = _needManagerType != null ? FindEntityComponents(_needManagerType).Count : -1;

            int beavers = -1, adults = -1, bots = -1;
            try
            {
                var pop = _populationService.GlobalPopulationData;
                beavers = pop.NumberOfBeavers;
                adults = pop.NumberOfAdults;
                bots = pop.NumberOfBots;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] PopulationService read failed: {ex.Message}");
            }

            Debug.Log($"[Archipelago] PostLoad state — " +
                      $"entities={totalEntities}, BonusManagers={bonusMgrs}, Inventories={inventories}, " +
                      $"NeedManagers={needMgrs}, beavers={beavers} (adults={adults}), bots={bots}");
        }

        /// <summary>
        /// Exercise every reflection path the mod depends on at Load() so that
        /// incorrect type assumptions, missing methods, or constraint-enforced
        /// MakeGenericMethod failures surface LOUDLY in Player.log at startup
        /// instead of crashing the game on first use of a feature.
        ///
        /// Added after the 2026-04-18 crash where ApplyBonusToAllEntities threw
        /// ArgumentException on the first boost because GetEnabled&lt;T&gt; had an
        /// IRegisteredComponent constraint that MakeGenericMethod enforces.
        /// That error only surfaced mid-game; a dry-run at Load() would have
        /// caught it immediately.
        /// </summary>
        private void RunReflectionDryRuns()
        {
            EnsureBaseComponentResolved();
            EnsureBonusSystemResolved();
            EnsureInventorySystemResolved();
            EnsureNeedSystemResolved();

            Debug.Log("[Archipelago] Reflection dry-run beginning…");

            // 1) BonusManager lookup (boost pipeline)
            if (_bonusManagerType != null)
            {
                try
                {
                    var bms = FindEntityComponents(_bonusManagerType);
                    Debug.Log($"[Archipelago] Dry-run: BonusManager enumerable — {bms.Count} instance(s) " +
                              $"(0 is normal at fresh-game Load before entities instantiate).");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Archipelago] Dry-run FAILED: BonusManager enumeration threw: {ex.Message}");
                }
            }

            // 2) Inventory lookup + GiveIgnoringCapacity (filler pipeline)
            if (_inventoryType != null)
            {
                try
                {
                    var invs = FindEntityComponents(_inventoryType);
                    Debug.Log($"[Archipelago] Dry-run: Inventory enumerable — {invs.Count} instance(s).");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Archipelago] Dry-run FAILED: Inventory enumeration threw: {ex.Message}");
                }
            }

            // 3) NeedManager lookup (trap pipeline)
            if (_needManagerType != null)
            {
                try
                {
                    var nms = FindEntityComponents(_needManagerType);
                    Debug.Log($"[Archipelago] Dry-run: NeedManager enumerable — {nms.Count} instance(s).");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Archipelago] Dry-run FAILED: NeedManager enumeration threw: {ex.Message}");
                }
            }

            // 4) GoodAmount construction (filler pipeline secondary check)
            if (_goodAmountType != null)
            {
                try
                {
                    Activator.CreateInstance(_goodAmountType, "Log", 1);
                    Debug.Log("[Archipelago] Dry-run: GoodAmount('Log', 1) ctor OK.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Archipelago] Dry-run FAILED: GoodAmount ctor threw: {ex.Message}");
                }
            }

            Debug.Log("[Archipelago] Reflection dry-run complete.");
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
                case "Hazardous Weather":
                    TriggerHazardousWeather();
                    break;
                case "Hungry Beavers":
                    TriggerHungryBeavers();
                    break;
                case "Thirsty Beavers":
                    TriggerThirstyBeavers();
                    break;
                // Legacy trap names kept for backwards compat with any v0.0.2-era
                // seeds still in circulation; both now route through the generic
                // weather path so the game's own randomizer picks drought/badtide.
                case "Early Drought":
                case "Badwater Leak":
                    Debug.Log($"[Archipelago] Legacy trap '{trapName}' routed to generic Hazardous Weather.");
                    TriggerHazardousWeather();
                    break;
                default:
                    Debug.LogWarning($"[Archipelago] Unknown trap: {trapName}");
                    break;
            }
        }

        /// <summary>
        /// Entry point for the consolidated Hazardous Weather trap. Instead of
        /// forcing a specific weather type mid-cycle (which the v0.0.2 approach
        /// attempted and which only updated history state, not the visual weather
        /// widget), this shortens the current temperate cycle and lets the game's
        /// own HazardousWeatherRandomizer pick drought vs badtide at natural
        /// cycle end. The player gets a proper countdown UI and the weather
        /// widget transitions correctly.
        /// </summary>
        private void TriggerHazardousWeather()
        {
            bool hazardousActive = _weatherService.IsHazardousWeather;
            bool hazardousPending = _hazardousWeatherService.HazardousWeatherDuration > 0 && !hazardousActive;

            if (hazardousActive || hazardousPending)
            {
                int trapMode = 0;
                if (ArchipelagoManager.SlotData != null
                    && ArchipelagoManager.SlotData.TryGetValue("trap_mode", out var modeObj))
                {
                    int.TryParse(modeObj?.ToString() ?? "0", out trapMode);
                }

                if (trapMode == 1)
                {
                    Debug.Log("[Archipelago] Hazardous Weather skipped (already active/pending, trap_mode=skip)");
                    ArchipelagoManager.PostLogMessage("Trap skipped: Hazardous Weather (weather already active)");
                    return;
                }

                _weatherTrapQueue.Enqueue("HazardousWeather");
                Debug.Log($"[Archipelago] Hazardous Weather queued (queue size: {_weatherTrapQueue.Count})");
                ArchipelagoManager.PostLogMessage("Trap queued: Hazardous Weather (weather already active)");
                return;
            }

            ScheduleHazardousWeather();
        }

        /// <summary>
        /// Set things up so the game naturally transitions to hazardous weather
        /// within WEATHER_NOTICE_DAYS:
        /// 1. Shorten the current temperate cycle (if its natural duration is
        ///    longer than the notice period). TemperateWeatherDuration is the
        ///    total day-length of the current cycle; the cycle ends when
        ///    GameCycleService.CycleDay &gt;= TemperateWeatherDuration.
        /// 2. Call HazardousWeatherService.SetForCycle(duration) so that when
        ///    the temperate cycle ends, the game's OnCycleEndedEvent handler
        ///    sees a non-zero HazardousWeatherDuration and transitions to
        ///    a hazardous cycle. The game picks drought vs badtide itself via
        ///    its HazardousWeatherRandomizer.
        /// </summary>
        private void ScheduleHazardousWeather()
        {
            try
            {
                // Queue next cycle to be hazardous. SetForCycle sets HazardousWeatherDuration
                // to a random value in the configured range.
                var hwServiceType = _hazardousWeatherService.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                int requestedHazardousDuration = _hazardousWeatherService.HazardousWeatherDuration;
                if (requestedHazardousDuration <= 0) requestedHazardousDuration = 5;

                var setForCycleMethod = hwServiceType.GetMethod("SetForCycle", flags);
                if (setForCycleMethod != null)
                {
                    setForCycleMethod.Invoke(_hazardousWeatherService, new object[] { requestedHazardousDuration });
                }
                Debug.Log($"[Archipelago] HazardousWeatherService queued for next cycle — " +
                          $"HazardousWeatherDuration={_hazardousWeatherService.HazardousWeatherDuration}");

                // Shorten the current temperate cycle so transition happens soon, but
                // only if natural duration is LONGER than our notice period. If it's
                // already shorter, leave it alone (hazardous will fire naturally sooner).
                int currentDay = _gameCycleService.CycleDay;
                int targetEndDay = currentDay + WEATHER_NOTICE_DAYS;
                int naturalDuration = _temperateWeatherDurationService.TemperateWeatherDuration;

                if (naturalDuration > targetEndDay)
                {
                    _temperateWeatherDurationService.TemperateWeatherDuration = targetEndDay;
                    Debug.Log($"[Archipelago] Shortened temperate: " +
                              $"TemperateWeatherDuration {naturalDuration} -> {targetEndDay} " +
                              $"(current day {currentDay}, notice {WEATHER_NOTICE_DAYS} days). " +
                              "Game will transition to hazardous at cycle end.");
                    ArchipelagoManager.PostLogMessage(
                        $"WARNING: Hazardous weather incoming in {WEATHER_NOTICE_DAYS} days! Prepare your colony!");
                }
                else
                {
                    int daysRemaining = naturalDuration - currentDay;
                    Debug.Log($"[Archipelago] Temperate already ending soon " +
                              $"(natural end day {naturalDuration}, current day {currentDay}, " +
                              $"~{daysRemaining} days). Leaving duration unchanged; " +
                              "hazardous will fire at natural cycle end.");
                    ArchipelagoManager.PostLogMessage(
                        $"WARNING: Hazardous weather incoming in ~{Math.Max(0, daysRemaining)} days! Prepare your colony!");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Failed to schedule Hazardous Weather: {ex.Message}");
                ArchipelagoManager.PostLogMessage("Trap: Hazardous Weather (failed to schedule)");
            }
        }

        /// <summary>
        /// Called each tick by ArchipelagoTicker. Dequeues the next weather trap
        /// once the current hazardous cycle ends and no new one is pending.
        /// </summary>
        public void ProcessWeatherQueue()
        {
            if (_weatherTrapQueue.Count == 0) return;

            bool hazardousActive = _weatherService.IsHazardousWeather;
            bool hazardousPending = _hazardousWeatherService.HazardousWeatherDuration > 0 && !hazardousActive;
            if (hazardousActive || hazardousPending) return;

            _weatherTrapQueue.Dequeue();
            Debug.Log($"[Archipelago] Dequeuing weather trap (remaining: {_weatherTrapQueue.Count})");
            ScheduleHazardousWeather();
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

            if (_needManagerType == null || _allNeedsField == null ||
                _needIdProperty == null || _setPointsMethod == null)
            {
                Debug.LogWarning("[Archipelago] NeedSystem not fully resolved — cannot set need");
                return 0;
            }

            // NeedManager extends BaseComponent but does not implement IRegisteredComponent
            // (same as BonusManager / Inventory) — use EntityRegistry.Entities + per-entity
            // GetComponent<T>() instead of EntityComponentRegistry.GetEnabled<T>().
            var managers = FindEntityComponents(_needManagerType);

            if (managers.Count == 0)
            {
                Debug.LogWarning("[Archipelago] No NeedManager instances found on entities");
                return 0;
            }

            int count = 0;
            foreach (var manager in managers)
            {
                try
                {
                    var allNeeds = _allNeedsField.GetValue(manager);
                    if (allNeeds == null) continue;

                    foreach (var need in (System.Collections.IEnumerable)allNeeds)
                    {
                        var id = _needIdProperty.GetValue(need) as string;
                        if (id == needId)
                        {
                            _setPointsMethod.Invoke(need, new object[] { points });
                            count++;
                            break;
                        }
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
                // Property getter may fail due to ImmutableArray<> dependency,
                // so access the auto-property backing field directly.
                _allNeedsField = _needManagerType.GetField("<AllNeeds>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }

            if (needType != null)
            {
                _needIdProperty = needType.GetProperty("NeedId",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _setPointsMethod = needType.GetMethod("SetPoints",
                    new[] { typeof(float) });
            }

            Debug.Log($"[Archipelago] NeedSystem resolved: Manager={_needManagerType != null}, " +
                      $"AllNeedsField={_allNeedsField != null}, NeedId={_needIdProperty != null}, " +
                      $"SetPoints={_setPointsMethod != null}");
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
            EnsureInventorySystemResolved();

            if (_goodAmountType == null)
            {
                Debug.LogWarning("[Archipelago] Could not find GoodAmount type for goods injection");
                return;
            }
            if (_inventoryType == null || _giveIgnoringCapacityMethod == null)
            {
                Debug.LogWarning("[Archipelago] Could not resolve Inventory.GiveIgnoringCapacity for goods injection");
                return;
            }

            // GoodAmount(string goodId, int amount) — verified ctor signature
            var goodAmount = Activator.CreateInstance(_goodAmountType, goodIdStr, amount);

            // Inventory extends BaseComponent but does NOT implement IRegisteredComponent,
            // so EntityComponentRegistry.GetEnabled<T>() via MakeGenericMethod throws
            // ArgumentException at runtime (same crash as BonusManager). Instead iterate
            // EntityRegistry.Entities and use BaseComponent.GetComponent<T>() per entity.
            var inventories = FindEntityComponents(_inventoryType);

            if (inventories.Count == 0)
            {
                Debug.LogWarning($"[Archipelago] No Inventory instances found — cannot inject {amount} {goodIdStr}");
                return;
            }

            // Prefer inventories that explicitly accept this good (stockpiles over
            // beaver-carry inventories). Fall back to any inventory if nothing matches.
            var accepting = new List<object>();
            if (_givesMethod != null)
            {
                foreach (var inv in inventories)
                {
                    try
                    {
                        if (_givesMethod.Invoke(inv, new object[] { goodIdStr }) is bool gives && gives)
                            accepting.Add(inv);
                    }
                    catch { /* skip on reflection issue */ }
                }
            }
            var candidates = accepting.Count > 0 ? accepting : inventories;

            bool injected = false;
            foreach (var inventory in candidates)
            {
                try
                {
                    _giveIgnoringCapacityMethod.Invoke(inventory, new[] { goodAmount });
                    injected = true;
                    Debug.Log($"[Archipelago] Injected {amount} {goodIdStr} into an inventory " +
                              $"({accepting.Count} accepting of {inventories.Count} total)");
                    break; // GiveIgnoringCapacity handles the full amount
                }
                catch (Exception ex)
                {
                    // This inventory rejected the good — try next
                    Debug.Log($"[Archipelago] Inventory rejected {goodIdStr}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            if (!injected)
                Debug.LogWarning($"[Archipelago] Could not inject {amount} {goodIdStr} — no accepting inventory found " +
                                 $"(checked {candidates.Count} candidate(s))");
        }

        private void EnsureInventorySystemResolved()
        {
            if (_inventorySystemSearched) return;
            _inventorySystemSearched = true;

            _goodAmountType = FindType("Timberborn.Goods.GoodAmount");
            _inventoryType = FindType("Timberborn.InventorySystem.Inventory");

            if (_inventoryType != null && _goodAmountType != null)
            {
                // GiveIgnoringCapacity(GoodAmount good) — verified single-param signature
                _giveIgnoringCapacityMethod = _inventoryType.GetMethod(
                    "GiveIgnoringCapacity", new[] { _goodAmountType });
            }

            if (_inventoryType != null)
            {
                // Gives(string goodId) — filters out inventories that don't accept a good
                _givesMethod = _inventoryType.GetMethod("Gives", new[] { typeof(string) });
            }

            Debug.Log($"[Archipelago] InventorySystem resolved: Inventory={_inventoryType != null}, " +
                      $"GoodAmount={_goodAmountType != null}, " +
                      $"GiveIgnoringCapacity={_giveIgnoringCapacityMethod != null}, " +
                      $"Gives={_givesMethod != null}");
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
        ///
        /// Previously tried EntityComponentRegistry.GetEnabled{T}() via
        /// MakeGenericMethod, but GetEnabled has a `T : IRegisteredComponent`
        /// constraint that MakeGenericMethod DOES enforce at runtime. BonusManager
        /// only implements IAwakableComponent, so that call threw
        /// `ArgumentException: Invalid generic arguments` (crashed the mod).
        ///
        /// Instead: iterate EntityRegistry.Entities (ReadOnlyList&lt;EntityComponent&gt;)
        /// and use BaseComponent.GetComponent{T}() on each. GetComponent{T} is
        /// unconstrained so MakeGenericMethod is safe.
        /// </summary>
        private int ApplyBonusToAllEntities(string bonusIdStr, float multiplierDelta)
        {
            EnsureBonusSystemResolved();

            if (_bonusManagerType == null || _addBonusMethod == null)
            {
                Debug.LogWarning("[Archipelago] BonusSystem not resolved — cannot apply bonus");
                return 0;
            }

            var managers = FindEntityComponents(_bonusManagerType);

            if (managers.Count == 0)
            {
                Debug.LogWarning("[Archipelago] No BonusManager instances found on entities");
                return 0;
            }

            int count = 0;
            foreach (var manager in managers)
            {
                try
                {
                    // AddBonus takes (string bonusId, float multiplierDelta)
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

        /// <summary>
        /// Validate BoostMapping BonusIds against the live game's BonusTypeSpecService
        /// at load time. Any mapped id not present in the game is a typo or stale
        /// string that would have silently failed at boost-apply time. Logs a
        /// warning so these bugs surface at startup instead of during playtest.
        ///
        /// The v0.0.1 playtest found four such mismatches (WorkSpeed vs WorkingSpeed,
        /// TreeGrowth vs GrowthSpeed, Longevity vs LifeExpectancy, WoodcuttingYield
        /// vs CuttingSuccessChance). This check would have caught all of them at load.
        /// </summary>
        private void DiscoverBonusIds()
        {
            try
            {
                var liveIds = new HashSet<string>(_bonusTypeSpecService.BonusIds ?? Enumerable.Empty<string>());
                var mappedIds = BoostMapping.Values.Select(v => v.bonusId).ToHashSet();

                if (liveIds.Count == 0)
                {
                    Debug.LogWarning("[Archipelago] BonusTypeSpecService reports no BonusIds at load time — " +
                                     "validation skipped (may run before specs are loaded).");
                    return;
                }

                var missing = mappedIds.Where(id => !liveIds.Contains(id)).ToList();

                if (missing.Count == 0)
                {
                    Debug.Log($"[Archipelago] BonusId validation passed — all {mappedIds.Count} mapped ids " +
                              $"present in game ({liveIds.Count} live ids total).");
                }
                else
                {
                    Debug.LogWarning($"[Archipelago] BonusId validation FAILED — mapping references " +
                                     $"{missing.Count} unknown id(s): [{string.Join(", ", missing)}]. " +
                                     $"Live ids: [{string.Join(", ", liveIds.OrderBy(s => s))}]. " +
                                     "Affected boosts will silently fail to apply until the mapping is corrected.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] BonusId validation threw: {ex.Message}");
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
