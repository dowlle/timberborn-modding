using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public string Path;         // "A", "B", "C", "D"
        public int    Level;        // position within path (0-based)
        public long   LocationId;
        public int    Price;        // science cost
        public int    Tier;         // tier gate (1-5)
        public string BuildingName; // building display name (for scouted reveal)
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
    public class ArchipelagoSaveData : ISaveableSingleton, ILoadableSingleton, IUnloadableSingleton
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
        private static readonly PropertyKey<string> ActiveBoostsKey = new("ActiveBoosts");
        private static readonly PropertyKey<string> ScoutedPathsKey = new("ScoutedPaths");
        private static readonly PropertyKey<int> BaselineDroughtKey = new("BaselineDroughtCount");
        private static readonly PropertyKey<int> BaselineBadtideKey = new("BaselineBadtideCount");

        /// <summary>Fired when ShopLayout becomes available (from save or slot_data).</summary>
        public static event Action OnShopLayoutAvailable;

        /// <summary>Fired when milestone definitions become available (from save or slot_data).</summary>
        public static event Action OnMilestonesAvailable;

        /// <summary>Fired when goal definitions become available (from save or slot_data).</summary>
        public static event Action OnGoalsAvailable;

        private readonly ISingletonLoader _singletonLoader;
        private readonly FactionService _factionService;

        /// <summary>Set to true when connection is blocked (e.g. faction mismatch) to prevent auto-reconnect loops.</summary>
        private bool _connectionBlocked;

        // The save's BOUND AP identity. Set once on first successful connect (in
        // OnConnectionChanged) and persisted by Save() — never cleared by Disconnect.
        // Source of truth for "what slot does this save belong to?" The live
        // ArchipelagoManager.Current* statics are connection-state and CANNOT be
        // used for save identity (Disconnect clears CurrentSlot but leaves CurrentHost,
        // so a save-while-disconnected used to write Host=valid + Slot=null and lose
        // the binding).
        private string _savedHost;
        private int _savedPort;
        private string _savedSlot;
        private string _savedSeed;

        /// <summary>Host of the AP server this save is bound to (set on first connect).</summary>
        public string SavedHost => _savedHost;
        /// <summary>Port of the AP server this save is bound to.</summary>
        public int SavedPort => _savedPort;
        /// <summary>Slot name this save is bound to.</summary>
        public string SavedSlot => _savedSlot;
        /// <summary>Seed of the multiworld this save is bound to.</summary>
        public string SavedSeed => _savedSeed;

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

        /// <summary>Baseline hazardous weather counts at AP session start (-1 = not yet set).</summary>
        public int BaselineDroughtCount { get; set; } = -1;
        public int BaselineBadtideCount { get; set; } = -1;

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

        /// <summary>Active boost names, persisted so they survive save/load and re-apply on game start.</summary>
        public HashSet<string> ActiveBoosts { get; } = new();

        /// <summary>Path letters that have been scouted (building names revealed).</summary>
        public HashSet<string> ScoutedPaths { get; } = new();

        public ArchipelagoSaveData(ISingletonLoader singletonLoader, FactionService factionService)
        {
            _singletonLoader = singletonLoader;
            _factionService = factionService;
        }

        public void Load()
        {
            // Always subscribe to connection events — even on fresh saves with no AP data
            ArchipelagoManager.OnConnectionChanged += OnConnectionChanged;

            // Reset the per-process connection block flag on every save load.
            // ConnectionBlocked is a static on ArchipelagoManager; a faction mismatch
            // from a prior connection attempt would otherwise persist for the whole
            // Timberborn process and prevent all future connects until full exe restart.
            // Clearing here means reloading any save gives a fresh chance to connect;
            // if the server still reports the wrong faction, the block flips back on.
            _connectionBlocked = false;
            ArchipelagoManager.ConnectionBlocked = false;

            // Set faction early so GetFaction() works before SlotData arrives
            try
            {
                var currentProp = _factionService.GetType().GetProperty("Current");
                var factionSpec = currentProp?.GetValue(_factionService);
                var idProp = factionSpec?.GetType().GetProperty("Id");
                var gameFactionId = idProp?.GetValue(factionSpec)?.ToString();
                if (!string.IsNullOrEmpty(gameFactionId))
                {
                    ApBuildingLocations.SetGameFaction(gameFactionId);
                    Debug.Log($"[Archipelago] Game faction set early: {gameFactionId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Could not read faction from FactionService: {ex.Message}");
            }

            if (!_singletonLoader.TryGetSingleton(ArchipelagoKey, out var loader))
            {
                Debug.Log("[Archipelago] No saved AP data found in this save file.");
                // Fresh save: clear any static state carried over from a previous save
                // in the same Timberborn process. Without this, ProcessedItemIndex
                // (and any future static AP state) leaks across save loads and causes
                // items to be silently skipped in the new slot.
                ArchipelagoManager.ProcessedItemIndex = 0;
                return;
            }

            var savedIndex = loader.Get(ProcessedIndexKey);

            // Auto-heal carryover-corrupted ProcessedItemIndex from the pre-fix bug:
            // a non-zero ProcessedItemIndex with an empty ReceivedItems set is
            // impossible from real play (HandleItem adds to ReceivedItems before
            // any branching, so every applied item is recorded). When this pattern
            // is detected, reset to 0 so the AP server's full item history replays
            // and lands correctly on this save. ReceivedItems / ProgressiveCounters
            // are empty on broken saves, so replay is idempotent.
            bool hasReceivedItems = loader.Has(ReceivedItemsKey)
                && !string.IsNullOrEmpty(loader.Get(ReceivedItemsKey));
            if (savedIndex > 0 && !hasReceivedItems)
            {
                Debug.LogWarning($"[Archipelago] Detected orphaned ProcessedItemIndex={savedIndex} with no applied items in save (likely from a pre-fix carryover bug). Resetting to 0 so the item history replays correctly.");
                savedIndex = 0;
            }

            ArchipelagoManager.ProcessedItemIndex = savedIndex;

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
            if (loader.Has(BaselineDroughtKey))
                BaselineDroughtCount = loader.Get(BaselineDroughtKey);
            if (loader.Has(BaselineBadtideKey))
                BaselineBadtideCount = loader.Get(BaselineBadtideKey);

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
            if (loader.Has(ActiveBoostsKey))
            {
                var raw = loader.Get(ActiveBoostsKey);
                if (!string.IsNullOrEmpty(raw))
                    foreach (var b in raw.Split('|'))
                        ActiveBoosts.Add(b);
            }
            if (loader.Has(ScoutedPathsKey))
            {
                var raw = loader.Get(ScoutedPathsKey);
                if (!string.IsNullOrEmpty(raw))
                    foreach (var p in raw.Split('|'))
                        ScoutedPaths.Add(p);
            }

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

            // Auto-reconnect if we have saved connection data (unless blocked by validation)
            if (!_connectionBlocked && !string.IsNullOrEmpty(_savedHost) && !string.IsNullOrEmpty(_savedSlot))
            {
                Debug.Log($"[Archipelago] Attempting auto-reconnect to {_savedHost}:{_savedPort} as '{_savedSlot}'...");
                ArchipelagoManager.Connect(_savedHost, _savedPort, _savedSlot);
            }
        }

        /// <summary>
        /// Tears down the AP session when the save unloads (loading another save,
        /// returning to main menu, quitting). Without this, the static AP state
        /// (ProcessedItemIndex, queued items, connection flags) leaks into the
        /// next save load and causes items to be silently skipped or applied to
        /// the wrong slot. Every other AP class implements IUnloadableSingleton;
        /// this one was the gap.
        /// </summary>
        public void Unload()
        {
            ArchipelagoManager.OnConnectionChanged -= OnConnectionChanged;
            ArchipelagoManager.DisconnectWithReason("Save unloaded");
            ArchipelagoManager.ResetSessionState();
        }

        private void OnConnectionChanged(bool connected, string message)
        {
            if (!connected) return;
            if (_connectionBlocked) return;

            // Validate faction before processing slot_data
            var slotData = ArchipelagoManager.SlotData;
            if (!ValidateFaction(slotData))
                return;

            // Bind this save's identity to the connected slot ON FIRST CONNECT.
            // Once bound, the save persists this Host/Port/Slot/Seed regardless of
            // the live ArchipelagoManager.Current* state. Auto-recover the
            // Host-but-no-Slot corruption pattern by re-binding when the slot was
            // null in the loaded save (see Save() for why that pattern existed).
            if (string.IsNullOrEmpty(_savedSlot))
            {
                _savedHost = ArchipelagoManager.CurrentHost;
                _savedPort = ArchipelagoManager.CurrentPort;
                _savedSlot = ArchipelagoManager.CurrentSlot;
                _savedSeed = ArchipelagoManager.CurrentSeed;
                Debug.Log($"[Archipelago] Bound save to AP slot: {_savedHost}:{_savedPort} / {_savedSlot} / seed {_savedSeed}");
            }

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

            // Restore checked state from server (critical for fresh-save reconnects)
            RestoreCheckedStateFromServer();

            // Restore goal completion status from server
            if (!GoalAchieved && ArchipelagoManager.IsGoalCompleted())
            {
                GoalAchieved = true;
                Debug.Log("[Archipelago] Goal already completed on server — restored");
            }
        }

        /// <summary>
        /// Populates CheckedLocations and CheckedMilestoneIds from the server's
        /// authoritative list of checked locations. This is essential when connecting
        /// from a fresh save to an existing AP slot — without it, the player would
        /// have to rebuy all shop locations.
        /// </summary>
        private void RestoreCheckedStateFromServer()
        {
            var serverChecked = ArchipelagoManager.GetAllCheckedLocations();
            if (serverChecked.Count == 0) return;

            // Build a set of milestone location IDs for fast lookup
            var milestoneLocationIds = new HashSet<long>();
            if (Milestones != null)
            {
                foreach (var m in Milestones)
                    milestoneLocationIds.Add(m.LocationId);
            }

            int restoredShop = 0;
            int restoredMilestones = 0;
            foreach (var locId in serverChecked)
            {
                var locIdStr = locId.ToString();
                if (CheckedLocations.Add(locIdStr))
                    restoredShop++;

                if (milestoneLocationIds.Contains(locId) && CheckedMilestoneIds.Add(locId))
                    restoredMilestones++;
            }

            if (restoredShop > 0 || restoredMilestones > 0)
            {
                Debug.Log($"[Archipelago] Restored from server: {restoredShop} checked locations, {restoredMilestones} milestones");
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
                // FactionService.Current returns the FactionSpec with an Id property
                var currentProp = _factionService.GetType().GetProperty("Current");
                if (currentProp == null)
                {
                    Debug.LogWarning("[Archipelago] FactionService.Current not found. Skipping faction validation.");
                    return true;
                }

                var factionSpec = currentProp.GetValue(_factionService);
                var idProp = factionSpec?.GetType().GetProperty("Id");
                var gameFactionId = idProp?.GetValue(factionSpec)?.ToString();

                if (gameFactionId == null)
                {
                    Debug.LogWarning("[Archipelago] Could not determine in-game faction. Skipping validation.");
                    return true;
                }

                if (!string.Equals(gameFactionId, expectedFaction, StringComparison.OrdinalIgnoreCase))
                {
                    var msg = $"Wrong faction! Playing as {gameFactionId}, YAML requires {expectedFaction}.";
                    Debug.LogError($"[Archipelago] {msg}");
                    ArchipelagoManager.PostLogMessage("=== FACTION MISMATCH ===");
                    ArchipelagoManager.PostLogMessage(msg);
                    ArchipelagoManager.PostLogMessage("Please start a new game with the correct faction.");
                    // Block reconnection globally and disconnect with a descriptive status message
                    _connectionBlocked = true;
                    ArchipelagoManager.ConnectionBlocked = true;
                    ArchipelagoManager.DisconnectWithReason(msg);
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

            // Write the save's BOUND identity (instance fields), NOT the live
            // ArchipelagoManager.Current* state. Disconnect clears CurrentSlot but
            // leaves CurrentHost set, so the previous code wrote Host=valid +
            // Slot=null whenever the user saved while disconnected (network blip,
            // focus loss, faction-mismatch reject window). That destroyed the
            // save's slot binding. Instance fields are bound on first connect
            // (OnConnectionChanged) and never cleared, so this Save round-trips
            // a stable identity even when we're not currently connected.
            if (!string.IsNullOrEmpty(_savedHost))
                saver.Set(HostKey, _savedHost);
            if (_savedPort > 0)
                saver.Set(PortKey, _savedPort);
            if (!string.IsNullOrEmpty(_savedSlot))
                saver.Set(SlotKey, _savedSlot);
            if (!string.IsNullOrEmpty(_savedSeed))
                saver.Set(SeedKey, _savedSeed);

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
            if (BaselineDroughtCount >= 0)
                saver.Set(BaselineDroughtKey, BaselineDroughtCount);
            if (BaselineBadtideCount >= 0)
                saver.Set(BaselineBadtideKey, BaselineBadtideCount);

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
            if (ActiveBoosts.Count > 0)
                saver.Set(ActiveBoostsKey, string.Join("|", ActiveBoosts));
            if (ScoutedPaths.Count > 0)
                saver.Set(ScoutedPathsKey, string.Join("|", ScoutedPaths));
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
        // Format per slot: "Path,Level,LocationId,Price,Tier,BuildingName"
        // Slots separated by ";"
        // -----------------------------------------------------------------

        private static string SerializeShopLayout(List<ShopSlot> layout)
        {
            return string.Join(";", layout.Select(s =>
                $"{s.Path},{s.Level},{s.LocationId},{s.Price},{s.Tier},{s.BuildingName}"));
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
                    BuildingName = parts.Length > 5 ? parts[5] : "",
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
                    BuildingName = item["building_name"]?.ToString() ?? "",
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
