using System;
using System.Collections.Generic;
using System.Linq;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// A place received goods can be delivered into. The game adapter wraps a
    /// stockpile inventory; tests supply fakes. FreeCapacity must already account
    /// for the storage's good filter and for capacity reserved by haulers.
    /// </summary>
    internal interface IGoodsStorageSlot
    {
        bool PublicInput { get; }
        bool Enabled { get; }
        bool Accepts(string goodId);
        int FreeCapacity(string goodId);
    }

    internal readonly struct GoodsDelivery
    {
        public readonly IGoodsStorageSlot Slot;
        public readonly string GoodId;
        public readonly int Amount;

        public GoodsDelivery(IGoodsStorageSlot slot, string goodId, int amount)
        {
            Slot = slot;
            GoodId = goodId;
            Amount = amount;
        }
    }

    internal sealed class GoodsDeliveryResult
    {
        public List<GoodsDelivery> Delivered { get; } = new();
        public List<(GoodsDelivery Delivery, Exception Error)> Failed { get; } = new();
        public int TotalDelivered => Delivered.Sum(d => d.Amount);
    }

    /// <summary>
    /// Per-save stock of received goods that have not reached storage yet
    /// (good id -> amount). Goods only leave the ledger once a delivery into a
    /// finished public storage succeeded, so nothing is dropped when the colony
    /// has no suitable storage. Pure logic: no Unity or Timberborn types, so the
    /// standalone contract test can compile it on its own.
    /// </summary>
    internal sealed class PendingGoodsLedger
    {
        private readonly SortedDictionary<string, int> _pending = new(StringComparer.Ordinal);

        /// <summary>Fired whenever the pending amounts change.</summary>
        public event Action Changed;

        public bool IsEmpty => _pending.Count == 0;

        public int TotalAmount => _pending.Values.Sum();

        public IReadOnlyDictionary<string, int> Entries => _pending;

        public int Get(string goodId) =>
            goodId != null && _pending.TryGetValue(goodId, out var amount) ? amount : 0;

        public void Add(string goodId, int amount)
        {
            if (string.IsNullOrEmpty(goodId) || amount <= 0) return;
            long total = (long)Get(goodId) + amount;
            _pending[goodId] = (int)Math.Min(total, int.MaxValue);
            Changed?.Invoke();
        }

        private void Remove(string goodId, int amount)
        {
            int left = Get(goodId) - amount;
            if (left > 0) _pending[goodId] = left;
            else _pending.Remove(goodId);
        }

        /// <summary>
        /// Plans deliveries without touching storage: every pending good goes into
        /// enabled public-input slots that accept it, never more than each slot's
        /// free capacity. Capacity used by earlier planned deliveries is subtracted
        /// from the same slot for every good, so shared capacity is never overbooked.
        /// </summary>
        public List<GoodsDelivery> Plan(IReadOnlyList<IGoodsStorageSlot> slots)
        {
            var plan = new List<GoodsDelivery>();
            if (slots == null || slots.Count == 0 || IsEmpty) return plan;

            var used = new int[slots.Count];
            foreach (var entry in _pending)
            {
                int remaining = entry.Value;
                for (int i = 0; i < slots.Count && remaining > 0; i++)
                {
                    var slot = slots[i];
                    if (slot == null || !slot.PublicInput || !slot.Enabled || !slot.Accepts(entry.Key))
                        continue;
                    int free = slot.FreeCapacity(entry.Key) - used[i];
                    if (free <= 0) continue;
                    int take = Math.Min(remaining, free);
                    plan.Add(new GoodsDelivery(slot, entry.Key, take));
                    used[i] += take;
                    remaining -= take;
                }
            }
            return plan;
        }

        /// <summary>
        /// Plans and executes deliveries. <paramref name="give"/> performs one
        /// delivery and throws if the storage rejected it; rejected amounts stay
        /// pending for a later pass.
        /// </summary>
        public GoodsDeliveryResult Deliver(IReadOnlyList<IGoodsStorageSlot> slots,
                                           Action<IGoodsStorageSlot, string, int> give)
        {
            var result = new GoodsDeliveryResult();
            foreach (var delivery in Plan(slots))
            {
                try
                {
                    give(delivery.Slot, delivery.GoodId, delivery.Amount);
                    Remove(delivery.GoodId, delivery.Amount);
                    result.Delivered.Add(delivery);
                }
                catch (Exception ex)
                {
                    result.Failed.Add((delivery, ex));
                }
            }
            if (result.Delivered.Count > 0) Changed?.Invoke();
            return result;
        }

        /// <summary>"Log:50|Plank:20"; empty string when nothing is pending.</summary>
        public string Serialize() =>
            string.Join("|", _pending.Select(e => $"{e.Key}:{e.Value}"));

        /// <summary>Replaces the contents from Serialize() output; malformed entries are skipped.</summary>
        public void Load(string raw)
        {
            _pending.Clear();
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var part in raw.Split('|'))
                {
                    int colon = part.LastIndexOf(':');
                    if (colon <= 0) continue;
                    if (!int.TryParse(part.Substring(colon + 1), out var amount) || amount <= 0) continue;
                    var goodId = part.Substring(0, colon);
                    long total = (long)Get(goodId) + amount;
                    _pending[goodId] = (int)Math.Min(total, int.MaxValue);
                }
            }
            Changed?.Invoke();
        }

        /// <summary>"20 Planks, 50 Logs" using the given display names, or "" when empty.</summary>
        public string Describe(Func<string, string> displayName) =>
            string.Join(", ", _pending.Select(e => $"{e.Value} {displayName?.Invoke(e.Key) ?? e.Key}"));
    }
}
