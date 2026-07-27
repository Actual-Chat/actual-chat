using System.Net;
using System.Net.Sockets;
using ActualChat.Hashing;
using IPNetwork = System.Net.IPNetwork;

namespace ActualChat.Resilience;

public enum RateLimitIdentityKind
{
    Session = 0,
    UserId,
    IP,
    Target,
}

/// <summary>
/// One identity dimension an inbound call is charged against.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct RateLimitIdentity(RateLimitIdentityKind Kind, string Value)
{
    // Ranges that many unrelated callers share, so charging them as one caller carries no signal
    private static readonly IPNetwork[] SharedNetworks = [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("169.254.0.0/16"),
    ];

    public static RateLimitIdentity? ForIP(IPAddress? address)
        => ForIP(ToChargeableIP(address));

    public static RateLimitIdentity? ForIP(string? chargeableIP)
        => chargeableIP.IsNullOrEmpty()
            ? null
            : new RateLimitIdentity(RateLimitIdentityKind.IP, chargeableIP);

    public static string? ToChargeableIP(IPAddress? address)
    {
        // Canonical text form, or null for an address many unrelated callers share.
        // Callers store the result, so this runs once per connection rather than per call.
        if (address is null)
            return null;
        if (address.AddressFamily is AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6UniqueLocal)
            return null;

        foreach (var network in SharedNetworks)
            if (network.Contains(address))
                return null;

        return address.ToString();
    }

    // The value can identify a person, so logs and Redis keys carry its hash instead
    public string GetValueHash()
        => Value.Hash().SHA256().ToBase64HashString(HashAlgorithm.SHA256);

    public override string ToString()
        => $"{Kind}:{Value}";
}
