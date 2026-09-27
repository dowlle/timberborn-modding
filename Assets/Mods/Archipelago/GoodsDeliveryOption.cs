using System.Collections.Generic;

namespace ArchipelagoIntegration
{
    /// <summary>Where received goods (resource packages) are delivered.</summary>
    public enum GoodsDeliveryMode
    {
        /// <summary>Into the District Center first; stockpiles take what it does not.</summary>
        DistrictCenter = 0,
        /// <summary>Into finished stockpiles only (the pre-0.1.0 behaviour).</summary>
        Storage = 1,
    }

    /// <summary>
    /// Reads the goods_delivery option from slot_data. The APWorld sends the
    /// option value (0 = district_center, 1 = storage). A seed without the key
    /// (generated before the option existed) or with an unknown value gets the
    /// District Center. In both modes goods that find no room stay in the
    /// pending stock and are delivered later.
    /// </summary>
    public static class GoodsDeliveryOption
    {
        public const string SlotDataKey = "goods_delivery";
        public const GoodsDeliveryMode Default = GoodsDeliveryMode.DistrictCenter;

        public static GoodsDeliveryMode FromSlotData(IDictionary<string, object> slotData)
        {
            if (slotData == null || !slotData.TryGetValue(SlotDataKey, out var value))
                return Default;
            return Parse(value);
        }

        /// <summary>Accepts the option value as a number (long, int, JSON value) or its name.</summary>
        public static GoodsDeliveryMode Parse(object value)
        {
            var text = value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(text)) return Default;
            if (long.TryParse(text, out var number))
                return number == (long)GoodsDeliveryMode.Storage ? GoodsDeliveryMode.Storage : Default;
            switch (text.ToLowerInvariant())
            {
                case "storage": return GoodsDeliveryMode.Storage;
                default: return Default;
            }
        }

        /// <summary>Stored in the save as this number; unknown numbers load as the default.</summary>
        public static int ToSaveValue(GoodsDeliveryMode mode) => (int)mode;

        public static GoodsDeliveryMode FromSaveValue(int value) =>
            value == (int)GoodsDeliveryMode.Storage ? GoodsDeliveryMode.Storage : Default;

        public static string Describe(GoodsDeliveryMode mode) =>
            mode == GoodsDeliveryMode.Storage ? "storage" : "District Center, then storage";
    }
}
