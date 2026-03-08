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
    /// The AP Shop panel — a tiered UI where players spend science to "check"
    /// AP locations (sending them to the server). Tiers unlock progressively
    /// based on which AP items the player has received, mirroring Rules.py.
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
        private VisualElement _tierContainer;
        private Label _statusLabel;

        // Data model
        private readonly List<ShopEntry> _entries = new();

        // Per-tier UI containers for refresh
        private readonly Dictionary<ApTier, VisualElement> _tierBodies = new();
        private readonly Dictionary<ApTier, Label> _tierStatusLabels = new();

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

                _entries.Add(new ShopEntry
                {
                    LocationName = locationName,
                    BuildingName = buildingName,
                    ScienceCost = spec.ScienceCost,
                    Tier = ApBuildingLocations.GetTier(locationName),
                });
            }

            // Sort entries by tier, then by science cost
            _entries.Sort((a, b) =>
            {
                int tierCompare = a.Tier.CompareTo(b.Tier);
                return tierCompare != 0 ? tierCompare : a.ScienceCost.CompareTo(b.ScienceCost);
            });

            // Build UI
            _root = _visualElementLoader.LoadVisualElement("ArchipelagoShop");
            _uiLayout.AddBottomRight(_root, 11);

            _scienceLabel = _root.Q<Label>("ScienceLabel");
            _tierContainer = _root.Q<ScrollView>("TierContainer");
            _statusLabel = _root.Q<Label>("ShopStatus");

            BuildTierUI();

            // Subscribe to events
            ArchipelagoManager.OnItemReceived += OnItemReceived;
            ArchipelagoManager.OnConnectionChanged += OnConnectionChanged;

            Debug.Log($"[Archipelago] AP Shop ready — {_entries.Count} locations across 5 tiers.");
        }

        public void Unload()
        {
            ArchipelagoManager.OnItemReceived -= OnItemReceived;
            ArchipelagoManager.OnConnectionChanged -= OnConnectionChanged;
        }

        // -----------------------------------------------------------------
        // Show / Hide (called by ApShopTool)
        // -----------------------------------------------------------------

        public void Show()
        {
            _root.style.display = DisplayStyle.Flex;
            RefreshAll();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        // -----------------------------------------------------------------
        // UI Construction
        // -----------------------------------------------------------------

        private void BuildTierUI()
        {
            foreach (ApTier tier in Enum.GetValues(typeof(ApTier)))
            {
                var tierEntries = _entries.Where(e => e.Tier == tier).ToList();
                if (tierEntries.Count == 0) continue;

                var tierSection = new VisualElement();
                tierSection.AddToClassList("ap-shop__tier");

                // Tier header
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

                // Tier body (location rows)
                var body = new VisualElement();
                _tierBodies[tier] = body;

                foreach (var entry in tierEntries)
                {
                    var row = CreateLocationRow(entry);
                    entry.RowElement = row;
                    body.Add(row);
                }

                tierSection.Add(body);
                _tierContainer.Add(tierSection);
            }
        }

        private VisualElement CreateLocationRow(ShopEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("ap-shop__row");

            var nameLabel = new Label(entry.BuildingName);
            nameLabel.AddToClassList("ap-shop__row-name");
            row.Add(nameLabel);

            var costLabel = new Label(entry.ScienceCost.ToString());
            costLabel.AddToClassList("ap-shop__row-cost");
            row.Add(costLabel);

            var checkButton = new Button(() => OnCheckClicked(entry)) { text = "Check" };
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

        // -----------------------------------------------------------------
        // UI Refresh
        // -----------------------------------------------------------------

        private void RefreshAll()
        {
            // Update science display
            _scienceLabel.text = $"Science: {_scienceService.SciencePoints}";

            // Update connection status
            _statusLabel.text = ArchipelagoManager.IsConnected
                ? $"Connected as {ArchipelagoManager.CurrentSlot}"
                : "Not connected";

            // Update tier states and buttons
            foreach (ApTier tier in Enum.GetValues(typeof(ApTier)))
            {
                if (!_tierBodies.ContainsKey(tier)) continue;

                bool unlocked = ApBuildingLocations.IsTierUnlocked(tier, _saveData.ReceivedItems);
                _tierStatusLabels[tier].text = unlocked ? "Unlocked" : ApBuildingLocations.GetTierRequirements(tier);
                _tierBodies[tier].style.display = unlocked ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Update individual entry states
            foreach (var entry in _entries)
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

        // -----------------------------------------------------------------
        // Actions
        // -----------------------------------------------------------------

        private void OnCheckClicked(ShopEntry entry)
        {
            if (_saveData.CheckedLocations.Contains(entry.LocationName))
                return;
            if (!ArchipelagoManager.IsConnected)
                return;
            if (_scienceService.SciencePoints < entry.ScienceCost)
                return;

            // Deduct science and send check
            _scienceService.SubtractPoints(entry.ScienceCost);
            ArchipelagoManager.SendLocationCheck(entry.LocationName);
            _saveData.CheckedLocations.Add(entry.LocationName);

            Debug.Log($"[Archipelago] Shop check: {entry.LocationName} (cost: {entry.ScienceCost})");
            RefreshAll();
        }

        // -----------------------------------------------------------------
        // Event handlers
        // -----------------------------------------------------------------

        private void OnItemReceived(ApItem item)
        {
            _saveData.ReceivedItems.Add(item.ItemName);
            // Refresh if the panel is visible (tier gates may have changed)
            if (_root.style.display == DisplayStyle.Flex)
                RefreshAll();
        }

        private void OnConnectionChanged(bool connected, string message)
        {
            if (_root.style.display == DisplayStyle.Flex)
                RefreshAll();
        }

        // -----------------------------------------------------------------
        // Data model
        // -----------------------------------------------------------------

        private class ShopEntry
        {
            public string LocationName;
            public string BuildingName;
            public int ScienceCost;
            public ApTier Tier;
            public VisualElement RowElement;
            public Button CheckButton;
            public Label CheckedLabel;
        }
    }
}
