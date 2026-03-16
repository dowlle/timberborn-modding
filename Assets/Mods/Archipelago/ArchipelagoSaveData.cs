using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.GameFactionSystem;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// One entry in the branching shop layout received from slot_data.
    /// </summary>
    public class ShopSlot
    {
        public string Path;      // "A", "B", "C", "D"
        public int    Level;     // position within path (0-based)
        public long   LocationId;
        public int    Price;     // science cost
        public int    Tier;      // tier gate (1-5)
    }

    /// <summary>
    /// Persists Archipelago state to the Timberborn save file:
    /// - ProcessedItemIndex (so reconnects don't replay items)
    /// - Connection data (for auto-reconnect)
    /// - Checked AP locations (shop state)
    /// - Received AP items (tier gate state)
    /// - Shop layout (from slot_data, for branching shop)
    /// - Skips available
    /// </summary>
    public class ArchipelagoSaveData : ISaveableSingleton, ILoadableSingleton
    {
        private static readonly SingletonKey ArchipelagoKey = new("Archipelago");
        private static readonly PropertyKey<int> ProcessedIndexKey = new("ProcessedItemIndex");
        private static readonly PropertyKey<string> HostKey = new("Host");
        private static readonly PropertyKey<int> PortKey = new("Port");
        private static readonly PropertyKey<string> SlotKey = new("Slot");
        private static readonly PropertyKey<string> SeedKey = new("Seed");
        private static readonly PropertyKey<string> CheckedLocsKey = new("CheckedLocations");
        private static readonly PropertyKey<string> ReceivedItemsKey = new("ReceivedItems");
        private static readonly PropertyKey<string> ShopLayoutKey = new("ShopLayout");
        private static readonly PropertyKey<int> SkipsAvailableKey = new("SkipsAvailable");
        private static readonly PropertyKey<string> MilestonesKey = new("Milestones");
        private static readonly PropertyKey<string> CheckedMilestonesKey = new("CheckedMilestones");
        private static readonly PropertyKey<string> ProgressiveChainsKey = new("ProgressiveChains");
        private static readonly PropertyKey<string> ProgressiveCountersKey = new("ProgressiveCounters");
        private static readonly PropertyKey<string> GoalsKey = new("Goals");
        private static readonly PropertyKey<int> GoalRequirementKey = new("GoalRequirement");
        private static readonly PropertyKey<int> PopulationModeKey = new("PopulationMode");
        private static readonly PropertyKey<string> CompletedGoalsKey = new("CompletedGoals");
        private static readonly PropertyKey<int> GoalAchievedKey = new("GoalAchieved");

        /// <summary>Fired when ShopLayout becomes available (from save or slot_data).</summary>
        public static event Action OnShopLayoutAvailable;

        /// <summary>Fired when milestone definitions become available (from save or slot_data).</summary>
        public static event Action OnMilestonesAvailable;

        /// <summary>Fired when goal definitions become available (from save or slot_data).</summary>
        public static event Action OnGoalsAvailable;

        private readonly ISingletonLoader _singletonLoader;
        private readonly FactionService _factionService;

        private string _savedHost;
        private int _savedPort;
        private string _savedSlot;
        private string _savedSeed;

        /// <summary>
        /// Checked AP location names, persisted across save/load.
        /// Read by ApShopPanel at Load time.
        /// </summary>
        public HashSet<string> CheckedLocations { get; } = new();

        /// <summary>
        /// Received AP item names, persisted across save/load.
        /// Used by ApShopPanel for tier gate evaluation.
        /// </summary>
        public HashSet<string> ReceivedItems { get; } = new();

        /// <summary>
        /// The branching shop layout from slot_data.
        /// Persisted so the shop works offline after first connect.
        /// </summary>
        public List<ShopSlot> ShopLayout { get; private set; }

        /// <summary>Number of Skip items available to spend.</summary>
        public int SkipsAvailable { get; set; }

        /// <summary>Milestone definitions from slot_data (persisted for offline use).</summary>
        public List<MilestoneDefinition> Milestones { get; private set; }

        /// <summary>AP location IDs of milestones already checked.</summary>
        public HashSet<long> CheckedMilestoneIds { get; } = new();

        /// <summary>
        /// Progressive item chains from slot_data.
        /// Maps progressive item name → ordered list of building names.
        /// </summary>
        public Dictionary<string, List<string>> ProgressiveChains { get; private set; } = new();

        /// <summary>
        /// Tracks how many times each progressive item has been received.
        /// Persisted so reconnects don't double-unlock buildings.
        /// </summary>
        public Dictionary<string, int> ProgressiveCounters { get; private set; } = new();

        /// <summary>Goal definitions from slot_data (persisted for offline use).</summary>
        public List<GoalDefinition> Goals { get; private set; }

        /// <summary>Goal requirement mode: 0=any, 1=all.</summary>
        public int GoalRequirement { get; set; }

        /// <summary>Population mode: 0=beavers_only, 1=bots_only, 2=beavers_and_bots.</summary>
        public int PopulationMode { get; set; }

        /// <summary>Names of goals already completed by the player.</summary>
        public HashSet<string> CompletedGoals { get; } = new();

        /// <summary>Whether the overall goal has been achieved (sent to server).</summary>
        public bool GoalAchieved { get; set; }

        public ArchipelagoSaveData(ISingletonLoader singletonLoader, FactionService factionService)
        {
            _singletonLoader = singletonLoader;
            _factionService = factionService;
        }

        public void Load()
        {
            // Always subscribe to connection events — even on fresh saves with no AP data
            ArchipelagoManager.OnConnectionChanged += OnConnectionChanged;

            if (!_singletonLoader.TryGetSingleton(ArchipelagoKey, out var loader))
            {
                Debug.Log("[Archipelago] No saved AP data found in this save file.");
                return;
            }

            ArchipelagoManager.ProcessedItemIndex = loader.Get(ProcessedIndexKey);

            if (loader.Has(HostKey))
                _savedHost = loader.Get(HostKey);
            if (loader.Has(PortKey))
                _savedPort = loader.Get(PortKey);
            if (loader.Has(SlotKey))
                _savedSlot = loader.Get(SlotKey);
            if (loader.Has(SeedKey))
                _savedSeed = loader.Get(SeedKey);

            // Restore shop state
            if (loader.Has(CheckedLocsKey))
            {
                var raw = loader.Get(CheckedLocsKey);
                if (!string.IsNullOrEmpty(raw))
                    foreach (var loc in raw.Split('|'))
                        CheckedLocations.Add(loc);
            }
            if (loader.Has(ReceivedItemsKey))
            {
                var raw = loader.Get(ReceivedItemsKey);
                if (!string.IsNullOrEmpty(raw))
                    foreach (var item in raw.Split('|'))
                        ReceivedItems.Add(item);
            }

            // Restore branching shop layout
            if (loader.Has(SkipsAvailableKey))
                SkipsAvailable = loader.Get(SkipsAvailableKey);
            if (loader.Has(ShopLayoutKey))
            {
                var raw = loader.Get(ShopLayoutKey);
                if (!string.IsNullOrEmpty(raw))
                    ShopLayout = DeserializeShopLayout(raw);
            }

            // Restore milestone definitions and checked state
            if (loader.Has(MilestonesKey))
            {
                var raw = loader.Get(MilestonesKey);
                if (!string.IsNullOrEmpty(raw))
                    Milestones = DeserializeMilestones(raw);
            }
            if (loader.Has(CheckedMilestonesKey))
            {
                var raw = loader.Get(CheckedMilestonesKey);
                if (!string.IsNullOrEmpty(raw))
                    foreach (var id in raw.Split('|'))
                        if (long.TryParse(id, out var lid))
                            CheckedMilestoneIds.Add(lid);
            }

            // Restore progressive chains and counters
            if (loader.Has(ProgressiveChainsKey))
            {
                var raw = loader.Get(ProgressiveChainsKey);
                if (!string.IsNullOrEmpty(raw))
                    ProgressiveChains = DeserializeProgressiveChains(raw);
            }
            if (loader.Has(ProgressiveCountersKey))
            {
                var raw = loader.Get(ProgressiveCountersKey);
                if (!string.IsNullOrEmpty(raw))
                    ProgressiveCounters = DeserializeProgressiveCounters(raw);
            }

            // Restore goal data
            if (loader.Has(GoalsKey))
            {
                var raw = loader.Get(GoalsKey);
                if (!string.IsNullOrEmpty(raw))
                    Goals = DeserializeGoals(raw);
            }
            if (loader.Has(GoalRequirementKey))
                GoalRequirement = loader.Get(GoalRequirementKey);
            if (loader.Has(PopulationModeKey))
                PopulationMode = loader.Get(PopulationModeKey);
            if (loader.Has(CompletedGoalsKey))
            {
                var raw = loader.Get(CompletedGoalsKey);
                if (!string.IsNullOrEmpty(raw))
                    foreach (var g in raw.Split('|'))
                        CompletedGoals.Add(g);
            }
            if (loader.Has(GoalAchievedKey))
                GoalAchieved = loader.Get(GoalAchievedKey) == 1;

            Debug.Log($"[Archipelago] Loaded save data: ProcessedItemIndex={ArchipelagoManager.ProcessedItemIndex}, " +
                      $"CheckedLocs={CheckedLocations.Count}, ReceivedItems={ReceivedItems.Count}, " +
                      $"ShopSlots={ShopLayout?.Count ?? 0}, Skips={SkipsAvailable}, " +
                      $"Milestones={Milestones?.Count ?? 0}, CheckedMilestones={CheckedMilestoneIds.Count}, " +
                      $"ProgressiveChains={ProgressiveChains.Count}, " +
                      $"Goals={Goals?.Count ?? 0}, CompletedGoals={CompletedGoals.Count}, GoalAchieved={GoalAchieved}");

            if (ShopLayout != null && ShopLayout.Count > 0)
                OnShopLayoutAvailable?.Invoke();

            if (Milestones != null && Milestones.Count > 0)
                OnMilestonesAvailable?.Invoke();

            if (Goals != null && Goals.Count > 0)
                OnGoalsAvailable?.Invoke();

            // Auto-reconnect if we have saved connection data
            if (!string.IsNullOrEmpty(_savedHost) && !string.IsNullOrEmpty(_savedSlot))
            {
                Debug.Log($"[Archipelago] Attempting auto-reconnect to {_savedHost}:{_savedPort} as '{_savedSlot}'...");
                ArchipelagoManager.Connect(_savedHost, _savedPort, _savedSlot);
            }
        }

        private void OnConnectionChanged(bool connected, string message)
        {
            if (!connected) return;

            // Validate faction before processing slot_data
            var slotData = ArchipelagoManager.SlotData;
            if (!ValidateFaction(slotData))
                return;

            Debug.Log($"[Archipelago] SaveData.OnConnectionChanged — ShopLayout null={ShopLayout == null}, count={ShopLayout?.Count ?? -1}");

            // Parse shop layout from slot_data if we don't already have one
            if (ShopLayout == null || ShopLayout.Count == 0)
            {
                LoadFromSlotData(slotData);
                if (ShopLayout != null && ShopLayout.Count > 0)
                {
                    Debug.Log($"[Archipelago] Firing OnShopLayoutAvailable ({ShopLayout.Count} slots)");
                    OnShopLayoutAvailable?.Invoke();
                }
                else
                {
                    Debug.LogWarning("[Archipelago] ShopLayout still empty after LoadFromSlotData");
                }
            }

            // Parse progressive chains from slot_data if we don't already have them
            if (ProgressiveChains.Count == 0 && slotData != null)
            {
                if (slotData.TryGetValue("progressive_chains", out var chainsObj))
                {
                    ProgressiveChains = ParseProgressiveChainsFromSlotData(chainsObj);
                    Debug.Log($"[Archipelago] Parsed {ProgressiveChains.Count} progressive chains from slot_data");
                }
            }

            // Parse milestone definitions from slot_data if we don't already have them
            if ((Milestones == null || Milestones.Count == 0) && slotData != null)
            {
                if (slotData.TryGetValue("milestones", out var milestonesObj))
                {
                    Milestones = ApMilestoneTracker.ParseFromSlotData(milestonesObj);
                    if (Milestones.Count > 0)
                    {
                        Debug.Log($"[Archipelago] Firing OnMilestonesAvailable ({Milestones.Count} milestones)");
                        OnMilestonesAvailable?.Invoke();
                    }
                }
            }

            // Parse goal definitions from slot_data if we don't already have them
            if ((Goals == null || Goals.Count == 0) && slotData != null)
            {
                Goals = ApGoalTracker.ParseGoalsFromSlotData(slotData);
                GoalRequirement = ApGoalTracker.GetIntFromSlotData(slotData, "goal_requirement", 0);
                PopulationMode = ApGoalTracker.GetIntFromSlotData(slotData, "population_mode", 0);
                if (Goals.Count > 0)
                {
                    Debug.Log($"[Archipelago] Firing OnGoalsAvailable ({Goals.Count} goals)");
                    OnGoalsAvailable?.Invoke();
                }
            }
        }

        /// <summary>
        /// Validates that the in-game faction matches the server-expected faction.
        /// Returns true if valid, false if mismatched (disconnects on mismatch).
        /// </summary>
        private bool ValidateFaction(Dictionary<string, object> slotData)
        {
            if (slotData == null) return true;
            if (!slotData.TryGetValue("faction", out var factionObj)) return true;

            var expectedFaction = factionObj?.ToString() ?? "Folktails";

            try
            {
                // FactionService.FactionId returns the current in-game faction identifier
                var gameFactionId = _factionService.FactionId.ToString();

                if (!string.Equals(gameFactionId, expectedFaction, StringComparison.OrdinalIgnoreCase))
                {
                    var msg = $"Connection blocked: You are playing as {gameFactionId} " +
                              $"but your YAML requires {expectedFaction}. " +
                              $"Please start a new game with the correct faction.";
                    Debug.LogError($"[Archipelago] {msg}");
                    ArchipelagoManager.PostLogMessage(msg);
                    ArchipelagoManager.Disconnect();
                    return false;
                }

                Debug.Log($"[Archipelago] Faction validated: {gameFactionId} matches slot_data");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Could not validate faction: {ex.Message}. Proceeding anyway.");
            }

            return true;
        }

        public void Save(ISingletonSaver singletonSaver)
        {
            var saver = singletonSaver.GetSingleton(ArchipelagoKey);
            saver.Set(ProcessedIndexKey, ArchipelagoManager.ProcessedItemIndex);

            if (ArchipelagoManager.CurrentHost != null)
            {
                saver.Set(HostKey, ArchipelagoManager.CurrentHost);
                saver.Set(PortKey, ArchipelagoManager.CurrentPort);
                saver.Set(SlotKey, ArchipelagoManager.CurrentSlot);
            }
            if (ArchipelagoManager.CurrentSeed != null)
            {
                saver.Set(SeedKey, ArchipelagoManager.CurrentSeed);
            }

            // Persist shop state as pipe-delimited strings
            if (CheckedLocations.Count > 0)
                saver.Set(CheckedLocsKey, string.Join("|", CheckedLocations));
            if (ReceivedItems.Count > 0)
                saver.Set(ReceivedItemsKey, string.Join("|", ReceivedItems));

            // Persist branching shop layout
            saver.Set(SkipsAvailableKey, SkipsAvailable);
            if (ShopLayout != null && ShopLayout.Count > 0)
                saver.Set(ShopLayoutKey, SerializeShopLayout(ShopLayout));

            // Persist milestones
            if (Milestones != null && Milestones.Count > 0)
                saver.Set(MilestonesKey, SerializeMilestones(Milestones));
            if (CheckedMilestoneIds.Count > 0)
                saver.Set(CheckedMilestonesKey, string.Join("|", CheckedMilestoneIds));

            // Persist progressive chains and counters
            if (ProgressiveChains.Count > 0)
                saver.Set(ProgressiveChainsKey, SerializeProgressiveChains(ProgressiveChains));
            if (ProgressiveCounters.Count > 0)
                saver.Set(ProgressiveCountersKey, SerializeProgressiveCounters(ProgressiveCounters));

            // Persist goal data
            if (Goals != null && Goals.Count > 0)
                saver.Set(GoalsKey, SerializeGoals(Goals));
            saver.Set(GoalRequirementKey, GoalRequirement);
            saver.Set(PopulationModeKey, PopulationMode);
            if (CompletedGoals.Count > 0)
                saver.Set(CompletedGoalsKey, string.Join("|", CompletedGoals));
            saver.Set(GoalAchievedKey, GoalAchieved ? 1 : 0);
        }

        /// <summary>
        /// Parse shop_layout from slot_data on first connect.
        /// Called by the connection flow after ArchipelagoManager.Connect succeeds.
        /// </summary>
        public void LoadFromSlotData(Dictionary<string, object> slotData)
        {
            if (slotData == null)
            {
                Debug.LogWarning("[Archipelago] LoadFromSlotData called with null slotData");
                return;
            }

            if (slotData.TryGetValue("shop_layout", out var layoutObj))
            {
                Debug.Log($"[Archipelago] shop_layout type: {layoutObj?.GetType().FullName}");
                ShopLayout = ParseShopLayoutFromSlotData(layoutObj);
                Debug.Log($"[Archipelago] Parsed shop_layout from slot_data: {ShopLayout.Count} slots across " +
                          $"{ShopLayout.Select(s => s.Path).Distinct().Count()} paths");
            }
            else
            {
                Debug.LogWarning("[Archipelago] slot_data has no 'shop_layout' key");
            }
        }

        // -----------------------------------------------------------------
        // Serialization helpers (pipe-delimited compact format)
        // Format per slot: "Path,Level,LocationId,Price,Tier"
        // Slots separated by ";"
        // -----------------------------------------------------------------

        private static string SerializeShopLayout(List<ShopSlot> layout)
        {
            return string.Join(";", layout.Select(s =>
                $"{s.Path},{s.Level},{s.LocationId},{s.Price},{s.Tier}"));
        }

        private static List<ShopSlot> DeserializeShopLayout(string raw)
        {
            var result = new List<ShopSlot>();
            foreach (var entry in raw.Split(';'))
            {
                var parts = entry.Split(',');
                if (parts.Length < 5) continue;
                result.Add(new ShopSlot
                {
                    Path = parts[0],
                    Level = int.Parse(parts[1]),
                    LocationId = long.Parse(parts[2]),
                    Price = int.Parse(parts[3]),
                    Tier = int.Parse(parts[4]),
                });
            }
            return result;
        }

        private static List<ShopSlot> ParseShopLayoutFromSlotData(object layoutObj)
        {
            var result = new List<ShopSlot>();

            // Try direct JArray cast first
            Newtonsoft.Json.Linq.JArray arr = layoutObj as Newtonsoft.Json.Linq.JArray;

            // If it's a JToken but not JArray, try converting
            if (arr == null && layoutObj is Newtonsoft.Json.Linq.JToken token)
                arr = token as Newtonsoft.Json.Linq.JArray;

            // If it's a raw string, try parsing
            if (arr == null && layoutObj is string str)
            {
                try { arr = Newtonsoft.Json.Linq.JArray.Parse(str); }
                catch { /* not valid JSON array */ }
            }

            if (arr == null)
            {
                Debug.LogWarning($"[Archipelago] Cannot parse shop_layout: " +
                                 $"expected JArray, got {layoutObj?.GetType().FullName}");
                return result;
            }

            foreach (var item in arr)
            {
                result.Add(new ShopSlot
                {
                    Path = item["path"]?.ToString() ?? "A",
                    Level = item["level"]?.ToObject<int>() ?? 0,
                    LocationId = item["location_id"]?.ToObject<long>() ?? 0,
                    Price = item["price"]?.ToObject<int>() ?? 0,
                    Tier = item["tier"]?.ToObject<int>() ?? 1,
                });
            }

            Debug.Log($"[Archipelago] Parsed {result.Count} shop slots from slot_data");
            return result;
        }

        // -----------------------------------------------------------------
        // Milestone serialization (compact format)
        // Format per milestone: "Name,LocationId,Type,Threshold"
        // Milestones separated by ";"
        // -----------------------------------------------------------------

        private static string SerializeMilestones(List<MilestoneDefinition> milestones)
        {
            return string.Join(";", milestones.Select(m =>
                $"{m.Name},{m.LocationId},{m.Type},{m.Threshold}"));
        }

        private static List<MilestoneDefinition> DeserializeMilestones(string raw)
        {
            var result = new List<MilestoneDefinition>();
            foreach (var entry in raw.Split(';'))
            {
                var parts = entry.Split(',');
                if (parts.Length < 4) continue;
                result.Add(new MilestoneDefinition
                {
                    Name = parts[0],
                    LocationId = long.Parse(parts[1]),
                    Type = parts[2],
                    Threshold = int.Parse(parts[3]),
                });
            }
            return result;
        }

        // -----------------------------------------------------------------
        // Progressive chains serialization
        // Format: "ProgName:Building1,Building2,Building3;ProgName2:B1,B2"
        // -----------------------------------------------------------------

        private static string SerializeProgressiveChains(Dictionary<string, List<string>> chains)
        {
            return string.Join(";", chains.Select(kvp =>
                $"{kvp.Key}:{string.Join(",", kvp.Value)}"));
        }

        private static Dictionary<string, List<string>> DeserializeProgressiveChains(string raw)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var entry in raw.Split(';'))
            {
                var colonIdx = entry.IndexOf(':');
                if (colonIdx < 0) continue;
                var name = entry.Substring(0, colonIdx);
                var buildings = entry.Substring(colonIdx + 1).Split(',').ToList();
                result[name] = buildings;
            }
            return result;
        }

        private static string SerializeProgressiveCounters(Dictionary<string, int> counters)
        {
            return string.Join(";", counters.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        }

        private static Dictionary<string, int> DeserializeProgressiveCounters(string raw)
        {
            var result = new Dictionary<string, int>();
            foreach (var entry in raw.Split(';'))
            {
                var colonIdx = entry.IndexOf(':');
                if (colonIdx < 0) continue;
                var name = entry.Substring(0, colonIdx);
                if (int.TryParse(entry.Substring(colonIdx + 1), out var count))
                    result[name] = count;
            }
            return result;
        }

        // -----------------------------------------------------------------
        // Goal serialization
        // Format: "Name,Threshold;Name2,Threshold2"
        // -----------------------------------------------------------------

        private static string SerializeGoals(List<GoalDefinition> goals)
        {
            return string.Join(";", goals.Select(g => $"{g.Name},{g.Threshold}"));
        }

        private static List<GoalDefinition> DeserializeGoals(string raw)
        {
            var result = new List<GoalDefinition>();
            foreach (var entry in raw.Split(';'))
            {
                var parts = entry.Split(',');
                if (parts.Length < 2) continue;
                result.Add(new GoalDefinition
                {
                    Name = parts[0],
                    Threshold = int.TryParse(parts[1], out var t) ? t : 0,
                });
            }
            return result;
        }

        private static Dictionary<string, List<string>> ParseProgressiveChainsFromSlotData(object chainsObj)
        {
            var result = new Dictionary<string, List<string>>();

            // Try JObject (dict-like)
            if (chainsObj is Newtonsoft.Json.Linq.JObject jObj)
            {
                foreach (var prop in jObj.Properties())
                {
                    if (prop.Value is Newtonsoft.Json.Linq.JArray arr)
                        result[prop.Name] = arr.Select(t => t.ToString()).ToList();
                }
                return result;
            }

            // Try as JToken
            if (chainsObj is Newtonsoft.Json.Linq.JToken jToken && jToken.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                foreach (var prop in ((Newtonsoft.Json.Linq.JObject)jToken).Properties())
                {
                    if (prop.Value is Newtonsoft.Json.Linq.JArray arr)
                        result[prop.Name] = arr.Select(t => t.ToString()).ToList();
                }
                return result;
            }

            Debug.LogWarning($"[Archipelago] Cannot parse progressive_chains: " +
                             $"expected JObject, got {chainsObj?.GetType().FullName}");
            return result;
        }
    }
}
