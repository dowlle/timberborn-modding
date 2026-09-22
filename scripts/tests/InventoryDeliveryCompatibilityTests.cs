using System;
using ArchipelagoIntegration;

// Standalone test executable; compile together with InventoryDeliveryCompatibility.cs.
public static class InventoryDeliveryCompatibilityTests
{
    public struct GoodAmount { public int Amount; }
    public class Legacy
    {
        public int Stock;
        public void GiveIgnoringCapacity(GoodAmount amount) { Stock += amount.Amount; }
    }
    public class Modern
    {
        public int Stock;
        public void GiveExistingIgnoringCapacity(GoodAmount amount) { Stock += amount.Amount; }
        public void GiveImported(GoodAmount amount) { throw new Exception("Capacity-checked API selected"); }
    }
    public class Both : Modern
    {
        public void GiveIgnoringCapacity(GoodAmount amount) { throw new Exception("Legacy API preferred"); }
    }
    public class ImportedOnly { public void GiveImported(GoodAmount amount) {} }
    public class WrongOverload { public void GiveExistingIgnoringCapacity(string amount) {} }
    public class StaticOnly { public static void GiveExistingIgnoringCapacity(GoodAmount amount) {} }

    public static int Main()
    {
        var legacy = new Legacy();
        var modern = new Modern();
        var both = new Both();
        Deliver(legacy, "GiveIgnoringCapacity");
        Deliver(modern, "GiveExistingIgnoringCapacity");
        Deliver(both, "GiveExistingIgnoringCapacity");
        if (legacy.Stock != 100 || modern.Stock != 100 || both.Stock != 100)
            throw new Exception("Resolved method did not deliver the goods");
        foreach (var type in new[] { typeof(ImportedOnly), typeof(WrongOverload), typeof(StaticOnly) })
            if (InventoryDeliveryCompatibility.ResolveGiveIgnoringCapacity(type, typeof(GoodAmount)) != null)
                throw new Exception("Unsafe fallback resolved for " + type.Name);
        if (InventoryDeliveryCompatibility.ResolveGiveIgnoringCapacity(null, typeof(GoodAmount)) != null ||
            InventoryDeliveryCompatibility.ResolveGiveIgnoringCapacity(typeof(Modern), null) != null)
            throw new Exception("Missing runtime types must not resolve");
        Console.WriteLine("PASS: legacy, modern, preference, unsupported API, overload, static and missing-type cases");
        return 0;
    }

    private static void Deliver(object inventory, string expectedName)
    {
        var method = InventoryDeliveryCompatibility.ResolveGiveIgnoringCapacity(inventory.GetType(), typeof(GoodAmount));
        if (method == null || method.Name != expectedName) throw new Exception("Unexpected delivery method");
        method.Invoke(inventory, new object[] { new GoodAmount { Amount = 100 } });
    }
}
