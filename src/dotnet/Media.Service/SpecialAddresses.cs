namespace ActualChat.Media;

public static class SpecialAddresses
{
    private static class IPv4 {
        public const string ThisNetwork = "0.0.0.0/8";
        public const string PrivateA = "10.0.0.0/8";
        public const string CGNAT = "100.64.0.0/10";
        public const string Loopback = "127.0.0.0/8";
        public const string LinkLocal = "169.254.0.0/16";
        public const string PrivateB = "172.16.0.0/12";
        public const string IETFProtocol = "192.0.0.0/24";
        public const string TestNet1 = "192.0.2.0/24";
        public const string IPv6ToIPv4 = "192.88.99.0/24";
        public const string PrivateC = "192.168.0.0/16";
        public const string Benchmark = "198.18.0.0/15";
        public const string TestNet2 = "198.51.100.0/24";
        public const string TestNet3 = "203.0.113.0/24";
        public const string Multicast = "224.0.0.0/4";
        public const string MulticastTest = "233.252.0.0/24";
        public const string Future = "240.0.0.0/4";
        public const string Broadcast = "255.255.255.255/32";
    }

    private static class IPv6 {
        public const string Unspecified = "::/128";
        public const string Loopback = "::1/128";
        public const string Mapped = "::ffff:0:0/96";
        public const string Translated = "::ffff:0:0:0:0/96";
        public const string NAT64 = "64:ff9b::/96";
        public const string NAT64Local = "64:ff9b:1::/48";
        public const string DiscardPrefix = "100::/64";
        public const string Teredo = "2001::/32";
        public const string ORCHIDv2 = "2001:20::/28";
        public const string Documentation = "2001:db8::/32";
        public const string SixToFour = "2002::/16";
        public const string Documentation2 = "3fff::/20";
        public const string SRv6 = "5f00::/16";
        public const string UniqueLocal = "fc00::/7";
        public const string LinkLocal1 = "fe80::/64";
        public const string LinkLocal2 = "fe80::/10";
        public const string Multicast = "ff00::/8";
    }

    public static readonly string[] Subnets = [
        IPv4.ThisNetwork,
        IPv4.PrivateA,
        IPv4.CGNAT,
        IPv4.Loopback,
        IPv4.LinkLocal,
        IPv4.PrivateB,
        IPv4.IETFProtocol,
        IPv4.TestNet1,
        IPv4.IPv6ToIPv4,
        IPv4.PrivateC,
        IPv4.Benchmark,
        IPv4.TestNet2,
        IPv4.TestNet3,
        IPv4.Multicast,
        IPv4.MulticastTest,
        IPv4.Future,
        IPv4.Broadcast,
        IPv6.Unspecified,
        IPv6.Loopback,
        IPv6.Mapped,
        IPv6.Translated,
        IPv6.NAT64,
        IPv6.NAT64Local,
        IPv6.DiscardPrefix,
        IPv6.Teredo,
        IPv6.ORCHIDv2,
        IPv6.Documentation,
        IPv6.SixToFour,
        IPv6.Documentation2,
        IPv6.SRv6,
        IPv6.UniqueLocal,
        IPv6.LinkLocal1,
        IPv6.LinkLocal2,
        IPv6.Multicast,
    ];
}
