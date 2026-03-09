using System.Collections.Generic;
using System.Linq;
using Timberborn.CoreUI;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// The AP Shop panel — 4 abstract paths (A/B/C/D) displayed as compact cards.
    /// Each card shows only the next purchasable item; when purchased the card
    /// advances to the next slot.  Completed paths show a "Complete" indicator.
    /// UI is built when shop layout becomes available (from save or slot_data).
    /// </summary>
    public class ApShopPanel : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly UILayout _uiLayout;
        private readonly VisualElementLoader _visualElementLoader;
        private readonly ScienceService _scienceService;
        private readonly ArchipelagoSaveData _saveData;

        private VisualElement _root;
        private Label _scienceLabel;
        private Label _statusLabel;
        private Label _skipsLabel;
        private Label _placeholder;

        private VisualElement _branchContainer;
        private readonly Dictionary<string, List<BranchSlotEntry>> _pathSlots = new();
        private readonly Dictionary<string, PathCard> _pathCards = new();

        public ApShopPanel(
            UILayout uiLayout,
            VisualElementLoader visualElementLoader,
            ScienceService scienceService,
            ArchipelagoSaveData saveData)
        {
            _uiLayout = uiLayout;
            _visualElementLoader = visualElementLoader;
            _scienceService = scienceService;
            _saveData = saveData;
        }

        public void Load()
        {
            _root = _visualElementLoader.LoadVisualElement("ArchipelagoShop");

            // Add to UILayout first to get into the visual tree, then reparent
            // to the root so absolute positioning is relative to the full screen.
            _uiLayout.AddBottomRight(_root, 11);
            var panelRoot = _root.panel?.visualTree;
            if (panelRoot != null)
            {
                panelRoot.Add(_root);
                Debug.Log("[Archipelago] Shop reparented to panel root for full-screen positioning.");
            }
            else
            {
                Debug.LogWarning("[Archipelago] Could not find panel root — shop may be mispositioned.");
            }

            _scienceLabel = _root.Q<Label>("ScienceLabel");
            _statusLabel = _root.Q<Label>("ShopStatus");
            _skipsLabel = _root.Q<Label>("SkipsLabel");

            _branchContainer = _root.Q<VisualElement>("BranchContainer");
            _branchContainer.style.display = DisplayStyle.None;

            // Show placeholder until layout is available
            _placeholder = new Label("Connect to AP server to load shop...");
            _placeholder.AddToClassList("ap-shop__placeholder");
            _root.Q<VisualElement>("ShopBody").Add(_placeholder);

            // If save already has layout, build immediately
            if (_saveData.ShopLayout != null && _saveData.ShopLayout.Count > 0)
                OnShopLayoutAvailable();

            ArchipelagoSaveData.OnShopLayoutAvailable += OnShopLayoutAvailable;
            ArchipelagoManager.OnItemReceived += OnItemReceived;
            ArchipelagoManager.OnConnectionChanged += OnConnectionChanged;

            Debug.Log("[Archipelago] AP Shop ready — waiting for layout.");
        }

        public void Unload()
        {
            ArchipelagoSaveData.OnShopLayoutAvailable -= OnShopLayoutAvailable;
            ArchipelagoManager.OnItemReceived -= OnItemReceived;
            ArchipelagoManager.OnConnectionChanged -= OnConnectionChanged;
        }

        public void Show()
        {
            _root.style.display = DisplayStyle.Flex;
            RefreshAll();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        // =================================================================
        // Layout available handler
        // =================================================================

        private void OnShopLayoutAvailable()
        {
            Debug.Log($"[Archipelago] ApShopPanel.OnShopLayoutAvailable fired, existing paths={_pathSlots.Count}");

            // Guard against double-build
            if (_pathSlots.Count > 0) return;

            _placeholder.style.display = DisplayStyle.None;
            _branchContainer.style.display = DisplayStyle.Flex;
            BuildBranchingUI();

            if (_root.style.display == DisplayStyle.Flex)
                RefreshAll();
        }

        // =================================================================
        // CARD-BASED UI (one item per path)
        // =================================================================

        private void BuildBranchingUI()
        {
            var grouped = _saveData.ShopLayout
                .GroupBy(s => s.Path)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Level).ToList());

            foreach (var (path, slots) in grouped)
            {
                // Build the full slot list for logic tracking
                var slotEntries = new List<BranchSlotEntry>();
                for (int i = 0; i < slots.Count; i++)
                {
                    slotEntries.Add(new BranchSlotEntry
                    {
                        Slot = slots[i],
                        Index = i,
                        Path = path,
                    });
                }
                _pathSlots[path] = slotEntries;

                // Build the visual card for this path
                var card = CreatePathCard(path, slots.Count);
                _branchContainer.Add(card.Container);
                _pathCards[path] = card;
            }

            Debug.Log($"[Archipelago] Branching shop built: {_saveData.ShopLayout.Count} slots across {grouped.Count} paths (card view).");
        }

        private PathCard CreatePathCard(string path, int totalSlots)
        {
            var card = new PathCard { Path = path, TotalSlots = totalSlots };

            card.Container = new VisualElement();
            card.Container.AddToClassList("ap-shop__path-card");

            // Header: path name
            var header = new Label($"Path {path}");
            header.AddToClassList("ap-shop__path-header");
            card.Container.Add(header);

            // Progress: "3 / 31"
            card.ProgressLabel = new Label("0 / " + totalSlots);
            card.ProgressLabel.AddToClassList("ap-shop__card-progress");
            card.Container.Add(card.ProgressLabel);

            // Price: "150 SC"
            card.PriceLabel = new Label("");
            card.PriceLabel.AddToClassList("ap-shop__card-price");
            card.Container.Add(card.PriceLabel);

            // Tier: "T1"
            card.TierLabel = new Label("");
            card.TierLabel.AddToClassList("ap-shop__card-tier");
            card.Container.Add(card.TierLabel);

            // Status message (tier requirement or "Complete!")
            card.StatusLabel = new Label("");
            card.StatusLabel.AddToClassList("ap-shop__card-status");
            card.StatusLabel.style.display = DisplayStyle.None;
            card.Container.Add(card.StatusLabel);

            // Buy button
            card.BuyButton = new Button(() => OnCardBuyClicked(path)) { text = "Buy" };
            card.BuyButton.AddToClassList("ap-shop__card-buy");
            card.Container.Add(card.BuyButton);

            // Skip button
            card.SkipButton = new Button(() => OnCardSkipClicked(path)) { text = "Skip" };
            card.SkipButton.AddToClassList("ap-shop__card-skip");
            card.Container.Add(card.SkipButton);

            return card;
        }

        // =================================================================
        // Slot helpers
        // =================================================================

        private BranchSlotEntry GetNextEntry(string path)
        {
            if (!_pathSlots.TryGetValue(path, out var entries)) return null;
            return entries.FirstOrDefault(e => !IsBranchSlotChecked(e));
        }

        private int GetCheckedCount(string path)
        {
            if (!_pathSlots.TryGetValue(path, out var entries)) return 0;
            return entries.Count(e => IsBranchSlotChecked(e));
        }

        private bool IsBranchSlotChecked(BranchSlotEntry entry)
        {
            return _saveData.CheckedLocations.Contains(entry.Slot.LocationId.ToString());
        }

        private bool IsBranchSlotAvailable(BranchSlotEntry entry)
        {
            if (!ApBuildingLocations.IsTierUnlocked(entry.Slot.Tier, _saveData.ReceivedItems))
                return false;

            if (entry.Index == 0)
                return true;

            var prev = _pathSlots[entry.Path][entry.Index - 1];
            return IsBranchSlotChecked(prev);
        }

        private bool CanPurchaseBranchSlot(BranchSlotEntry entry)
        {
            if (!ArchipelagoManager.IsConnected) return false;
            if (IsBranchSlotChecked(entry)) return false;
            if (!IsBranchSlotAvailable(entry)) return false;
            if (_scienceService.SciencePoints < entry.Slot.Price) return false;
            return true;
        }

        private static string GetTierRequirementText(int tier)
        {
            switch (tier)
            {
                case 2: return "Requires: Gear Workshop";
                case 3: return "Requires: Scavenger Flag + Smelter";
                case 4: return "Requires: Tapper's Shack + Wood Workshop";
                case 5: return "Requires: Bot Part Factory + Bot Assembler";
                default: return "";
            }
        }

        // =================================================================
        // Actions
        // =================================================================

        private void OnCardBuyClicked(string path)
        {
            var entry = GetNextEntry(path);
            if (entry == null || !CanPurchaseBranchSlot(entry)) return;

            _scienceService.SubtractPoints(entry.Slot.Price);
            ArchipelagoManager.SendLocationCheck(entry.Slot.LocationId);
            _saveData.CheckedLocations.Add(entry.Slot.LocationId.ToString());

            Debug.Log($"[Archipelago] Shop buy: {entry.Path}-{entry.Index + 1} " +
                      $"(loc={entry.Slot.LocationId}, cost={entry.Slot.Price})");
            RefreshAll();
        }

        private void OnCardSkipClicked(string path)
        {
            var entry = GetNextEntry(path);
            if (entry == null) return;
            if (_saveData.SkipsAvailable <= 0) return;
            if (!IsBranchSlotAvailable(entry)) return;

            _saveData.SkipsAvailable--;
            ArchipelagoManager.SendLocationCheck(entry.Slot.LocationId);
            _saveData.CheckedLocations.Add(entry.Slot.LocationId.ToString());

            Debug.Log($"[Archipelago] Shop skip: {entry.Path}-{entry.Index + 1} " +
                      $"(loc={entry.Slot.LocationId}, skips remaining={_saveData.SkipsAvailable})");
            RefreshAll();
        }

        // =================================================================
        // Refresh
        // =================================================================

        private void RefreshAll()
        {
            _scienceLabel.text = $"Science: {_scienceService.SciencePoints}";
            _statusLabel.text = ArchipelagoManager.IsConnected
                ? $"Connected as {ArchipelagoManager.CurrentSlot}"
                : "Not connected";

            if (_pathCards.Count == 0) return;

            if (_skipsLabel != null)
                _skipsLabel.text = $"Skips: {_saveData.SkipsAvailable}";

            bool connected = ArchipelagoManager.IsConnected;

            foreach (var (path, card) in _pathCards)
            {
                int checkedCount = GetCheckedCount(path);
                card.ProgressLabel.text = $"{checkedCount} / {card.TotalSlots}";

                var next = GetNextEntry(path);

                if (next == null)
                {
                    // Path complete
                    card.PriceLabel.style.display = DisplayStyle.None;
                    card.TierLabel.style.display = DisplayStyle.None;
                    card.BuyButton.style.display = DisplayStyle.None;
                    card.SkipButton.style.display = DisplayStyle.None;
                    card.StatusLabel.text = "Complete!";
                    card.StatusLabel.style.display = DisplayStyle.Flex;
                    card.Container.RemoveFromClassList("ap-shop__path-card--available");
                    card.Container.RemoveFromClassList("ap-shop__path-card--locked");
                    card.Container.AddToClassList("ap-shop__path-card--complete");
                }
                else if (!IsBranchSlotAvailable(next))
                {
                    // Tier locked
                    card.PriceLabel.text = $"{next.Slot.Price} SC";
                    card.PriceLabel.style.display = DisplayStyle.Flex;
                    card.TierLabel.text = $"T{next.Slot.Tier}";
                    card.TierLabel.style.display = DisplayStyle.Flex;
                    card.BuyButton.style.display = DisplayStyle.Flex;
                    card.BuyButton.SetEnabled(false);
                    card.SkipButton.style.display = DisplayStyle.None;
                    card.StatusLabel.text = GetTierRequirementText(next.Slot.Tier);
                    card.StatusLabel.style.display = DisplayStyle.Flex;
                    card.Container.RemoveFromClassList("ap-shop__path-card--available");
                    card.Container.RemoveFromClassList("ap-shop__path-card--complete");
                    card.Container.AddToClassList("ap-shop__path-card--locked");
                }
                else
                {
                    // Available
                    bool canAfford = _scienceService.SciencePoints >= next.Slot.Price;
                    card.PriceLabel.text = $"{next.Slot.Price} SC";
                    card.PriceLabel.style.display = DisplayStyle.Flex;
                    card.TierLabel.text = $"T{next.Slot.Tier}";
                    card.TierLabel.style.display = DisplayStyle.Flex;
                    card.BuyButton.style.display = DisplayStyle.Flex;
                    card.BuyButton.SetEnabled(canAfford && connected);
                    card.SkipButton.style.display = (_saveData.SkipsAvailable > 0)
                        ? DisplayStyle.Flex : DisplayStyle.None;
                    card.SkipButton.SetEnabled(connected);
                    card.StatusLabel.style.display = DisplayStyle.None;
                    card.Container.RemoveFromClassList("ap-shop__path-card--locked");
                    card.Container.RemoveFromClassList("ap-shop__path-card--complete");
                    card.Container.AddToClassList("ap-shop__path-card--available");
                }
            }
        }

        // =================================================================
        // Event handlers
        // =================================================================

        private void OnItemReceived(ApItem item)
        {
            _saveData.ReceivedItems.Add(item.ItemName);
            if (_root.style.display == DisplayStyle.Flex)
                RefreshAll();
        }

        private void OnConnectionChanged(bool connected, string message)
        {
            if (_root.style.display == DisplayStyle.Flex)
                RefreshAll();
        }

        // =================================================================
        // Data models
        // =================================================================

        private class BranchSlotEntry
        {
            public ShopSlot Slot;
            public int Index;
            public string Path;
        }

        private class PathCard
        {
            public string Path;
            public int TotalSlots;
            public VisualElement Container;
            public Label ProgressLabel;
            public Label PriceLabel;
            public Label TierLabel;
            public Label StatusLabel;
            public Button BuyButton;
            public Button SkipButton;
        }
    }
}
