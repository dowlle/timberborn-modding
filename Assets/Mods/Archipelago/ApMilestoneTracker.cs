using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Timberborn.GameWonderCompletion;
using Timberborn.HazardousWeatherSystem;
using Timberborn.Population;
using Timberborn.SingletonSystem;
using Timberborn.Wellbeing;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Definition of a single milestone, parsed from slot_data.
    /// </summary>
    public class MilestoneDefinition
    {
        public string Name;        // "Population: Reach 10 Beavers"
        public long   LocationId;  // AP location ID
        public string Type;        // "population", "wellbeing", "survival", "wonder"
        public int    Threshold;   // numeric threshold (10, 5, 1, etc.)
    }

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
        private readonly GameWonderCompletionService _wonderService;
        private readonly ArchipelagoSaveData _saveData;

        private List<MilestoneDefinition> _milestones = new();
        private HashSet<long> _checkedMilestoneIds = new();

        // Track "first beaver born / grown" via population deltas
        private bool _everSawBirth;
        private bool _everSawGrowth;
        private int _lastPopulation = -1;
        private int _lastAdults = -1;

        // Cached reflection for wonder completion (runtime enforces access on publicized internals)
        private MethodInfo _isWonderCompletedMethod;

        public ApMilestoneTracker(
            PopulationService populationService,
            WellbeingService wellbeingService,
            HazardousWeatherHistory weatherHistory,
            GameWonderCompletionService wonderService,
            ArchipelagoSaveData saveData)
        {
            _populationService = populationService;
            _wellbeingService = wellbeingService;
            _weatherHistory = weatherHistory;
            _wonderService = wonderService;
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
            Debug.Log($"[Archipelago] MilestoneTracker loaded {_milestones.Count} milestones " +
                      $"({_checkedMilestoneIds.Count} already checked)");
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
            // HazardousWeatherId values are "DroughtWeather" and "BadtideWeather"
            // (matching the class names, not the short display names)
            if (m.Name.Contains("Drought"))
            {
                int droughtCount = _weatherHistory.GetCyclesCount("DroughtWeather");
                return droughtCount >= m.Threshold;
            }

            if (m.Name.Contains("Badtide"))
            {
                int badtideCount = _weatherHistory.GetCyclesCount("BadtideWeather");
                return badtideCount >= m.Threshold;
            }

            return false;
        }

        private bool EvaluateWonder(MilestoneDefinition m)
        {
            // Runtime enforces access on publicized internal methods — must use reflection
            if (_isWonderCompletedMethod == null)
            {
                _isWonderCompletedMethod = _wonderService.GetType().GetMethod(
                    "IsWonderCompletedWithCurrentFaction",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (_isWonderCompletedMethod == null)
                {
                    Debug.LogWarning("[Archipelago] Could not find IsWonderCompletedWithCurrentFaction method");
                    return false;
                }
            }

            return (bool)_isWonderCompletedMethod.Invoke(_wonderService, null);
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
                result.Add(new MilestoneDefinition
                {
                    Name = item["name"]?.ToString() ?? "",
                    LocationId = item["location_id"]?.ToObject<long>() ?? 0,
                    Type = item["type"]?.ToString() ?? "unknown",
                    Threshold = item["threshold"]?.ToObject<int>() ?? 0,
                });
            }

            Debug.Log($"[Archipelago] Parsed {result.Count} milestones from slot_data");
            return result;
        }
    }
}
