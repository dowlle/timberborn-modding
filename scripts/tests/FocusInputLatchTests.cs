using System;
using ArchipelagoIntegration;

// Standalone test executable; compile together with FocusInputLatch.cs.
// Models the game's InputBlocker counter: hotkeys run only while it is zero.
public static class FocusInputLatchTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static int Main()
    {
        int blockers = 0;
        FocusInputLatch Latch() => new FocusInputLatch(() => blockers++, () => blockers--);

        var host = Latch();
        var port = Latch();

        host.Acquire();
        Check(blockers == 1 && host.Holding, "focusing a field blocks hotkeys");
        host.Acquire();
        Check(blockers == 1, "a repeated focus event does not stack blocks");

        // Tab from Host to Port: FocusOut on Host, then FocusIn on Port.
        host.Release();
        port.Acquire();
        Check(blockers == 1 && !host.Holding && port.Holding, "moving between fields keeps exactly one block");

        port.Release();
        Check(blockers == 0, "leaving the last field unblocks hotkeys");
        port.Release();
        Check(blockers == 0, "an unpaired focus-out never unblocks the game's own blocks");

        // Panel hidden while a field has focus, then released by the panel.
        blockers = 1; // a game dialog holds its own block
        host.Acquire();
        host.Release();
        port.Release();
        Check(blockers == 1, "releasing all latches leaves other blockers alone");

        bool threw = false;
        try { new FocusInputLatch(null, () => { }); } catch (ArgumentNullException) { threw = true; }
        Check(threw, "null callbacks are rejected");

        Console.WriteLine("FocusInputLatchTests: PASS");
        return 0;
    }
}
