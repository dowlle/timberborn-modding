using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.Buildings;
using Timberborn.CoreUI;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// The AP Shop panel — supports two modes:
    /// - Flat (ShopStyle=0): 5-tier list of named locations, original behavior.
    /// - Branching (ShopStyle=1): 4 abstract paths (A/B/C/D) with sequential
    ///   purchases, escalating prices, tier gates, and skip support.
    /// </summary>
    public class ApShopPanel : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly UILayout _uiLayout;
        private readonly VisualElementLoader _visualElementLoader;
        private readonly ScienceService _scienceService;
        private readonly TemplateNameMapper _templateNameMapper;
        private readonly ArchipelagoSaveData _saveData;

        private VisualElement _root;
        private Label _scienceLabel;
        private Label _statusLabel;
        private Label _skipsLabel;

        // Flat mode
        private VisualElement _flatContainer;
        private readonly List<FlatShopEntry> _flatEntries = new();
        private readonly Dictionary<ApTier, VisualElement> _tierBodies = new();
        private readonly Dictionary<ApTier, Label> _tierStatusLabels = new();

        // Branching mode
        private VisualElement _branchContainer;
        private readonly Dictionary<string, List<BranchSlotEntry>> _pathSlots = new();

        public ApShopPanel(
            UILayout uiLayout,
            VisualElementLoader visualElementLoader,
            ScienceService scienceService,
            TemplateNameMapper templateNameMapper,
            ArchipelagoSaveData saveData)
        {
            _uiLayout = uiLayout;
            _visualElementLoader = visualElementLoader;
            _scienceService = scienceService;
            _templateNameMapper = templateNameMapper;
            _saveData = saveData;
        }

        public void Load()
        {
            _root = _visualElementLoader.LoadVisualElement("ArchipelagoShop");
            _uiLayout.AddBottomRight(_root, 11);

            _scienceLabel = _root.Q<Label>("ScienceLabel");
            _statusLabel = _root.Q<Label>("ShopStatus");
            _skipsLabel = _root.Q<Label>("SkipsLabel");

            if (_saveData.ShopStyle == 1 && _saveData.ShopLayout != null && _saveData.ShopLayout.Count > 0)
                BuildBranchingUI();
            else
                BuildFlatUI();

            ArchipelagoManager.OnItemReceived += OnItemReceived;
            ArchipelagoManager.OnConnectionChanged += OnConnectionChanged;

            Debug.Log($"[Archipelago] AP Shop ready — mode={(_saveData.ShopStyle == 1 ? "branching" : "flat")}");
        }

        public void Unload()
        {
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
        // FLAT MODE (original 5-tier layout)
        // =================================================================

        private void BuildFlatUI()
        {
            // Build data model from location mappings
            foreach (var entry in ApBuildingLocations.AllEntries)
            {
                var templateName = entry.Key;
                var locationName = entry.Value;
                var buildingName = locationName.Replace("Science: ", "");

                if (!_templateNameMapper.TryGetTemplate(templateName, out var template))
                    continue;

                var spec = template.GetSpec<BuildingSpec>();
                if (spec == null)
                    continue;

                _flatEntries.Add(new FlatShopEntry
                {
                    LocationName = locationName,
                    BuildingName = buildingName,
                    ScienceCost = spec.ScienceCost,
                    Tier = ApBuildingLocations.GetTier(locationName),
                });
            }

            _flatEntries.Sort((a, b) =>
            {
                int tierCompare = a.Tier.CompareTo(b.Tier);
                return tierCompare != 0 ? tierCompare : a.ScienceCost.CompareTo(b.ScienceCost);
            });

            _flatContainer = _root.Q<VisualElement>("FlatContainer");
            if (_flatContainer == null)
            {
                _flatContainer = new ScrollView();
                _flatContainer.name = "FlatContainer";
                _flatContainer.AddToClassList("ap-shop__scroll");
                _root.Q<VisualElement>("ShopBody").Add(_flatContainer);
            }

            // Hide branching elements
            var branchEl = _root.Q<VisualElement>("BranchContainer");
            if (branchEl != null) branchEl.style.display = DisplayStyle.None;
            if (_skipsLabel != null) _skipsLabel.style.display = DisplayStyle.None;

            foreach (ApTier tier in Enum.GetValues(typeof(ApTier)))
            {
                var tierEntries = _flatEntries.Where(e => e.Tier == tier).ToList();
                if (tierEntries.Count == 0) continue;

                var tierSection = new VisualElement();
                tierSection.AddToClassList("ap-shop__tier");

                var header = new VisualElement();
                header.AddToClassList("ap-shop__tier-header");

                var tierName = new Label($"Tier {(int)tier} ({tierEntries.Count})");
                tierName.AddToClassList("ap-shop__tier-name");
                header.Add(tierName);

                var tierStatus = new Label();
                tierStatus.AddToClassList("ap-shop__tier-status");
                header.Add(tierStatus);
                _tierStatusLabels[tier] = tierStatus;

                tierSection.Add(header);

                var body = new VisualElement();
                _tierBodies[tier] = body;

                foreach (var entry in tierEntries)
                {
                    var row = CreateFlatRow(entry);
                    entry.RowElement = row;
                    body.Add(row);
                }

                tierSection.Add(body);
                _flatContainer.Add(tierSection);
            }

            Debug.Log($"[Archipelago] Flat shop: {_flatEntries.Count} locations across 5 tiers.");
        }

        private VisualElement CreateFlatRow(FlatShopEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("ap-shop__row");

            var nameLabel = new Label(entry.BuildingName);
            nameLabel.AddToClassList("ap-shop__row-name");
            row.Add(nameLabel);

            var costLabel = new Label(entry.ScienceCost.ToString());
            costLabel.AddToClassList("ap-shop__row-cost");
            row.Add(costLabel);

            var checkButton = new Button(() => OnFlatCheckClicked(entry)) { text = "Check" };
            checkButton.AddToClassList("ap-shop__row-button");
            entry.CheckButton = checkButton;
            row.Add(checkButton);

            var checkedLabel = new Label("\u2713");
            checkedLabel.AddToClassList("ap-shop__row-checked");
            checkedLabel.style.display = DisplayStyle.None;
            entry.CheckedLabel = checkedLabel;
            row.Add(checkedLabel);

            return row;
        }

        private void OnFlatCheckClicked(FlatShopEntry entry)
        {
            if (_saveData.CheckedLocations.Contains(entry.LocationName)) return;
            if (!ArchipelagoManager.IsConnected) return;
            if (_scienceService.SciencePoints < entry.ScienceCost) return;

            _scienceService.SubtractPoints(entry.ScienceCost);
            ArchipelagoManager.SendLocationCheck(entry.LocationName);
            _saveData.CheckedLocations.Add(entry.LocationName);

            Debug.Log($"[Archipelago] Shop check: {entry.LocationName} (cost: {entry.ScienceCost})");
            RefreshAll();
        }

        // =================================================================
        // BRANCHING MODE (4-column path layout)
        // =================================================================

        private void BuildBranchingUI()
        {
            // Hide flat elements
            var flatEl = _root.Q<VisualElement>("FlatContainer");
            if (flatEl != null) flatEl.style.display = DisplayStyle.None;

            _branchContainer = _root.Q<VisualElement>("BranchContainer");
            if (_branchContainer == null)
            {
                _branchContainer = new VisualElement();
                _branchContainer.name = "BranchContainer";
                _branchContainer.AddToClassList("ap-shop__branches");
                _root.Q<VisualElement>("ShopBody").Add(_branchContainer);
            }

            // Show skips label
            if (_skipsLabel != null) _skipsLabel.style.display = DisplayStyle.Flex;

            // Group shop slots by path, sorted by level
            var grouped = _saveData.ShopLayout
                .GroupBy(s => s.Path)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Level).ToList());

            foreach (var (path, slots) in grouped)
            {
                var column = new VisualElement();
                column.AddToClassList("ap-shop__path-column");

                // Path header
                var pathHeader = new Label($"Path {path}");
                pathHeader.AddToClassList("ap-shop__path-header");
                column.Add(pathHeader);

                // Scrollable slot list
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

            Debug.Log($"[Archipelago] Branching shop: {_saveData.ShopLayout.Count} slots across {grouped.Count} paths.");
        }

        private VisualElement CreateBranchRow(BranchSlotEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("ap-shop__branch-row");

            // Slot label: "A-1", "B-5", etc. (1-based for display)
            var slotLabel = new Label($"{entry.Path}-{entry.Index + 1}");
            slotLabel.AddToClassList("ap-shop__branch-label");
            row.Add(slotLabel);

            // Price
            var priceLabel = new Label($"{entry.Slot.Price}");
            priceLabel.AddToClassList("ap-shop__branch-price");
            entry.PriceLabel = priceLabel;
            row.Add(priceLabel);

            // Tier indicator
            var tierLabel = new Label($"T{entry.Slot.Tier}");
            tierLabel.AddToClassList("ap-shop__branch-tier");
            entry.TierLabel = tierLabel;
            row.Add(tierLabel);

            // Check button
            var checkBtn = new Button(() => OnBranchCheckClicked(entry)) { text = "Buy" };
            checkBtn.AddToClassList("ap-shop__branch-button");
            entry.CheckButton = checkBtn;
            row.Add(checkBtn);

            // Skip button
            var skipBtn = new Button(() => OnBranchSkipClicked(entry)) { text = "Skip" };
            skipBtn.AddToClassList("ap-shop__branch-skip-button");
            entry.SkipButton = skipBtn;
            row.Add(skipBtn);

            // Checked indicator
            var checkedLabel = new Label("\u2713");
            checkedLabel.AddToClassList("ap-shop__branch-checked");
            checkedLabel.style.display = DisplayStyle.None;
            entry.CheckedLabel = checkedLabel;
            row.Add(checkedLabel);

            return row;
        }

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
            // Tier gate
            if (!ApBuildingLocations.IsTierUnlocked(entry.Slot.Tier, _saveData.ReceivedItems))
                return false;

            // Sequential: first in path always available, others require predecessor checked
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

            if (_saveData.ShopStyle == 1 && _pathSlots.Count > 0)
                RefreshBranching();
            else
                RefreshFlat();
        }

        private void RefreshFlat()
        {
            foreach (ApTier tier in Enum.GetValues(typeof(ApTier)))
            {
                if (!_tierBodies.ContainsKey(tier)) continue;

                bool unlocked = ApBuildingLocations.IsTierUnlocked(tier, _saveData.ReceivedItems);
                _tierStatusLabels[tier].text = unlocked ? "Unlocked" : ApBuildingLocations.GetTierRequirements(tier);
                _tierBodies[tier].style.display = unlocked ? DisplayStyle.Flex : DisplayStyle.None;
            }

            foreach (var entry in _flatEntries)
            {
                bool isChecked = _saveData.CheckedLocations.Contains(entry.LocationName);
                bool canAfford = _scienceService.SciencePoints >= entry.ScienceCost;
                bool connected = ArchipelagoManager.IsConnected;

                if (entry.CheckButton != null)
                {
                    entry.CheckButton.style.display = isChecked ? DisplayStyle.None : DisplayStyle.Flex;
                    entry.CheckButton.SetEnabled(canAfford && connected && !isChecked);
                }
                if (entry.CheckedLabel != null)
                {
                    entry.CheckedLabel.style.display = isChecked ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }

        private void RefreshBranching()
        {
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

                    // Row styling
                    entry.RowElement.RemoveFromClassList("ap-shop__branch-row--checked");
                    entry.RowElement.RemoveFromClassList("ap-shop__branch-row--available");
                    entry.RowElement.RemoveFromClassList("ap-shop__branch-row--locked");

                    if (isChecked)
                        entry.RowElement.AddToClassList("ap-shop__branch-row--checked");
                    else if (isAvailable)
                        entry.RowElement.AddToClassList("ap-shop__branch-row--available");
                    else
                        entry.RowElement.AddToClassList("ap-shop__branch-row--locked");

                    // Buttons
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
        // Data models
        // =================================================================

        private class FlatShopEntry
        {
            public string LocationName;
            public string BuildingName;
            public int ScienceCost;
            public ApTier Tier;
            public VisualElement RowElement;
            public Button CheckButton;
            public Label CheckedLabel;
        }

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
