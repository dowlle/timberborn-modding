using System;
using System.Collections.Generic;
using ArchipelagoIntegration;

// Standalone test executable; compile together with PendingGoodsLedger.cs.
public static class PendingGoodsLedgerTests
{
    private sealed class FakeSlot : IGoodsStorageSlot
    {
        public string Name;
        public bool PublicInput { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public HashSet<string> Takes = new();
        public int Capacity;
        public bool Throws;
        public readonly Dictionary<string, int> Stock = new();

        public bool Accepts(string goodId) => Takes.Contains(goodId);

        public int FreeCapacity(string goodId)
        {
            int total = 0;
            foreach (var amount in Stock.Values) total += amount;
            return Math.Max(0, Capacity - total);
        }

        public void Give(string goodId, int amount)
        {
            if (Throws) throw new InvalidOperationException("rejected");
            if (!Accepts(goodId) || !PublicInput || !Enabled) throw new Exception(Name + " received a good it must not get");
            if (amount > FreeCapacity(goodId)) throw new Exception(Name + " was given more than its free capacity");
            Stock.TryGetValue(goodId, out var have);
            Stock[goodId] = have + amount;
        }
    }

    private static FakeSlot Slot(string name, int capacity, params string[] goods)
    {
        var slot = new FakeSlot { Name = name, Capacity = capacity };
        foreach (var good in goods) slot.Takes.Add(good);
        return slot;
    }

    private static GoodsDeliveryResult Deliver(PendingGoodsLedger ledger, params FakeSlot[] slots) =>
        ledger.Deliver(slots, (slot, goodId, amount) => ((FakeSlot)slot).Give(goodId, amount));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static int Held(FakeSlot slot, string goodId) =>
        slot.Stock.TryGetValue(goodId, out var amount) ? amount : 0;

    public static int Main()
    {
        // Receiving adds up per good and ignores empty amounts.
        var ledger = new PendingGoodsLedger();
        int changes = 0;
        ledger.Changed += () => changes++;
        ledger.Add("Plank", 20);
        ledger.Add("Plank", 5);
        ledger.Add("Log", 0);
        ledger.Add("", 10);
        Check(ledger.Get("Plank") == 25 && ledger.Get("Log") == 0 && ledger.TotalAmount == 25, "aggregation");
        Check(changes == 2, "Changed fires once per real add");

        // The playtest bug: no finished storage at all. Nothing is delivered, nothing is lost.
        var result = Deliver(ledger);
        Check(result.Delivered.Count == 0 && ledger.Get("Plank") == 25, "no storage keeps goods pending");

        // Carry slots, construction sites and storage for other goods never receive.
        var carry = Slot("carry", 1000, "Plank"); carry.PublicInput = false;
        var site = Slot("site", 1000, "Plank"); site.Enabled = false;
        var logPile = Slot("logs", 1000, "Log");
        result = Deliver(ledger, carry, site, logPile);
        Check(result.Delivered.Count == 0 && ledger.Get("Plank") == 25, "ineligible slots rejected");
        Check(carry.Stock.Count == 0 && site.Stock.Count == 0 && logPile.Stock.Count == 0, "ineligible slots untouched");

        // Capacity is respected and split across storage; the rest waits.
        var small = Slot("small", 10, "Plank");
        var medium = Slot("medium", 8, "Plank");
        result = Deliver(ledger, small, medium);
        Check(Held(small, "Plank") == 10 && Held(medium, "Plank") == 8, "fills up to free capacity");
        Check(ledger.Get("Plank") == 7 && result.TotalDelivered == 18, "remainder stays pending");
        Check(ledger.Describe(id => id == "Plank" ? "Planks" : id) == "7 Planks", "description");

        // Full storage delivers nothing; a new warehouse later takes the rest.
        result = Deliver(ledger, small, medium);
        Check(result.Delivered.Count == 0 && ledger.Get("Plank") == 7, "full storage keeps goods pending");
        var warehouse = Slot("warehouse", 200, "Plank");
        result = Deliver(ledger, small, medium, warehouse);
        Check(Held(warehouse, "Plank") == 7 && ledger.IsEmpty, "later storage receives remainder");

        // Several goods sharing one slot's capacity are never overbooked.
        var shared = new PendingGoodsLedger();
        shared.Add("Log", 30);
        shared.Add("Plank", 30);
        var mixed = Slot("mixed", 40, "Log", "Plank");
        var plan = shared.Plan(new IGoodsStorageSlot[] { mixed });
        int planned = 0;
        foreach (var delivery in plan) planned += delivery.Amount;
        Check(planned == 40, "shared capacity planned once");
        Deliver(shared, mixed);
        Check(Held(mixed, "Log") + Held(mixed, "Plank") == 40 && shared.TotalAmount == 20, "shared capacity delivery");

        // A rejected delivery stays pending and is reported.
        var failing = new PendingGoodsLedger();
        failing.Add("Gear", 12);
        var broken = Slot("broken", 50, "Gear"); broken.Throws = true;
        result = Deliver(failing, broken);
        Check(result.Failed.Count == 1 && failing.Get("Gear") == 12, "rejected delivery stays pending");

        // Save round-trip, including malformed entries from a damaged save.
        var saved = new PendingGoodsLedger();
        saved.Add("Plank", 20);
        saved.Add("MetalBlock", 3);
        var raw = saved.Serialize();
        Check(raw == "MetalBlock:3|Plank:20", "serialize format: " + raw);
        var loaded = new PendingGoodsLedger();
        loaded.Load(raw + "|bad|Log:-4|Log:x|:5|Plank:1");
        Check(loaded.Get("Plank") == 21 && loaded.Get("MetalBlock") == 3 && loaded.Get("Log") == 0, "tolerant load");
        loaded.Load("");
        Check(loaded.IsEmpty && loaded.Serialize() == "", "empty load clears");

        Console.WriteLine("PASS: aggregation, no storage, ineligible slots, capacity split, full storage, " +
                          "shared capacity, rejected delivery, save round-trip");
        return 0;
    }
}
