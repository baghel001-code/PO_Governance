using System.Net;

namespace Infrastructure.Utilitys
{
    /// <summary>
    /// IP allow-list with CIDR ranges (IPv4 and IPv6; IPv4-mapped IPv6 addresses such as ::ffff:10.0.0.5 match their IPv4
    /// entries). Entries: "10.10.20.15", "10.10.21.0/24", "::1", "fd00::/8". Used by the SAP interface
    /// (SapInterface:AllowedIps / TrustedProxies).
    /// </summary>
    public sealed class IpAllowList
    {
        private readonly List<(byte[] Net, int Bits)> _ranges = new();

        public IpAllowList(IEnumerable<string>? entries)
        {
            foreach (var raw in entries ?? Array.Empty<string>())
            {
                var e = (raw ?? "").Trim();
                if (e.Length == 0) continue;
                if (!TryParseEntry(e, out var net, out var bits))
                    throw new FormatException("Invalid IP allow-list entry '" + e + "' (use an address or CIDR, e.g. 10.10.21.0/24)");
                _ranges.Add((net, bits));
            }
        }

        public int Count => _ranges.Count;

        public static bool TryParseEntry(string entry, out byte[] network, out int bits)
        {
            network = Array.Empty<byte>();
            bits = 0;
            var parts = entry.Split('/');
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0].Trim(), out var ip)) return false;
            ip = Normalise(ip);
            var bytes = ip.GetAddressBytes();
            var max = bytes.Length * 8;
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1].Trim(), out bits) || bits < 0 || bits > max) return false;
            }
            else bits = max;
            network = Mask(bytes, bits);
            return true;
        }

        private static IPAddress Normalise(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        private static byte[] Mask(byte[] bytes, int bits)
        {
            var r = (byte[])bytes.Clone();
            for (var i = 0; i < r.Length; i++)
            {
                var keep = Math.Clamp(bits - i * 8, 0, 8);
                r[i] = (byte)(r[i] & (0xFF << (8 - keep)));
            }
            return r;
        }

        public bool Contains(IPAddress? address)
        {
            if (address == null) return false;
            var ip = Normalise(address);
            var bytes = ip.GetAddressBytes();
            foreach (var (net, bits) in _ranges)
                if (net.Length == bytes.Length && Mask(bytes, bits).AsSpan().SequenceEqual(net)) return true;
            return false;
        }

        public bool Contains(string? address) => IPAddress.TryParse((address ?? "").Trim(), out var ip) && Contains(ip);

        /// <summary>
        /// The caller's address: the TCP peer, or — only when the peer is a trusted proxy — the right-most address of
        /// X-Forwarded-For that is not itself a trusted proxy (the address the proxy chain saw first-hand).
        /// </summary>
        public static IPAddress? ClientAddress(IPAddress? peer, string? forwardedFor, IpAllowList trustedProxies)
        {
            if (peer == null) return null;
            if (trustedProxies.Count == 0 || !trustedProxies.Contains(peer) || string.IsNullOrWhiteSpace(forwardedFor)) return Normalise(peer);
            var hops = forwardedFor.Split(',').Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
            for (var i = hops.Count - 1; i >= 0; i--)
            {
                if (!IPAddress.TryParse(StripPort(hops[i]), out var ip)) return Normalise(peer);   // garbage: trust only the peer
                if (!trustedProxies.Contains(ip)) return Normalise(ip);
            }
            return Normalise(peer);
        }

        private static string StripPort(string hop)
        {
            if (hop.StartsWith('[')) { var e = hop.IndexOf(']'); return e > 0 ? hop[1..e] : hop; }
            var c = hop.IndexOf(':');
            return c > 0 && hop.IndexOf(':', c + 1) < 0 ? hop[..c] : hop;   // "1.2.3.4:5678" (IPv6 without brackets left as is)
        }
    }
}
