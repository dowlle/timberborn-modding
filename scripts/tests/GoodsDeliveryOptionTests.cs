using System;
using System.Collections.Generic;
using ArchipelagoIntegration;

// Standalone test executable; compile together with GoodsDeliveryOption.cs.
// Contract for the goods_delivery slot_data option (APWorld Options.GoodsDelivery:
// district_center = 0, storage = 1).
public static class GoodsDeliveryOptionTests
{
    private static int _failures;

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            _failures++;
            Console.WriteLine("FAIL: " + name);
        }
    }

    public static int Main()
    {
        Check(GoodsDeliveryOption.SlotDataKey == "goods_delivery", "slot_data key");
        Check(GoodsDeliveryOption.FromSlotData(null) == GoodsDeliveryMode.DistrictCenter, "no slot_data: District Center");
        Check(GoodsDeliveryOption.FromSlotData(new Dictionary<string, object>()) == GoodsDeliveryMode.DistrictCenter,
              "older seed without the key: District Center");
        Check(GoodsDeliveryOption.FromSlotData(new Dictionary<string, object> { ["goods_delivery"] = 0L })
              == GoodsDeliveryMode.DistrictCenter, "0 (long): District Center");
        Check(GoodsDeliveryOption.FromSlotData(new Dictionary<string, object> { ["goods_delivery"] = 1L })
              == GoodsDeliveryMode.Storage, "1 (long): storage");
        Check(GoodsDeliveryOption.FromSlotData(new Dictionary<string, object> { ["goods_delivery"] = 1 })
              == GoodsDeliveryMode.Storage, "1 (int): storage");
        Check(GoodsDeliveryOption.Parse("1") == GoodsDeliveryMode.Storage, "\"1\": storage");
        Check(GoodsDeliveryOption.Parse("storage") == GoodsDeliveryMode.Storage, "name storage");
        Check(GoodsDeliveryOption.Parse("district_center") == GoodsDeliveryMode.DistrictCenter, "name district_center");
        Check(GoodsDeliveryOption.Parse(7L) == GoodsDeliveryMode.DistrictCenter, "unknown number: District Center");
        Check(GoodsDeliveryOption.Parse(null) == GoodsDeliveryMode.DistrictCenter, "null value: District Center");
        Check(GoodsDeliveryOption.Parse("nonsense") == GoodsDeliveryMode.DistrictCenter, "unknown name: District Center");
        foreach (GoodsDeliveryMode mode in Enum.GetValues(typeof(GoodsDeliveryMode)))
            Check(GoodsDeliveryOption.FromSaveValue(GoodsDeliveryOption.ToSaveValue(mode)) == mode, "save round-trip " + mode);
        Check(GoodsDeliveryOption.FromSaveValue(-1) == GoodsDeliveryMode.DistrictCenter, "unset save value: District Center");

        if (_failures > 0)
        {
            Console.WriteLine(_failures + " goods delivery check(s) failed");
            return 1;
        }
        Console.WriteLine("PASS: goods_delivery key, missing key default, numeric and named values, unknown values, save round-trip");
        return 0;
    }
}
