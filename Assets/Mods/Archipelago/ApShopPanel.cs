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
    /// The AP Shop panel — 4 abstract paths (A/B/C/D) with sequential
    /// purchases, escalating prices, tier gates, and skip support.
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
        // BRANCHING UI (4-column path layout)
        // =================================================================

        private void BuildBranchingUI()
        {
            var grouped = _saveData.ShopLayout
                .GroupBy(s => s.Path)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Level).ToList());

            foreach (var (path, slots) in grouped)
            {
                var column = new VisualElement();
                column.AddToClassList("ap-shop__path-column");

                var pathHeader = new Label($"Path {path}");
                pathHeader.AddToClassList("ap-shop__path-header");
                column.Add(pathHeader);

                var slotScroll = new ScrollView();
                slotScroll.AddToClassList("ap-shop__path-scroll");

                var slotEntries = new List<BranchSlotEntry>();
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    var entry = new BranchSlotEntry
                    {
                        Slot = slot,
                        Index = i,
                        Path = path,
                    };

                    var row = CreateBranchRow(entry);
                    entry.RowElement = row;
                    slotScroll.Add(row);
                    slotEntries.Add(entry);
                }

                column.Add(slotScroll);
                _branchContainer.Add(column);
                _pathSlots[path] = slotEntries;
            }

            Debug.Log($"[Archipelago] Branching shop built: {_saveData.ShopLayout.Count} slots across {grouped.Count} paths.");
        }

        private VisualElement CreateBranchRow(BranchSlotEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("ap-shop__branch-row");

            var slotLabel = new Label($"{entry.Path}-{entry.Index + 1}");
            slotLabel.AddToClassList("ap-shop__branch-label");
            row.Add(slotLabel);

            var priceLabel = new Label($"{entry.Slot.Price}");
            priceLabel.AddToClassList("ap-shop__branch-price");
            entry.PriceLabel = priceLabel;
            row.Add(priceLabel);

            var tierLabel = new Label($"T{entry.Slot.Tier}");
            tierLabel.AddToClassList("ap-shop__branch-tier");
            entry.TierLabel = tierLabel;
            row.Add(tierLabel);

            var checkBtn = new Button(() => OnBranchCheckClicked(entry)) { text = "Buy" };
            checkBtn.AddToClassList("ap-shop__branch-button");
            entry.CheckButton = checkBtn;
            row.Add(checkBtn);

            var skipBtn = new Button(() => OnBranchSkipClicked(entry)) { text = "Skip" };
            skipBtn.AddToClassList("ap-shop__branch-skip-button");
            entry.SkipButton = skipBtn;
            row.Add(skipBtn);

            var checkedLabel = new Label("\u2713");
            checkedLabel.AddToClassList("ap-shop__branch-checked");
            checkedLabel.style.display = DisplayStyle.None;
            entry.CheckedLabel = checkedLabel;
            row.Add(checkedLabel);

            return row;
        }

        // =================================================================
        // Actions
        // =================================================================

        private void OnBranchCheckClicked(BranchSlotEntry entry)
        {
            if (!CanPurchaseBranchSlot(entry)) return;

            _scienceService.SubtractPoints(entry.Slot.Price);
            ArchipelagoManager.SendLocationCheck(entry.Slot.LocationId);
            _saveData.CheckedLocations.Add(entry.Slot.LocationId.ToString());

            Debug.Log($"[Archipelago] Branch check: {entry.Path}-{entry.Index + 1} " +
                      $"(loc={entry.Slot.LocationId}, cost={entry.Slot.Price})");
            RefreshAll();
        }

        private void OnBranchSkipClicked(BranchSlotEntry entry)
        {
            if (_saveData.SkipsAvailable <= 0) return;
            if (!IsBranchSlotAvailable(entry)) return;
            if (IsBranchSlotChecked(entry)) return;

            _saveData.SkipsAvailable--;
            ArchipelagoManager.SendLocationCheck(entry.Slot.LocationId);
            _saveData.CheckedLocations.Add(entry.Slot.LocationId.ToString());

            Debug.Log($"[Archipelago] Branch skip: {entry.Path}-{entry.Index + 1} " +
                      $"(loc={entry.Slot.LocationId}, skips remaining={_saveData.SkipsAvailable})");
            RefreshAll();
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

            var pathEntries = _pathSlots[entry.Path];
            var prev = pathEntries[entry.Index - 1];
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

        // =================================================================
        // Refresh
        // =================================================================

        private void RefreshAll()
        {
            _scienceLabel.text = $"Science: {_scienceService.SciencePoints}";
            _statusLabel.text = ArchipelagoManager.IsConnected
                ? $"Connected as {ArchipelagoManager.CurrentSlot}"
                : "Not connected";

            if (_pathSlots.Count == 0) return;

            if (_skipsLabel != null)
                _skipsLabel.text = $"Skips: {_saveData.SkipsAvailable}";

            foreach (var (path, entries) in _pathSlots)
            {
                foreach (var entry in entries)
                {
                    bool isChecked = IsBranchSlotChecked(entry);
                    bool isAvailable = !isChecked && IsBranchSlotAvailable(entry);
                    bool canAfford = _scienceService.SciencePoints >= entry.Slot.Price;
                    bool connected = ArchipelagoManager.IsConnected;

                    entry.RowElement.RemoveFromClassList("ap-shop__branch-row--checked");
                    entry.RowElement.RemoveFromClassList("ap-shop__branch-row--available");
                    entry.RowElement.RemoveFromClassList("ap-shop__branch-row--locked");

                    if (isChecked)
                        entry.RowElement.AddToClassList("ap-shop__branch-row--checked");
                    else if (isAvailable)
                        entry.RowElement.AddToClassList("ap-shop__branch-row--available");
                    else
                        entry.RowElement.AddToClassList("ap-shop__branch-row--locked");

                    entry.CheckButton.style.display = isChecked ? DisplayStyle.None : DisplayStyle.Flex;
                    entry.CheckButton.SetEnabled(isAvailable && canAfford && connected);

                    entry.SkipButton.style.display = (isAvailable && !isChecked && _saveData.SkipsAvailable > 0)
                        ? DisplayStyle.Flex : DisplayStyle.None;
                    entry.SkipButton.SetEnabled(connected);

                    entry.CheckedLabel.style.display = isChecked ? DisplayStyle.Flex : DisplayStyle.None;
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
        // Data model
        // =================================================================

        private class BranchSlotEntry
        {
            public ShopSlot Slot;
            public int Index;
            public string Path;
            public VisualElement RowElement;
            public Label PriceLabel;
            public Label TierLabel;
            public Button CheckButton;
            public Button SkipButton;
            public Label CheckedLabel;
        }
    }
}
