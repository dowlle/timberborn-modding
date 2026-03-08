using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Persists Archipelago state to the Timberborn save file:
    /// - ProcessedItemIndex (so reconnects don't replay items)
    /// - Connection data (for auto-reconnect)
    /// - Checked AP locations (shop state)
    /// - Received AP items (tier gate state)
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

        private readonly ISingletonLoader _singletonLoader;

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

        public ArchipelagoSaveData(ISingletonLoader singletonLoader)
        {
            _singletonLoader = singletonLoader;
        }

        public void Load()
        {
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

            Debug.Log($"[Archipelago] Loaded save data: ProcessedItemIndex={ArchipelagoManager.ProcessedItemIndex}, " +
                      $"CheckedLocs={CheckedLocations.Count}, ReceivedItems={ReceivedItems.Count}");

            // Auto-reconnect if we have saved connection data
            if (!string.IsNullOrEmpty(_savedHost) && !string.IsNullOrEmpty(_savedSlot))
            {
                Debug.Log($"[Archipelago] Attempting auto-reconnect to {_savedHost}:{_savedPort} as '{_savedSlot}'...");
                ArchipelagoManager.Connect(_savedHost, _savedPort, _savedSlot);
            }
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
        }
    }
}
