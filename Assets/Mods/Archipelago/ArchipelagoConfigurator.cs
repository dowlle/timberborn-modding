using Bindito.Core;
using Timberborn.BottomBarSystem;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Bindito configurator for the Game scene context.
    /// Registers all Archipelago singletons that need access to in-game services.
    /// </summary>
    [Context("Game")]
    public class ArchipelagoConfigurator : Configurator
    {
        protected override void Configure()
        {
            Bind<ApItemReceiver>().AsSingleton();
            Bind<BuildingUnlockToolRefresher>().AsSingleton();
            Bind<VanillaUnlockBlocker>().AsSingleton();
            Bind<ArchipelagoConnectPanel>().AsSingleton();
            Bind<ArchipelagoSaveData>().AsSingleton();
            Bind<ApEventLogPanel>().AsSingleton();

            // Milestone tracking (population, wellbeing, survival, wonder)
            Bind<ApMilestoneTracker>().AsSingleton();

            // Goal tracking (modular victory conditions)
            Bind<ApGoalTracker>().AsSingleton();

            // Effect handling (filler, traps, boosts)
            Bind<ApEffectHandler>().AsSingleton();

            // AP Shop (tiered location check panel)
            Bind<ApShopPanel>().AsSingleton();
            Bind<ApShopTool>().AsSingleton();
            Bind<ApShopButton>().AsSingleton();
            MultiBind<BottomBarModule>().ToProvider<ApBottomBarModuleProvider>().AsSingleton();
        }

        private class ApBottomBarModuleProvider : IProvider<BottomBarModule>
        {
            private readonly ApShopButton _apShopButton;

            public ApBottomBarModuleProvider(ApShopButton apShopButton)
            {
                _apShopButton = apShopButton;
            }

            public BottomBarModule Get()
            {
                var builder = new BottomBarModule.Builder();
                builder.AddLeftSectionElement(_apShopButton, 100);
                return builder.Build();
            }
        }
    }
}
