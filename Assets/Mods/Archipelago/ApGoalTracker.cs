using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// Definition of an active goal parsed from slot_data.
    /// </summary>
    public class GoalDefinition
    {
        public string Name;       // "Wonder", "Population", "Droughts", "Badtides", "Well-being", "Bots", "Water Storage"
        public int    Threshold;  // target value (0 for Wonder)
    }

    /// <summary>
    /// Polls game services each tick to detect goal completion and sends
    /// Victory events + overall goal achieved to the AP server.
    ///
    /// Goal definitions come from slot_data so the APWorld controls which
    /// goals are active, their thresholds, and the requirement mode (any/all).
    /// </summary>
    public class ApGoalTracker : ILoadableSingleton, IUnloadableSingleton
    {
        internal static ApGoalTracker Instance { get; private set; }

        private readonly PopulationService _populationService;
        private readonly WellbeingService _wellbeingService;
        private readonly HazardousWeatherHistory _weatherHistory;
        private readonly GameWonderCompletionService _wonderService;
        private readonly ResourceCountingService _resourceCountingService;
        private readonly ArchipelagoSaveData _saveData;

        private List<GoalDefinition> _goals = new();
        private HashSet<string> _completedGoals = new();
        private bool _goalAchieved;

        // 0 = any, 1 = all
        private int _goalRequirement;

        // Population mode: 0 = beavers_only, 1 = bots_only, 2 = beavers_and_bots
        private int _populationMode;

        // Cached reflection for wonder
        private MethodInfo _isWonderCompletedMethod;

        public ApGoalTracker(
            PopulationService populationService,
            WellbeingService wellbeingService,
            HazardousWeatherHistory weatherHistory,
            GameWonderCompletionService wonderService,
            ResourceCountingService resourceCountingService,
            ArchipelagoSaveData saveData)
        {
            _populationService = populationService;
            _wellbeingService = wellbeingService;
            _weatherHistory = weatherHistory;
            _wonderService = wonderService;
            _resourceCountingService = resourceCountingService;
            _saveData = saveData;
        }

        public void Load()
        {
            Instance = this;
            ArchipelagoSaveData.OnGoalsAvailable += LoadGoalDefinitions;

            if (_saveData.Goals != null && _saveData.Goals.Count > 0)
                LoadGoalDefinitions();
        }

        public void Unload()
        {
            ArchipelagoSaveData.OnGoalsAvailable -= LoadGoalDefinitions;
            if (Instance == this)
                Instance = null;
        }

        private void LoadGoalDefinitions()
        {
            _goals = _saveData.Goals ?? new List<GoalDefinition>();
            _goalRequirement = _saveData.GoalRequirement;
            _populationMode = _saveData.PopulationMode;
            _completedGoals = new HashSet<string>(_saveData.CompletedGoals);
            _goalAchieved = _saveData.GoalAchieved;
            Debug.Log($"[Archipelago] GoalTracker loaded {_goals.Count} goals " +
                      $"(requirement={(_goalRequirement == 1 ? "all" : "any")}, " +
                      $"completed={_completedGoals.Count}, achieved={_goalAchieved})");
        }

        /// <summary>
        /// Called each frame by ArchipelagoTicker.
        /// </summary>
        public void CheckGoals()
        {
            if (!ArchipelagoManager.IsConnected || _goals.Count == 0 || _goalAchieved)
                return;

            bool anyNewCompletion = false;

            foreach (var goal in _goals)
            {
                if (_completedGoals.Contains(goal.Name))
                    continue;

                if (EvaluateGoal(goal))
                {
                    _completedGoals.Add(goal.Name);
                    _saveData.CompletedGoals.Add(goal.Name);
                    anyNewCompletion = true;

                    // Wonder is purely logic-checked server-side, no event needed.
                    // All other goals send a Victory event.
                    if (goal.Name != "Wonder")
                    {
                        var eventName = $"Victory: {goal.Name}";
                        ArchipelagoManager.SendLocationCheck(eventName);
                        Debug.Log($"[Archipelago] Goal completed: {goal.Name} — sent {eventName}");
                    }
                    else
                    {
                        Debug.Log($"[Archipelago] Goal completed: Wonder (server-side logic check)");
                    }

                    ArchipelagoManager.PostLogMessage($"Goal completed: {goal.Name}!");
                }
            }

            if (anyNewCompletion)
                CheckOverallCompletion();
        }

        private void CheckOverallCompletion()
        {
            bool isComplete;

            if (_goalRequirement == 1) // all
            {
                isComplete = _goals.All(g => _completedGoals.Contains(g.Name));
            }
            else // any
            {
                isComplete = _goals.Any(g => _completedGoals.Contains(g.Name));
            }

            if (isComplete)
            {
                _goalAchieved = true;
                _saveData.GoalAchieved = true;
                ArchipelagoManager.SendGoalCompleted();
                ArchipelagoManager.PostLogMessage("All victory conditions met — game complete!");
                Debug.Log("[Archipelago] Overall goal achieved!");
            }
        }

        private bool EvaluateGoal(GoalDefinition goal)
        {
            switch (goal.Name)
            {
                case "Wonder":
                    return EvaluateWonder();
                case "Population":
                    return EvaluatePopulation(goal.Threshold);
                case "Droughts":
                    return EvaluateDroughts(goal.Threshold);
                case "Badtides":
                    return EvaluateBadtides(goal.Threshold);
                case "Well-being":
                    return EvaluateWellbeing(goal.Threshold);
                case "Bots":
                    return EvaluateBots(goal.Threshold);
                case "Water Storage":
                    return EvaluateWaterStorage(goal.Threshold);
                default:
                    Debug.LogWarning($"[Archipelago] Unknown goal type: {goal.Name}");
                    return false;
            }
        }

        private bool EvaluateWonder()
        {
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

        private bool EvaluatePopulation(int threshold)
        {
            var popData = _populationService.GlobalPopulationData;
            int beavers = popData.NumberOfBeavers;
            int bots = GetBotCount();
            int count;

            switch (_populationMode)
            {
                case 1: // bots_only
                    count = bots;
                    break;
                case 2: // beavers_and_bots
                    count = beavers + bots;
                    break;
                default: // beavers_only
                    count = beavers;
                    break;
            }

            return count >= threshold;
        }

        private int GetBotCount()
        {
            return _populationService.GlobalPopulationData.NumberOfBots;
        }

        private bool EvaluateBots(int threshold)
        {
            return GetBotCount() >= threshold;
        }

        private bool EvaluateWaterStorage(int threshold)
        {
            try
            {
                // GoodId may be a struct/record — construct via reflection
                var goodIdType = typeof(ResourceCountingService).Assembly
                    .GetType("Timberborn.Goods.GoodId")
                    ?? Type.GetType("Timberborn.Goods.GoodId, Timberborn.Goods");

                if (goodIdType == null)
                {
                    Debug.LogWarning("[Archipelago] Could not find GoodId type");
                    return false;
                }

                var waterGoodId = Activator.CreateInstance(goodIdType, "Water");

                // Call GetGlobalResourceCount via reflection since we can't reference GoodId directly
                var method = _resourceCountingService.GetType().GetMethod("GetGlobalResourceCount");
                if (method == null)
                {
                    Debug.LogWarning("[Archipelago] Could not find GetGlobalResourceCount method");
                    return false;
                }

                var resourceCount = method.Invoke(_resourceCountingService, new[] { waterGoodId });
                var allStockProp = resourceCount.GetType().GetProperty("AllStock");
                if (allStockProp == null)
                {
                    Debug.LogWarning("[Archipelago] Could not find AllStock property on ResourceCount");
                    return false;
                }

                var allStock = Convert.ToInt32(allStockProp.GetValue(resourceCount));
                return allStock >= threshold;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Water storage check failed: {ex.Message}");
                return false;
            }
        }

        private bool EvaluateDroughts(int threshold)
        {
            int count = _weatherHistory.GetCyclesCount("Drought");
            return count >= threshold;
        }

        private bool EvaluateBadtides(int threshold)
        {
            int count = _weatherHistory.GetCyclesCount("Badtide");
            return count >= threshold;
        }

        private bool EvaluateWellbeing(int threshold)
        {
            int wellbeing = _wellbeingService.AverageGlobalWellbeing;
            return wellbeing >= threshold;
        }

        /// <summary>
        /// Parse goal definitions from slot_data.
        /// </summary>
        public static List<GoalDefinition> ParseGoalsFromSlotData(Dictionary<string, object> slotData)
        {
            var result = new List<GoalDefinition>();
            if (slotData == null) return result;

            // Parse the goals list (string array)
            if (!slotData.TryGetValue("goals", out var goalsObj))
                return result;

            var goalNames = new List<string>();
            if (goalsObj is JArray jArr)
            {
                goalNames = jArr.Select(t => t.ToString()).ToList();
            }
            else if (goalsObj is JToken jToken && jToken.Type == JTokenType.Array)
            {
                goalNames = ((JArray)jToken).Select(t => t.ToString()).ToList();
            }

            // Map each goal name to its threshold from slot_data
            foreach (var name in goalNames)
            {
                int threshold = 0;
                switch (name)
                {
                    case "Population":
                        threshold = GetIntFromSlotData(slotData, "population_goal", 100);
                        break;
                    case "Droughts":
                        threshold = GetIntFromSlotData(slotData, "drought_cycles_goal", 25);
                        break;
                    case "Badtides":
                        threshold = GetIntFromSlotData(slotData, "badtide_cycles_goal", 10);
                        break;
                    case "Well-being":
                        threshold = GetIntFromSlotData(slotData, "wellbeing_goal", 15);
                        break;
                    case "Bots":
                        threshold = GetIntFromSlotData(slotData, "bots_goal", 10);
                        break;
                    case "Water Storage":
                        threshold = GetIntFromSlotData(slotData, "water_storage_goal", 5000);
                        break;
                    case "Wonder":
                        threshold = 0; // boolean check
                        break;
                }

                result.Add(new GoalDefinition { Name = name, Threshold = threshold });
            }

            Debug.Log($"[Archipelago] Parsed {result.Count} goals from slot_data: " +
                      string.Join(", ", result.Select(g => $"{g.Name}({g.Threshold})")));
            return result;
        }

        internal static int GetIntFromSlotData(Dictionary<string, object> slotData, string key, int defaultValue)
        {
            if (!slotData.TryGetValue(key, out var val))
                return defaultValue;

            if (val is long l) return (int)l;
            if (val is int i) return i;
            if (val is JValue jv) return jv.ToObject<int>();
            if (int.TryParse(val?.ToString(), out var parsed)) return parsed;
            return defaultValue;
        }
    }
}
