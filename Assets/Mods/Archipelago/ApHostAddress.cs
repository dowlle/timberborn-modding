using System;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Turns the Host field into the host string handed to MultiClient.Net.
    ///
    /// Windows resolves "localhost" to ::1 first. A local MultiServer listens on
    /// 127.0.0.1 only, so each attempt waits about 2 s for the refused ::1 connect.
    /// Without a scheme MultiClient.Net tries wss:// and then ws://, and the two slow
    /// attempts exceed its login timeout ("Connection timed out"). For loopback hosts
    /// we therefore connect to 127.0.0.1 over plain ws:// directly. Other hosts
    /// (archipelago.gg and the like) are passed through unchanged.
    /// </summary>
    public static class ApHostAddress
    {
        private const string Ws = "ws://";
        private const string Wss = "wss://";

        public static string ForConnect(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return host;
            var text = host.Trim();

            string scheme = null;
            if (text.StartsWith(Ws, StringComparison.OrdinalIgnoreCase)) scheme = Ws;
            else if (text.StartsWith(Wss, StringComparison.OrdinalIgnoreCase)) scheme = Wss;
            var rest = scheme == null ? text : text.Substring(scheme.Length);

            int end = rest.IndexOfAny(new[] { ':', '/' });
            var name = end < 0 ? rest : rest.Substring(0, end);
            var suffix = end < 0 ? "" : rest.Substring(end);

            bool isLocalhost = name.Equals("localhost", StringComparison.OrdinalIgnoreCase);
            if (!isLocalhost && !IsIpv4Loopback(name)) return host;

            if (isLocalhost) name = "127.0.0.1";
            return (scheme ?? Ws) + name + suffix;
        }

        private static bool IsIpv4Loopback(string name)
        {
            var parts = name.Split('.');
            if (parts.Length != 4 || parts[0] != "127") return false;
            foreach (var part in parts)
                if (!byte.TryParse(part, out _)) return false;
            return true;
        }
    }
}
