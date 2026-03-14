using Timberborn.SingletonSystem;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Kept as a registered singleton for DI compatibility.
    /// Connection UI has been merged into ApShopPanel.
    /// </summary>
    public class ArchipelagoConnectPanel : ILoadableSingleton, IUnloadableSingleton
    {
        public void Load() { }
        public void Unload() { }
    }
}
