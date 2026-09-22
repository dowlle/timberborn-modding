using System;
using System.Reflection;

namespace ArchipelagoIntegration
{
    /// <summary>Resolves the overflow-preserving delivery API across game versions.</summary>
    internal static class InventoryDeliveryCompatibility
    {
        internal static MethodInfo ResolveGiveIgnoringCapacity(Type inventoryType,
                                                               Type goodAmountType)
        {
            if (inventoryType == null || goodAmountType == null) return null;

            var flags = BindingFlags.Instance | BindingFlags.Public;
            var parameters = new[] { goodAmountType };
            // 1.1 distinguishes existing, imported and produced stock. GiveImported
            // checks capacity, so it cannot replace the old overflow behavior.
            return inventoryType.GetMethod("GiveExistingIgnoringCapacity", flags,
                       null, parameters, null)
                ?? inventoryType.GetMethod("GiveIgnoringCapacity", flags,
                       null, parameters, null);
        }
    }
}
