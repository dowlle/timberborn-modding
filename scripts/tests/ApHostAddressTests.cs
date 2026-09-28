using System;
using ArchipelagoIntegration;

// Standalone test executable; compile together with ApHostAddress.cs.
public static class ApHostAddressTests
{
    private static void Expect(string input, string expected)
    {
        var actual = ApHostAddress.ForConnect(input);
        if (actual != expected)
            throw new Exception($"FAIL: ForConnect(\"{input}\") = \"{actual}\", expected \"{expected}\"");
    }

    public static int Main()
    {
        // localhost goes to IPv4 over plain ws, skipping the slow ::1 and wss attempts.
        Expect("localhost", "ws://127.0.0.1");
        Expect("LocalHost", "ws://127.0.0.1");
        Expect(" localhost ", "ws://127.0.0.1");
        Expect("localhost:38281", "ws://127.0.0.1:38281");
        Expect("ws://localhost", "ws://127.0.0.1");
        Expect("WS://localhost:38281", "ws://127.0.0.1:38281");
        Expect("wss://localhost", "wss://127.0.0.1");

        // IPv4 loopback without a scheme also skips the wss attempt.
        Expect("127.0.0.1", "ws://127.0.0.1");
        Expect("127.0.0.1:38281", "ws://127.0.0.1:38281");
        Expect("ws://127.0.0.1", "ws://127.0.0.1");

        // Everything else keeps the library's behaviour.
        Expect("archipelago.gg", "archipelago.gg");
        Expect("archipelago.gg:38281", "archipelago.gg:38281");
        Expect("wss://archipelago.gg", "wss://archipelago.gg");
        Expect("192.168.1.20", "192.168.1.20");
        Expect("127.0.0.300", "127.0.0.300");
        Expect("localhost.example.com", "localhost.example.com");
        Expect("mylocalhost", "mylocalhost");
        Expect("", "");
        Expect(null, null);

        Console.WriteLine("ApHostAddressTests: PASS");
        return 0;
    }
}
