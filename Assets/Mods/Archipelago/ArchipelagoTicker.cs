using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Persistent MonoBehaviour that ticks the Archipelago item queue every frame.
    /// Created once in ArchipelagoStarter.StartMod() and kept alive for the full
    /// process lifetime via DontDestroyOnLoad.
    /// </summary>
    internal sealed class ArchipelagoTicker : MonoBehaviour
    {
        private void Update()
        {
            if (ArchipelagoManager.IsConnected)
            {
                ArchipelagoManager.DrainItemQueue();
                ApMilestoneTracker.Instance?.CheckMilestones();
            }
        }
    }
}
