using System.Linq;
using Timberborn.AssetSystem;
using Timberborn.BlockObjectTools;
using Timberborn.Buildings;
using Timberborn.SingletonSystem;
using Timberborn.ToolSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Replaces the science cost in the "Unlock:" section of a locked AP building's
    /// description with the Archipelago logo (#21). VanillaUnlockBlocker sets those
    /// costs to int.MaxValue, which the game shows as "2147M".
    ///
    /// The game builds that section once per tool (BuildingPlacer.AddUnlockSection,
    /// "Game/ToolPanel/UnlockSection") and caches the description panel, so the
    /// patch is applied once, after the description of a blocked building is shown.
    /// The "Unlock:" header stays; the slot or path of the building is not revealed.
    /// </summary>
    public class ApUnlockCostDisplay : ILoadableSingleton, IUpdatableSingleton, IUnloadableSingleton
    {
        private const string LogoPath = "Sprites/BottomBar/ApShopTool";
        private const string PatchedClass = "ap-unlock-cost--patched";
        private const int FramesToWatch = 3;

        private readonly EventBus _eventBus;
        private readonly IAssetLoader _assetLoader;
        private readonly ApShopPanel _shopPanel;
        private Sprite _logo;
        private int _pendingFrames;

        public ApUnlockCostDisplay(EventBus eventBus, IAssetLoader assetLoader, ApShopPanel shopPanel)
        {
            _eventBus = eventBus;
            _assetLoader = assetLoader;
            _shopPanel = shopPanel;
        }

        public void Load()
        {
            try
            {
                _logo = _assetLoader.Load<Sprite>(LogoPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Archipelago] Unlock cost display: AP logo not loaded ({ex.Message}); the cost is hidden without a logo.");
            }
            _eventBus.Register(this);
        }

        public void Unload()
        {
            _eventBus.Unregister(this);
        }

        [OnEvent]
        public void OnToolEntered(ToolEnteredEvent e) => Watch(e.Tool);

        [OnEvent]
        public void OnTemporaryToolEntered(TemporaryToolEnteredEvent e) => Watch(e.Tool);

        [OnEvent]
        public void OnToolUnlocked(ToolUnlockedEvent e) => Watch(e.Tool);

        private void Watch(ITool tool)
        {
            if (tool is BlockObjectTool blockObjectTool
                && blockObjectTool.Template?.GetSpec<BuildingSpec>() is { } spec
                && spec.ScienceCost == int.MaxValue)
            {
                _pendingFrames = FramesToWatch;
            }
        }

        public void UpdateSingleton()
        {
            if (_pendingFrames <= 0) return;
            _pendingFrames--;
            var root = _shopPanel.VisualTreeRoot;
            if (root == null) return;
            foreach (var section in root.Query<VisualElement>("ScienceCostSection").ToList())
            {
                if (section.ClassListContains(PatchedClass)) continue;
                var cost = section.Q<Label>("ScienceCost");
                // Only the int.MaxValue cost of an AP-blocked building ("2147M").
                if (cost == null || cost.text == null || !cost.text.Contains("2147")) continue;
                Patch(section);
            }
        }

        private void Patch(VisualElement section)
        {
            foreach (var child in section.Children().ToList())
                child.style.display = DisplayStyle.None;
            var logo = new Image { sprite = _logo, scaleMode = ScaleMode.ScaleToFit };
            logo.style.width = 22;
            logo.style.height = 22;
            logo.style.alignSelf = Align.Center;
            section.Add(logo);
            section.AddToClassList(PatchedClass);
        }
    }
}
