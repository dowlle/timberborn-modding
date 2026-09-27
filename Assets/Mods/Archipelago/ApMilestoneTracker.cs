using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Timberborn.GameWonderCompletion;
using Timberborn.HazardousWeatherSystem;
using Timberborn.Population;
using Timberborn.ResourceCountingSystem;
using Timberborn.SingletonSystem;
using Timberborn.Wellbeing;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Polls game services each tick to detect milestone completion and send AP
    /// location checks.  Milestone definitions come from slot_data so the APWorld
    /// controls which milestones exist and their thresholds.
    /// </summary>
    public class ApMilestoneTracker : ILoadableSingleton, IUnloadableSingleton
    {
        /// <summary>Static reference for ArchipelagoTicker to call without DI.</summary>
        internal static ApMilestoneTracker Instance { get; private set; }

        private readonly PopulationService _populationService;
        private readonly WellbeingService _wellbeingService;
        private readonly HazardousWeatherHistory _weatherHistory;
        private readonly HazardSurvivalTracker _survival;
        private readonly WonderCompletionCountdownStarter _wonderCountdown;
        private readonly ResourceCountingService _resourceCountingService;
        private readonly ArchipelagoSaveData _saveData;

        private List<MilestoneDefinition> _milestones = new();
        private HashSet<long> _checkedMilestoneIds = new();

        // Resource milestones already warned about this load, so a milestone that
        // cannot be evaluated logs once instead of every tick.
        private readonly HashSet<long> _warnedResourceMilestoneIds = new();

        // Track "first beaver born / grown" via population deltas
        private bool _everSawBirth;
        private bool _everSawGrowth;
        private int _lastPopulation = -1;
        private int _lastAdults = -1;

        // Baselines are stored in ArchipelagoSaveData for persistence across save/load.

        public ApMilestoneTracker(
            PopulationService populationService,
            WellbeingService wellbeingService,
            HazardousWeatherHistory weatherHistory,
            HazardSurvivalTracker survival,
            WonderCompletionCountdownStarter wonderCountdown,
            ResourceCountingService resourceCountingService,
            ArchipelagoSaveData saveData)
        {
            _populationService = populationService;
            _wellbeingService = wellbeingService;
            _weatherHistory = weatherHistory;
            _survival = survival;
            _wonderCountdown = wonderCountdown;
            _resourceCountingService = resourceCountingService;
            _saveData = saveData;
        }

        public void Load()
        {
            Instance = this;

            // Subscribe to milestone data arriving (from save or slot_data)
            ArchipelagoSaveData.OnMilestonesAvailable += LoadMilestoneDefinitions;

            // If milestones are already loaded (from save data), use them now
            if (_saveData.Milestones != null && _saveData.Milestones.Count > 0)
                LoadMilestoneDefinitions();
        }

        public void Unload()
        {
            ArchipelagoSaveData.OnMilestonesAvailable -= LoadMilestoneDefinitions;
            if (Instance == this)
                Instance = null;
        }

        private void LoadMilestoneDefinitions()
        {
            _milestones = _saveData.Milestones ?? new List<MilestoneDefinition>();
            _checkedMilestoneIds = new HashSet<long>(_saveData.CheckedMilestoneIds);
            _warnedResourceMilestoneIds.Clear();

            // Snapshot the game's rolled hazard counts. Survival now counts ended hazards
            // (HazardSurvivalTracker); the baselines stay for saves from older versions.
            if (_saveData.BaselineDroughtCount < 0)
                _saveData.BaselineDroughtCount = _weatherHistory.GetCyclesCount("DroughtWeather");
            if (_saveData.BaselineBadtideCount < 0)
                _saveData.BaselineBadtideCount = _weatherHistory.GetCyclesCount("BadtideWeather");

            Debug.Log($"[Archipelago] MilestoneTracker loaded {_milestones.Count} milestones " +
                      $"({_checkedMilestoneIds.Count} already checked), " +
                      $"baseline droughts={_saveData.BaselineDroughtCount} badtides={_saveData.BaselineBadtideCount}");
        }

        /// <summary>
        /// Called each frame by ArchipelagoTicker.  Evaluates all unchecked milestones.
        /// </summary>
        public void CheckMilestones()
        {
            if (!ArchipelagoManager.IsConnected || _milestones.Count == 0)
                return;

            foreach (var milestone in _milestones)
            {
                if (_checkedMilestoneIds.Contains(milestone.LocationId))
                    continue;

                if (EvaluateCondition(milestone))
                {
                    ArchipelagoManager.SendLocationCheck(milestone.LocationId);
                    _checkedMilestoneIds.Add(milestone.LocationId);
                    _saveData.CheckedMilestoneIds.Add(milestone.LocationId);
                    ArchipelagoManager.PostLogMessage($"Milestone: {milestone.Name}");
                    Debug.Log($"[Archipelago] Milestone completed: {milestone.Name}");
                }
            }
        }

        private bool EvaluateCondition(MilestoneDefinition m)
        {
            switch (m.Type)
            {
                case "population":
                    return EvaluatePopulation(m);
                case "wellbeing":
                    return EvaluateWellbeing(m);
                case "survival":
                    return EvaluateSurvival(m);
                case "wonder":
                    return EvaluateWonder(m);
                case "resource":
                    return EvaluateResource(m);
                default:
                    return false;
            }
        }

        private bool EvaluatePopulation(MilestoneDefinition m)
        {
            var popData = _populationService.GlobalPopulationData;
            int currentPop = popData.NumberOfBeavers;
            int currentAdults = popData.NumberOfAdults;

            // Detect "First Beaver Born" — population increased (new birth)
            if (m.Name.Contains("First Beaver Born"))
            {
                if (_lastPopulation >= 0 && currentPop > _lastPopulation)
                    _everSawBirth = true;
                _lastPopulation = currentPop;
                return _everSawBirth;
            }

            // Detect "First Beaver Grown Up" — adult count increased
            if (m.Name.Contains("First Beaver Grown Up"))
            {
                if (_lastAdults >= 0 && currentAdults > _lastAdults)
                    _everSawGrowth = true;
                _lastAdults = currentAdults;
                return _everSawGrowth;
            }

            // Numeric population threshold
            return currentPop >= m.Threshold;
        }

        private bool EvaluateWellbeing(MilestoneDefinition m)
        {
            // AverageGlobalWellbeing is an int (0-20 scale matching in-game display)
            int wellbeing = _wellbeingService.AverageGlobalWellbeing;
            return wellbeing >= m.Threshold;
        }

        private bool EvaluateSurvival(MilestoneDefinition m)
        {
            // Hazards count when they end, and only while this save is bound to the slot.
            if (m.Name.Contains("Drought"))
                return _survival.SurvivedDroughts >= m.Threshold;

            if (m.Name.Contains("Badtide"))
                return _survival.SurvivedBadtides >= m.Threshold;

            return false;
        }

        private bool EvaluateWonder(MilestoneDefinition m)
        {
            // CountdownFinished is saved per game. The game's own "completed with this
            // faction" check reads the player profile per map, so it is also true for a
            // wonder finished in an earlier game on the same map.
            return _wonderCountdown.CountdownFinished;
        }

        private bool EvaluateResource(MilestoneDefinition m)
        {
            if (string.IsNullOrEmpty(m.GoodId))
            {
                if (_warnedResourceMilestoneIds.Add(m.LocationId))
                    Debug.LogWarning($"[Archipelago] Resource milestone '{m.Name}' has no GoodId — skipping");
                return false;
            }

            try
            {
                var resourceCount = _resourceCountingService.GetGlobalResourceCount(m.GoodId);
                return resourceCount.AllStock >= m.Threshold;
            }
            catch (Exception ex)
            {
                if (_warnedResourceMilestoneIds.Add(m.LocationId))
                    Debug.LogWarning($"[Archipelago] Resource milestone '{m.Name}' cannot count GoodId '{m.GoodId}' — skipping: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Parse milestone definitions from slot_data.
        /// Called by ArchipelagoSaveData when slot_data arrives.
        /// </summary>
        public static List<MilestoneDefinition> ParseFromSlotData(object milestonesObj)
        {
            var result = new List<MilestoneDefinition>();

            JArray arr = milestonesObj as JArray;
            if (arr == null && milestonesObj is JToken token)
                arr = token as JArray;
            if (arr == null && milestonesObj is string str)
            {
                try { arr = JArray.Parse(str); }
                catch { /* not valid JSON */ }
            }

            if (arr == null)
            {
                Debug.LogWarning($"[Archipelago] Cannot parse milestones from slot_data: " +
                                 $"type={milestonesObj?.GetType().FullName}");
                return result;
            }

            foreach (var item in arr)
            {
                var name = item["name"]?.ToString() ?? "";
                result.Add(new MilestoneDefinition
                {
                    Name = name,
                    LocationId = item["location_id"]?.ToObject<long>() ?? 0,
                    Type = item["type"]?.ToString() ?? "unknown",
                    Threshold = item["threshold"]?.ToObject<int>() ?? 0,
                    GoodId = MilestoneCodec.ResolveGoodId(name, item["good_id"]?.ToString()),
                });
            }

            Debug.Log($"[Archipelago] Parsed {result.Count} milestones from slot_data");
            return result;
        }
    }
}
