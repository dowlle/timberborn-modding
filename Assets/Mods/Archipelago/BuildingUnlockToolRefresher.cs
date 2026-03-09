using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Bridges the gap between BuildingUnlockingService and the toolbar UI.
    /// BuildingToolLocker only evaluates lock state at scene load — it does NOT
    /// subscribe to BuildingUnlockedEvent. This class listens for that event,
    /// re-evaluates all lockers via reflection, and properly unlocks tools whose
    /// buildings have been unlocked (updating both internal state and visuals).
    /// </summary>
    public class BuildingUnlockToolRefresher : IPostLoadableSingleton
    {
        private readonly EventBus _eventBus;
        private readonly ToolUnlockingService _toolUnlockingService;
        private readonly ToolButtonService _toolButtonService;

        // Cached reflection into ToolButtonService
        private FieldInfo _toolToButtonMapField;

        // Cached reflection into ToolUnlockingService
        private FieldInfo _activeLockersField;
        private FieldInfo _toolLockersField;

        // Cached reflection for constructing ToolUnlockedEvent
        private ConstructorInfo _toolUnlockedEventCtor;

        public BuildingUnlockToolRefresher(
            EventBus eventBus,
            ToolUnlockingService toolUnlockingService,
            ToolButtonService toolButtonService)
        {
            _eventBus = eventBus;
            _toolUnlockingService = toolUnlockingService;
            _toolButtonService = toolButtonService;
        }

        public void PostLoad()
        {
            // --- ToolButtonService reflection ---
            _toolToButtonMapField = typeof(ToolButtonService).GetField(
                "_toolToButtonMap", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_toolToButtonMapField == null)
                Debug.LogWarning("[Archipelago] Could not find ToolButtonService._toolToButtonMap field.");

            // --- ToolUnlockingService reflection + diagnostics ---
            var tusType = typeof(ToolUnlockingService);
            var allFields = tusType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            Debug.Log($"[AP-Diag] ToolUnlockingService has {allFields.Length} fields:");
            foreach (var field in allFields)
            {
                try
                {
                    var val = field.GetValue(_toolUnlockingService);
                    string extra = "";
                    if (val is ICollection col) extra += $" count={col.Count}";
                    if (val is IDictionary dict)
                    {
                        extra += $" dictKeys={dict.Keys.Count}";
                        foreach (var k in dict.Keys)
                        {
                            extra += $" firstKeyType={k.GetType().FullName}";
                            break;
                        }
                    }
                    Debug.Log($"[AP-Diag]   {field.Name} : {field.FieldType.FullName} = {val}{extra}");
                }
                catch (Exception ex)
                {
                    Debug.Log($"[AP-Diag]   {field.Name} : {field.FieldType.FullName} = ERROR({ex.Message})");
                }
            }

            // Try to find the active lockers field (tracks which tools are locked)
            // Expected name: _activeLockers or similar; type: Dictionary<ITool, ...>
            foreach (var field in allFields)
            {
                var ft = field.FieldType;
                if (typeof(IDictionary).IsAssignableFrom(ft) && ft.IsGenericType)
                {
                    var genArgs = ft.GetGenericArguments();
                    if (genArgs.Length > 0 && typeof(ITool).IsAssignableFrom(genArgs[0]))
                    {
                        _activeLockersField = field;
                        Debug.Log($"[AP-Diag] Found active lockers field: {field.Name} ({ft.FullName})");
                        break;
                    }
                }
            }

            // Try to find the tool lockers list (all IToolLocker instances)
            // Expected name: _toolLockers; type: List<IToolLocker> or IToolLocker[]
            foreach (var field in allFields)
            {
                var ft = field.FieldType;
                if (ft.IsGenericType)
                {
                    var genArgs = ft.GetGenericArguments();
                    if (genArgs.Length > 0 && genArgs[0].Name.Contains("ToolLocker"))
                    {
                        _toolLockersField = field;
                        Debug.Log($"[AP-Diag] Found tool lockers field: {field.Name} ({ft.FullName})");
                        break;
                    }
                }
                else if (ft.IsArray && ft.GetElementType()?.Name.Contains("ToolLocker") == true)
                {
                    _toolLockersField = field;
                    Debug.Log($"[AP-Diag] Found tool lockers field (array): {field.Name} ({ft.FullName})");
                    break;
                }
            }

            if (_activeLockersField == null)
                Debug.LogWarning("[AP-Diag] Could not auto-detect active lockers field on ToolUnlockingService.");
            if (_toolLockersField == null)
                Debug.LogWarning("[AP-Diag] Could not auto-detect tool lockers field on ToolUnlockingService.");

            // --- ToolUnlockedEvent reflection ---
            var eventType = typeof(ToolUnlockingService).Assembly
                .GetType("Timberborn.ToolSystem.ToolUnlockedEvent");

            if (eventType != null)
            {
                _toolUnlockedEventCtor = eventType.GetConstructor(new[] { typeof(ITool) })
                    ?? eventType.GetConstructor(Type.EmptyTypes);

                if (_toolUnlockedEventCtor == null)
                {
                    Debug.LogWarning($"[AP-Diag] ToolUnlockedEvent: no usable constructor found.");
                    foreach (var ctor in eventType.GetConstructors(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        var ps = ctor.GetParameters();
                        Debug.Log($"[AP-Diag]   ctor({string.Join(", ", ps.Select(p => p.ParameterType.Name))})");
                    }
                }
                else
                {
                    Debug.Log($"[AP-Diag] ToolUnlockedEvent ctor params: {_toolUnlockedEventCtor.GetParameters().Length}");
                }
            }
            else
            {
                Debug.LogWarning("[AP-Diag] Could not find ToolUnlockedEvent type.");
            }

            // --- IToolLocker interface diagnostics ---
            var iToolLockerType = tusType.Assembly.GetType("Timberborn.ToolSystem.IToolLocker");
            if (iToolLockerType != null)
            {
                Debug.Log($"[AP-Diag] IToolLocker methods:");
                foreach (var m in iToolLockerType.GetMethods())
                    Debug.Log($"[AP-Diag]   {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) → {m.ReturnType.Name}");
            }

            _eventBus.Register(this);
            Debug.Log($"[Archipelago] BuildingUnlockToolRefresher registered. " +
                $"ActiveLockers={_activeLockersField?.Name ?? "NONE"}, " +
                $"ToolLockers={_toolLockersField?.Name ?? "NONE"}, " +
                $"EventCtor={_toolUnlockedEventCtor != null}");
        }

        [OnEvent]
        public void OnBuildingUnlocked(BuildingUnlockedEvent e)
        {
            try
            {
                if (_activeLockersField != null && _toolLockersField != null && _toolUnlockedEventCtor != null)
                    RefreshViaInternalState();
                else
                    Debug.LogWarning("[Archipelago] Cannot refresh toolbar — missing reflection fields. Check [AP-Diag] logs.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Archipelago] RefreshViaInternalState failed: {ex}");
            }
        }

        /// <summary>
        /// Reflects into ToolUnlockingService's internal state to properly unlock tools:
        /// 1. Gets the locked tools collection (_activeLockers)
        /// 2. Gets the locker list (_toolLockers)
        /// 3. For each locked tool, re-evaluates all lockers' ShouldLock()
        /// 4. If no locker says "lock", removes from locked set + posts ToolUnlockedEvent
        /// </summary>
        private void RefreshViaInternalState()
        {
            var activeLockers = _activeLockersField.GetValue(_toolUnlockingService) as IDictionary;
            if (activeLockers == null)
            {
                Debug.LogWarning("[Archipelago] _activeLockers value is null.");
                return;
            }

            var toolLockers = _toolLockersField.GetValue(_toolUnlockingService);
            if (toolLockers == null)
            {
                Debug.LogWarning("[Archipelago] _toolLockers value is null.");
                return;
            }

            // Get ShouldLock method from IToolLocker interface
            var iToolLockerType = typeof(ToolUnlockingService).Assembly
                .GetType("Timberborn.ToolSystem.IToolLocker");
            var shouldLockMethod = iToolLockerType?.GetMethod("ShouldLock");

            if (shouldLockMethod == null)
            {
                Debug.LogWarning("[Archipelago] Could not find IToolLocker.ShouldLock method.");
                return;
            }

            // Collect locker instances into a list we can iterate
            var lockerList = new List<object>();
            foreach (var locker in (IEnumerable)toolLockers)
                lockerList.Add(locker);

            // Snapshot locked tool keys (can't modify dict during iteration)
            var lockedKeys = new List<object>();
            foreach (var key in activeLockers.Keys)
                lockedKeys.Add(key);

            Debug.Log($"[Archipelago] Re-evaluating {lockedKeys.Count} locked tools against {lockerList.Count} lockers.");

            int unlocked = 0;
            foreach (var keyObj in lockedKeys)
            {
                if (keyObj is not ITool tool) continue;

                bool anyLockerSaysLock = false;
                foreach (var locker in lockerList)
                {
                    try
                    {
                        var result = shouldLockMethod.Invoke(locker, new object[] { tool });
                        if (result is true)
                        {
                            anyLockerSaysLock = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Some lockers may not apply to this tool type — skip
                    }
                }

                if (!anyLockerSaysLock)
                {
                    // Remove from locked set
                    activeLockers.Remove(keyObj);

                    // Post ToolUnlockedEvent
                    object evt;
                    if (_toolUnlockedEventCtor.GetParameters().Length == 1)
                        evt = _toolUnlockedEventCtor.Invoke(new object[] { tool });
                    else
                        evt = _toolUnlockedEventCtor.Invoke(Array.Empty<object>());

                    _eventBus.Post(evt);
                    unlocked++;

                    Debug.Log($"[Archipelago] Unlocked tool: {tool.GetType().Name}");
                }
            }

            Debug.Log($"[Archipelago] Toolbar refresh: {unlocked} tools unlocked, {lockedKeys.Count - unlocked} remain locked.");
        }
    }
}
