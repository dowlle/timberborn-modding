using Timberborn.Modding;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace ArchipelagoIntegration
{
    // IModStarter is the native Update 6 entry point for mods.
    // StartMod is called once when the game loads the mod, before any game scene.
    public class ArchipelagoStarter : IModStarter
    {
        public void StartMod(IModEnvironment modEnvironment)
        {
            var go = new GameObject("ArchipelagoTicker");
            go.AddComponent<ArchipelagoTicker>();
            Object.DontDestroyOnLoad(go);

            Debug.Log("[Archipelago] Mod loaded — ticker running.");
        }
    }
}