using Bindito.Core;
using Timberborn.SingletonSystem;

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
            Bind<ApScienceHook>().AsSingleton();
        }
    }
}
