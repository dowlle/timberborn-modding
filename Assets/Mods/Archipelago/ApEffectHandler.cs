using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.BonusSystem;
using Timberborn.Effects;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.GameCycleSystem;
using Timberborn.Goods;
using Timberborn.HazardousWeatherSystem;
using Timberborn.InventorySystem;
using Timberborn.NeedSystem;
using Timberborn.Population;
using Timberborn.SimpleOutputBuildings;
using Timberborn.SingletonSystem;
using Timberborn.Stockpiles;
using Timberborn.WeatherSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Handles non-blueprint AP item effects: traps, filler (received goods,
    /// delivered from the pending-goods ledger into storage), and boosts
    /// (permanent stat bonuses).
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
        private readonly DistrictCenterRegistry _districtCenterRegistry;
        private readonly BonusTypeSpecService _bonusTypeSpecService;
        private readonly PopulationService _populationService;
        private readonly ArchipelagoSaveData _saveData;
        private readonly EventBus _eventBus;

        // BaseComponent.GetComponent<T>() open generic method definition — used to
        // look up BaseComponent-derived types (BonusManager, Inventory, …) per-entity.
        // MakeGenericMethod is safe here: GetComponent<T> has no constraints.
        private Type _baseComponentType;
        private MethodInfo _getComponentOpen;
        private bool _baseComponentSearched;

        // Weather trap queue: additional Hazardous Weather traps that arrive
        // while a hazardous cycle is already active or our previous trap is
        // awaiting transition. Drained by ProcessWeatherQueue() once the
        // current cycle ends (or our scheduled trap has fired).
        private readonly Queue<string> _weatherTrapQueue = new();

        // True from the moment we schedule a trap (shorten current temperate)
        // until the next CycleStarted event fires (the cycle AFTER the trap-
        // induced hazardous resolves). While set, additional traps queue rather
        // than fire — covers both the "awaiting natural transition to hazardous"
        // window and the "hazardous in progress, awaiting cycle wrap" window in
        // a single flag.
        //
        // Cleared in OnCycleStarted, which is the post-trap cycle-cleanup hook
        // where we also re-randomize TemperateWeatherDuration (via SetForCycle)
        // to fix the post-v0.0.4 water-spawn regression — the hypothesis being
        // that the mutated backing field leaves downstream water-source state
        // in a stale configuration during the temperate cycle that follows.
        private bool _trapScheduledAwaitingTransition;

        // Filler delivery: received goods wait in ArchipelagoSaveData.PendingGoods and
        // a periodic pass moves them out. The goods_delivery option (slot_data, saved in
        // ArchipelagoSaveData.DeliveryMode) picks the target:
        // - District Center (default): the District Center's output inventory, the same
        //   inventory and call the game uses for starting berries and water
        //   (StartingGoodsProvider -> GiveExistingIgnoringCapacity). Its workers haul the
        //   goods to storage, builders can take them, and beavers eat and drink from it.
        //   Goods it does not take go to stockpiles.
        // - Storage: only what fits into finished stockpiles. Stockpile is the only
        //   inventory owner with PublicInput, and it enables its inventory only in the
        //   finished state, so construction sites, carry slots and workshop buffers are
        //   never candidates.
        // In both modes, goods that find no room stay pending for the next pass.
        private const float DeliveryIntervalSeconds = 3f;
        private GoodsDeliveryMode? _lastDeliveryMode;
        private float _nextDeliveryTime;
        private string _lastPendingSummary;

        // BonusManager resolved via reflection
        private Type _bonusManagerType;
        private MethodInfo _addBonusMethod;
        private bool _bonusSystemSearched;

        // Resource items: package settings parsed from slot_data, cached per slot_data
        // instance. Names and base amounts live in ResourcePackages.
        private Dictionary<string, object> _packageSlotData;
        private int? _packagePercent;
        private Dictionary<string, (string goodId, int amount)> _slotPackages;

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
            DistrictCenterRegistry districtCenterRegistry,
            BonusTypeSpecService bonusTypeSpecService,
            PopulationService populationService,
            ArchipelagoSaveData saveData,
            EventBus eventBus)
        {
            _hazardousWeatherService = hazardousWeatherService;
            _weatherService = weatherService;
            _temperateWeatherDurationService = temperateWeatherDurationService;
            _gameCycleService = gameCycleService;
            _entityComponentRegistry = entityComponentRegistry;
            _entityRegistry = entityRegistry;
            _districtCenterRegistry = districtCenterRegistry;
            _bonusTypeSpecService = bonusTypeSpecService;
            _populationService = populationService;
            _saveData = saveData;
            _eventBus = eventBus;
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
        /// and NeedManager (traps) lookups.
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
            _eventBus.Register(this);
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
            int inventories = -1;
            try { inventories = _entityComponentRegistry.GetEnabled<Stockpile>().Count(); }
            catch (Exception ex) { Debug.LogWarning($"[Archipelago] PostLoad stockpile count failed: {ex.Message}"); }
            int needMgrs = -1;
            try { needMgrs = _entityComponentRegistry.GetEnabled<NeedManager>().Count(); }
            catch (Exception ex) { Debug.LogWarning($"[Archipelago] PostLoad NeedManager count failed: {ex.Message}"); }

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
                      $"entities={totalEntities}, BonusManagers={bonusMgrs}, FinishedStockpiles={inventories}, " +
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

            // 2) Finished stockpile lookup (filler delivery pipeline)
            try
            {
                var stockpiles = _entityComponentRegistry.GetEnabled<Stockpile>().Count();
                Debug.Log($"[Archipelago] Dry-run: finished Stockpile enumerable — {stockpiles} instance(s).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Dry-run FAILED: Stockpile enumeration threw: {ex.Message}");
            }

            // 3) NeedManager lookup (need traps). NeedManager is an IRegisteredComponent,
            // so the registry lists it directly.
            try
            {
                var nms = _entityComponentRegistry.GetEnabled<NeedManager>().Count();
                Debug.Log($"[Archipelago] Dry-run: NeedManager enumerable — {nms} instance(s).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Dry-run FAILED: NeedManager enumeration threw: {ex.Message}");
            }

            Debug.Log("[Archipelago] Reflection dry-run complete.");
        }

        public void Unload()
        {
            try { _eventBus.Unregister(this); }
            catch { /* unregister may throw if never registered or already torn down */ }

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
            else if (ResourcePackages.IsResourceItem(item.ItemName))
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
                case "Thirsty Beavers":
                    TriggerNeedTrap(trapName);
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
        /// Entry point for the consolidated Hazardous Weather trap. Shortens the
        /// current temperate cycle so the game's own state machine fires hazardous
        /// at the natural transition (no direct StartHazardousWeather call). The
        /// drought-vs-badtide pick is whatever HazardousWeatherRandomizer pre-rolled
        /// for this cycle, exactly as in an unmodded game.
        ///
        /// Design history:
        ///   v0.0.2  — forced specific weather types mid-cycle; only updated history
        ///             state, not the visual widget. Bug.
        ///   v0.0.3/4 — temperate-shortening via &lt;TemperateWeatherDuration&gt;k__BackingField
        ///             reflection write; fired correctly with 3-day notice but
        ///             appeared to break water spawning in the *following* temperate
        ///             cycle (drought-testing finding 2026-04-18, post-v0.0.4 ship).
        ///   v0.0.5 first attempt — direct StartHazardousWeather() call mid-temperate.
        ///             Worked but fired hazardous IMMEDIATELY (no countdown UI), wrong
        ///             player UX. Rejected during Session J runtime verification 2026-04-29.
        ///   v0.0.5 final (this code) — back to temperate-shortening but with an
        ///             OnCycleStarted hook on the post-trap cycle that re-randomizes
        ///             TemperateWeatherDuration via SetForCycle. Hypothesis: the
        ///             water-spawn regression was caused by the shortened backing-field
        ///             value persisting into the post-trap cycle's water-source
        ///             accounting. Re-randomizing on the next CycleStarted event
        ///             should restore healthy water spawn.
        /// </summary>
        private void TriggerHazardousWeather()
        {
            bool hazardousActive = _weatherService.IsHazardousWeather;

            // Defer only when weather is currently hazardous or we've already
            // scheduled a trap that hasn't fired yet. We intentionally do NOT
            // check the game's HazardousWeatherDuration — on late-game saves
            // the game always has a next-hazardous lined up during temperate,
            // which would otherwise make every trap queue forever.
            if (hazardousActive || _trapScheduledAwaitingTransition)
            {
                int trapMode = 0;
                if (ArchipelagoManager.SlotData != null
                    && ArchipelagoManager.SlotData.TryGetValue("trap_mode", out var modeObj))
                {
                    int.TryParse(modeObj?.ToString() ?? "0", out trapMode);
                }

                string reason = hazardousActive ? "weather currently active" : "trap already scheduled, awaiting transition";
                if (trapMode == 1)
                {
                    Debug.Log($"[Archipelago] Hazardous Weather skipped ({reason}, trap_mode=skip)");
                    ArchipelagoManager.PostLogMessage($"Trap skipped: Hazardous Weather ({reason})");
                    return;
                }

                _weatherTrapQueue.Enqueue("HazardousWeather");
                Debug.Log($"[Archipelago] Hazardous Weather queued ({reason}, queue size: {_weatherTrapQueue.Count})");
                ArchipelagoManager.PostLogMessage($"Trap queued: Hazardous Weather ({reason})");
                return;
            }

            ScheduleHazardousWeather();
        }

        /// <summary>
        /// Schedule a Hazardous Weather trap by SHORTENING the current temperate
        /// cycle. The game's own state machine then fires hazardous at the natural
        /// temperate-end transition — drought-vs-badtide pick is whatever
        /// HazardousWeatherRandomizer pre-rolled for this cycle, full visual UI
        /// (countdown notice + native "Drought/Badtide approaching" notification)
        /// fires through the game's normal path, no mod intervention at the
        /// transition itself.
        ///
        /// Implementation: write &lt;TemperateWeatherDuration&gt;k__BackingField
        /// directly via reflection to currentDay + WEATHER_NOTICE_DAYS. The
        /// publicizer exposes the backing field but not a public setter, so the
        /// reflection write is required (same pattern as &lt;AllNeeds&gt;k__BackingField
        /// from NeedSystem). If the existing TemperateWeatherDuration is already
        /// shorter than currentDay + WEATHER_NOTICE_DAYS, we leave it alone — the
        /// trap effectively queues for the natural transition that's already
        /// imminent.
        ///
        /// The post-v0.0.4 water-spawn regression hypothesis (the reason we
        /// abandoned v0.0.3 temperate-shortening originally) is mitigated by the
        /// OnCycleStarted hook that runs at the START of the post-trap cycle
        /// (after hazardous resolves, when the next temperate begins) — it calls
        /// _temperateWeatherDurationService.SetForCycle(cycle) to re-randomize
        /// TemperateWeatherDuration so any downstream water-source accounting
        /// derived from the (mutated, now-stale) backing-field value gets a
        /// fresh, game-validated duration.
        /// </summary>
        private const int WEATHER_NOTICE_DAYS = 3;

        private void ScheduleHazardousWeather()
        {
            try
            {
                int cycle = _gameCycleService.Cycle;
                int cycleDay = _gameCycleService.CycleDay;
                int oldTempDuration = _temperateWeatherDurationService.TemperateWeatherDuration;
                int newTempDuration = cycleDay + WEATHER_NOTICE_DAYS;
                int hazStartDay = _weatherService.HazardousWeatherStartCycleDay;
                int hazDuration = _hazardousWeatherService.HazardousWeatherDuration;
                var hazType = _hazardousWeatherService.CurrentCycleHazardousWeather;
                string typeName = hazType?.GetType().Name ?? "(null)";

                Debug.Log($"[Archipelago] Trap: scheduling Hazardous Weather via temperate-shortening. " +
                          $"Pre-state: cycle={cycle}, day={cycleDay}, IsHazardous={_weatherService.IsHazardousWeather}, " +
                          $"Type={typeName}, HazDuration={hazDuration}, " +
                          $"TempDuration={oldTempDuration}, HazStartDay={hazStartDay}");

                // Mark trap as in-flight. Cleared in OnCycleStarted (the post-trap
                // cycle) along with the SetForCycle reset that fixes the water-
                // spawn regression. While set, additional traps queue.
                _trapScheduledAwaitingTransition = true;

                if (newTempDuration < oldTempDuration)
                {
                    // Reflect-write the backing field. The publicizer didn't expose
                    // a public setter (despite the inspector reporting W=True for
                    // the property), so we write the underlying compiler-generated
                    // backing field directly. Same pattern as <AllNeeds>k__BackingField.
                    var serviceType = _temperateWeatherDurationService.GetType();
                    var backingField = serviceType.GetField(
                        "<TemperateWeatherDuration>k__BackingField",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    if (backingField != null)
                    {
                        backingField.SetValue(_temperateWeatherDurationService, newTempDuration);
                        Debug.Log($"[Archipelago] Shortened temperate: TemperateWeatherDuration " +
                                  $"{oldTempDuration} -> {newTempDuration} (current day {cycleDay}, " +
                                  $"notice {WEATHER_NOTICE_DAYS} days)");
                    }
                    else
                    {
                        Debug.LogWarning("[Archipelago] <TemperateWeatherDuration>k__BackingField not found via reflection — " +
                                         "trap will fire at the natural transition without shortening.");
                    }

                    // Also shorten GameCycleService._cycleDurationInDays. Discovered
                    // 2026-04-29 during Session J: the game caches total cycle length
                    // at cycle start as the sum of all ICycleDuration providers
                    // (TempDur + HazDur = 21 normally), and the cycle-end check in
                    // OnDaytimeStart compares CycleDay to this cached value. Mutating
                    // TempDur mid-cycle does not propagate to _cycleDurationInDays,
                    // so without this write the cycle still runs its full original
                    // length — hazardous extends from our shortened-temperate end
                    // all the way to the original 21-day cycle boundary, instead
                    // of running its intended HazDur=7 days.
                    var gameCycleType = _gameCycleService.GetType();
                    var cycleDurationField = gameCycleType.GetField(
                        "_cycleDurationInDays",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (cycleDurationField != null)
                    {
                        int oldCycleDur = (int)cycleDurationField.GetValue(_gameCycleService);
                        int newCycleDur = newTempDuration + hazDuration;
                        cycleDurationField.SetValue(_gameCycleService, newCycleDur);
                        Debug.Log($"[Archipelago] Shortened cycle duration: GameCycleService._cycleDurationInDays " +
                                  $"{oldCycleDur} -> {newCycleDur} (= newTempDur {newTempDuration} + HazDur {hazDuration})");
                    }
                    else
                    {
                        Debug.LogWarning("[Archipelago] GameCycleService._cycleDurationInDays not found via reflection — " +
                                         "cycle will still run its original length, hazardous will extend.");
                    }
                }
                else
                {
                    Debug.Log($"[Archipelago] TemperateWeatherDuration ({oldTempDuration}) is already <= " +
                              $"currentDay+{WEATHER_NOTICE_DAYS} ({newTempDuration}); leaving as-is. " +
                              $"Natural transition is already imminent.");
                }

                // Grep-friendly summary line. Note: HAZARDOUS_WEATHER queued (not
                // "fired") because the actual hazardous start happens later when
                // the natural game transition runs. The companion [Archipelago/Cycle]
                // log lines from CycleEventObserver will mark the actual transition.
                Debug.Log($"[Archipelago/Trap] HAZARDOUS_WEATHER queued: " +
                          $"cycle={cycle}, day={cycleDay}, " +
                          $"newTempDur={newTempDuration}, " +
                          $"preRolledType={typeName}, " +
                          $"awaiting natural transition.");

                ArchipelagoManager.PostLogMessage($"Trap incoming: Hazardous Weather in {WEATHER_NOTICE_DAYS} days!");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Failed to schedule Hazardous Weather: {ex.Message}");
                ArchipelagoManager.PostLogMessage("Trap: Hazardous Weather (failed to schedule)");
                // Don't leave the flag set if we failed to actually shorten anything.
                _trapScheduledAwaitingTransition = false;
            }
        }

        /// <summary>
        /// Called each tick by ArchipelagoTicker. Drains the weather trap queue
        /// when the in-flight trap has completed — i.e., the post-trap cycle has
        /// started (flag cleared by OnCycleStarted) and no hazardous is currently
        /// active.
        ///
        /// Note: this no longer flips _trapScheduledAwaitingTransition itself.
        /// Cleanup happens in OnCycleStarted, which is the post-trap cycle hook
        /// where TemperateWeatherDuration also gets re-randomized via SetForCycle.
        /// </summary>
        public void ProcessWeatherQueue()
        {
            if (_weatherTrapQueue.Count == 0) return;

            bool hazardousActive = _weatherService.IsHazardousWeather;
            if (hazardousActive || _trapScheduledAwaitingTransition) return;

            _weatherTrapQueue.Dequeue();
            Debug.Log($"[Archipelago] Dequeuing weather trap (remaining: {_weatherTrapQueue.Count})");
            ScheduleHazardousWeather();
        }

        /// <summary>
        /// Post-trap cycle hook. Fires when CycleStarted dispatches at the start
        /// of every cycle. When _trapScheduledAwaitingTransition is set, this is
        /// the cycle AFTER our trap-induced hazardous resolved.
        ///
        /// Passive cleanup: GameCycleService.StartNextCycle has already called
        /// SetForCycle on each ICycleDuration provider (re-randomizing TempDur)
        /// and recomputed _cycleDurationInDays from the fresh values. The new
        /// cycle is in a consistent state by the time CycleStartedEvent fires,
        /// so this hook just clears the in-flight flag and logs the post-trap
        /// state for diagnostic comparison against the pre-trap baseline.
        ///
        /// Calling SetForCycle here would re-randomize TempDur AFTER
        /// _cycleDurationInDays was computed, leaving them out of sync — the
        /// same class of bug we fix on the trap-arrival side by writing both
        /// fields together.
        /// </summary>
        [OnEvent]
        public void OnCycleStarted(CycleStartedEvent e)
        {
            if (!_trapScheduledAwaitingTransition) return;

            int cycle = _gameCycleService.Cycle;
            int tempDur = _temperateWeatherDurationService.TemperateWeatherDuration;
            int hazDur = _hazardousWeatherService.HazardousWeatherDuration;
            int cycleLen = -1;
            try
            {
                var cycleDurationField = _gameCycleService.GetType().GetField(
                    "_cycleDurationInDays",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (cycleDurationField != null)
                    cycleLen = (int)cycleDurationField.GetValue(_gameCycleService);
            }
            catch { /* diagnostic-only; fall back to -1 */ }

            Debug.Log($"[Archipelago] Post-trap cycle started: cycle={cycle}, TempDur={tempDur}, " +
                      $"HazDur={hazDur}, _cycleDurationInDays={cycleLen} " +
                      $"(expect TempDur+HazDur={tempDur + hazDur}). " +
                      $"Walk rivers in this temperate to verify water spawn.");

            _trapScheduledAwaitingTransition = false;
            Debug.Log("[Archipelago] Post-trap cleanup complete, queue drain re-enabled.");
        }

        /// <summary>
        /// Hungry Beavers / Thirsty Beavers: drops the Hunger or Thirst need of every
        /// beaver (not bots) to NeedTraps.CriticalPoints. Beavers already at or below
        /// that level are left alone. The change goes through
        /// NeedManager.ApplyEffect(InstantEffect), the path the game's own need
        /// debug buttons use, so the critical-state and minimum-state events fire
        /// (status icon, work penalties, death tracking) as for a normal change.
        /// Writes exactly one log line per trap.
        /// </summary>
        private void TriggerNeedTrap(string trapName)
        {
            if (!NeedTraps.TryGetNeedId(trapName, out var needId))
            {
                Debug.LogWarning($"[Archipelago] Unknown need trap: {trapName}");
                return;
            }

            var tally = new NeedTraps.Tally();
            string firstError = null;
            try
            {
                foreach (var needManager in _entityComponentRegistry.GetEnabled<NeedManager>())
                {
                    try
                    {
                        if (!needManager.HasNeed(needId))
                        {
                            tally.Skipped++;
                            continue;
                        }

                        var spec = needManager.GetNeedSpec(needId);
                        if (!NeedTraps.AppliesTo(true, needManager.NeedIsEnabled(needId), spec.CharacterType))
                        {
                            tally.Skipped++;
                            continue;
                        }

                        float before = needManager.GetNeedPoints(needId);
                        if (!NeedTraps.TryPlanDrop(before, spec.MinimumValue, spec.MaximumValue, spec.Effectiveness,
                                                   out var effectPoints, out _))
                        {
                            tally.AlreadyCritical++;
                            continue;
                        }

                        var effect = new InstantEffect(needId, effectPoints, 1);
                        needManager.ApplyEffect(in effect);

                        if (needManager.GetNeedPoints(needId) < before) tally.Lowered++;
                        else tally.Failed++;
                    }
                    catch (Exception ex)
                    {
                        tally.Failed++;
                        firstError ??= ex.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                firstError ??= ex.Message;
            }

            var tag = trapName.ToUpperInvariant().Replace(' ', '_');
            Debug.Log($"[Archipelago/Trap] {tag} fired: need={needId}, target={NeedTraps.CriticalPoints}, " +
                      $"{tally.Describe()}, cycle={_gameCycleService.Cycle}, day={_gameCycleService.CycleDay}" +
                      (firstError != null ? $", firstError={firstError}" : ""));

            if (tally.Lowered == 0 && firstError != null)
                ArchipelagoManager.PostLogMessage($"Trap: {trapName} (failed to apply)");
            else
                ArchipelagoManager.PostLogMessage($"Trap activated: {trapName}! ({tally.Lowered} beavers affected)");
        }

        // =================================================================
        // Filler (pending-goods ledger + storage delivery)
        // =================================================================

        private void HandleFiller(ApItem item)
        {
            // "Package: Logs" → amount from slot_data (or base × resource_package_percent);
            // legacy "Filler: 50 Logs" → 50 × percent. Seeds without the option are 100%.
            RefreshPackageSettings();
            if (!ResourcePackages.TryResolve(item.ItemName, _packagePercent, _slotPackages, out var delivery))
            {
                Debug.LogWarning($"[Archipelago] Unknown resource item: {item.ItemName}");
                return;
            }

            int amount = delivery.Amount;
            string displayName = delivery.DisplayName;
            string goodIdStr = delivery.GoodId;

            // Never touch inventories here: the goods wait in the saved ledger until
            // a delivery pass finds finished storage with room for them.
            _saveData.PendingGoods.Add(goodIdStr, amount);
            _nextDeliveryTime = 0f; // try to deliver on the next frame
            Debug.Log($"[Archipelago] Queued {amount} {goodIdStr} for delivery" +
                      $"{(item.IsReplay ? " (replayed from server history)" : "")}; " +
                      $"pending now: {_saveData.PendingGoods.Serialize()}");
            if (!item.IsReplay)
                ArchipelagoManager.PostReceivedItem(item, $"{amount} {displayName}");
        }

        /// <summary>Display name for a filler good id ("Plank" → "Planks").</summary>
        internal static string GoodDisplayName(string goodId) => ResourcePackages.GoodDisplayName(goodId);

        /// <summary>
        /// Reads resource_package_percent and resource_packages from slot_data once per
        /// connection. Missing keys (seeds generated before the option) leave the
        /// percent null and the table empty, which ResourcePackages treats as 100%.
        /// </summary>
        private void RefreshPackageSettings()
        {
            var slotData = ArchipelagoManager.SlotData;
            if (slotData == null || ReferenceEquals(slotData, _packageSlotData))
                return;
            _packageSlotData = slotData;
            _packagePercent = null;
            _slotPackages = new Dictionary<string, (string goodId, int amount)>();

            if (slotData.ContainsKey("resource_package_percent"))
                _packagePercent = ApGoalTracker.GetIntFromSlotData(
                    slotData, "resource_package_percent", ResourcePackages.DefaultPercent);

            if (slotData.TryGetValue("resource_packages", out var packagesObj)
                && packagesObj is Newtonsoft.Json.Linq.JObject packages)
            {
                foreach (var prop in packages.Properties())
                {
                    try
                    {
                        var goodId = (string)prop.Value["good_id"];
                        var amount = (int?)prop.Value["amount"] ?? 0;
                        if (!string.IsNullOrEmpty(goodId) && amount > 0)
                            _slotPackages[prop.Name] = (goodId, amount);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Archipelago] Ignoring resource_packages entry {prop.Name}: {ex.Message}");
                    }
                }
            }

            Debug.Log($"[Archipelago] Resource packages: percent={_packagePercent?.ToString() ?? "missing (100)"}, " +
                      $"{_slotPackages.Count} package amounts from slot_data");
        }

        /// <summary>
        /// Called every frame by ArchipelagoTicker. Every few seconds, while goods
        /// are pending, moves as much as fits into finished public storage that
        /// takes the good. The rest stays pending. Runs while disconnected too:
        /// the ledger is local save state.
        /// </summary>
        public void ProcessPendingGoods()
        {
            var ledger = _saveData.PendingGoods;
            if (ledger.IsEmpty)
            {
                _lastPendingSummary = null;
                return;
            }
            if (Time.unscaledTime < _nextDeliveryTime) return;
            _nextDeliveryTime = Time.unscaledTime + DeliveryIntervalSeconds;

            var deliveryMode = _saveData.DeliveryMode;
            bool districtCenterFirst = deliveryMode == GoodsDeliveryMode.DistrictCenter;
            if (_lastDeliveryMode != deliveryMode)
            {
                _lastDeliveryMode = deliveryMode;
                Debug.Log($"[Archipelago] Delivery mode: {GoodsDeliveryOption.Describe(deliveryMode)} (goods_delivery)");
            }

            List<IGoodsStorageSlot> slots;
            DistrictCenterStorageSlot districtCenterSlot = null;
            try
            {
                slots = new List<IGoodsStorageSlot>();
                if (districtCenterFirst)
                {
                    districtCenterSlot = FindDeliveryDistrictCenter();
                    if (districtCenterSlot != null) slots.Add(districtCenterSlot);
                }
                slots.AddRange(_entityComponentRegistry.GetEnabled<Stockpile>()
                    .Where(stockpile => stockpile.Inventory != null)
                    .Select(stockpile => (IGoodsStorageSlot)new StockpileStorageSlot(stockpile.Inventory)));
            }
            catch (Exception ex)
            {
                LogPendingOnce($"[Archipelago] Could not enumerate storage for pending goods: {ex.Message}", warning: true);
                return;
            }

            var result = ledger.Deliver(slots, (slot, goodId, amount) =>
            {
                if (slot is DistrictCenterStorageSlot districtCenter)
                    districtCenter.Inventory.GiveExistingIgnoringCapacity(new GoodAmount(goodId, amount));
                else
                    ((StockpileStorageSlot)slot).Inventory.GiveExisting(new GoodAmount(goodId, amount));
            });

            foreach (var delivered in result.Delivered.GroupBy(d => (d.GoodId, ToDistrictCenter: d.Slot is DistrictCenterStorageSlot)))
            {
                int total = delivered.Sum(d => d.Amount);
                string goodId = delivered.Key.GoodId;
                if (delivered.Key.ToDistrictCenter)
                {
                    var name = ((DistrictCenterStorageSlot)delivered.First().Slot).Name;
                    Debug.Log($"[Archipelago] Delivery path=DistrictCenter: {total} {goodId} into '{name}' " +
                              $"(now holds {districtCenterSlot?.Inventory.AmountInStock(goodId)}); still waiting: {ledger.Get(goodId)}");
                    ArchipelagoManager.PostLogMessage($"Delivered {total} {GoodDisplayName(goodId)} to the District Center");
                }
                else
                {
                    Debug.Log($"[Archipelago] Delivery path=Stockpile: {total} {goodId} into " +
                              $"{delivered.Count()} storage building(s); still waiting: {ledger.Get(goodId)}");
                    ArchipelagoManager.PostLogMessage($"Delivered {total} {GoodDisplayName(goodId)} to storage");
                }
            }
            foreach (var (delivery, error) in result.Failed)
                Debug.LogWarning($"[Archipelago] {(delivery.Slot is DistrictCenterStorageSlot ? "District Center" : "Storage")} " +
                                 $"rejected {delivery.Amount} {delivery.GoodId}; it stays pending: " +
                                 $"{error.InnerException?.Message ?? error.Message}");

            if (!ledger.IsEmpty)
                LogPendingOnce($"[Archipelago] Delivery path=Pending: waiting for storage: {ledger.Serialize()} " +
                               $"({(districtCenterSlot != null ? "District Center '" + districtCenterSlot.Name + "' and " : "")}" +
                               $"{slots.Count - (districtCenterSlot != null ? 1 : 0)} finished stockpile(s) checked)", warning: false);
            else
                _lastPendingSummary = null;
        }

        /// <summary>
        /// The finished District Center with the most beavers (first one on a tie),
        /// or null when there is none or its output inventory is not active.
        /// </summary>
        private DistrictCenterStorageSlot FindDeliveryDistrictCenter()
        {
            DistrictCenter best = null;
            Inventory bestInventory = null;
            int bestPopulation = -1;
            foreach (var districtCenter in _districtCenterRegistry.FinishedDistrictCenters)
            {
                if (districtCenter == null) continue;
                var output = districtCenter.GetComponent<SimpleOutputInventory>();
                var inventory = output != null ? output.Inventory : null;
                if (inventory == null || !inventory.Enabled) continue;
                var population = districtCenter.DistrictPopulation;
                int count = population != null ? population.NumberOfAdults + population.NumberOfChildren : 0;
                if (count > bestPopulation)
                {
                    best = districtCenter;
                    bestInventory = inventory;
                    bestPopulation = count;
                }
            }
            return best == null ? null : new DistrictCenterStorageSlot(bestInventory, best.DistrictName);
        }

        /// <summary>Logs the waiting summary only when the pending amounts change, not every pass.</summary>
        private void LogPendingOnce(string message, bool warning)
        {
            var summary = _saveData.PendingGoods.Serialize();
            if (summary == _lastPendingSummary) return;
            _lastPendingSummary = summary;
            if (warning) Debug.LogWarning(message);
            else Debug.Log(message);
        }

        /// <summary>
        /// Game adapter for the District Center's output inventory
        /// (SimpleOutputInventory: all goods allowed as output, PublicOutput, 20 per
        /// good with ignorable capacity). It is not a public input, so the planner
        /// flag reports true here on purpose: we deliver the way the game adds
        /// starting goods, with GiveExistingIgnoringCapacity, and amounts over 20
        /// count as unwanted stock that its workers haul out to storage.
        /// </summary>
        private sealed class DistrictCenterStorageSlot : IGoodsStorageSlot
        {
            public Inventory Inventory { get; }
            public string Name { get; }

            public DistrictCenterStorageSlot(Inventory inventory, string name)
            {
                Inventory = inventory;
                Name = string.IsNullOrEmpty(name) ? "District Center" : name;
            }

            public bool PublicInput => true;

            public bool Enabled => Inventory.Enabled;

            // Gives() = the good is in the inventory's allowed output goods.
            public bool Accepts(string goodId)
            {
                try { return Inventory.Gives(goodId); }
                catch { return false; }
            }

            public int FreeCapacity(string goodId) => int.MaxValue;
        }

        /// <summary>Game adapter for the pending-goods planner: one stockpile inventory.</summary>
        private sealed class StockpileStorageSlot : IGoodsStorageSlot
        {
            public Inventory Inventory { get; }

            public StockpileStorageSlot(Inventory inventory)
            {
                Inventory = inventory;
            }

            public bool PublicInput => Inventory.PublicInput;

            // Stockpile.OnEnterFinishedState enables the inventory; construction
            // sites and demolished buildings have it disabled.
            public bool Enabled => Inventory.Enabled;

            public bool Accepts(string goodId)
            {
                try { return Inventory.Takes(goodId); }
                catch { return false; }
            }

            // UnreservedCapacity honours the storage's good filter, total capacity
            // and capacity already reserved by haulers on their way.
            public int FreeCapacity(string goodId)
            {
                try { return Inventory.UnreservedCapacity(goodId); }
                catch { return 0; }
            }
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
            // Grep-friendly summary line — one per boost-apply event (covers
            // both initial activation in HandleBoost and PostLoad re-application).
            Debug.Log($"[Archipelago/Boost] {boostName.Replace(" ", "_").ToUpperInvariant()} applied: " +
                      $"bonusId={mapping.bonusId}, delta=+{mapping.delta}, affected={affected}");
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
