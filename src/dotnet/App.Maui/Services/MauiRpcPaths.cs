using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using ActualChat.Rpc;
#if ANDROID
using Android.Content;
using Android.Net;
using Android.Telephony;
#endif

namespace ActualChat.App.Maui.Services;

public static class MauiRpcPaths
{
    public static RpcPathSettings CreateSettings()
    {
        var folder = Path.Combine(FileSystem.AppDataDirectory, "rpc-paths");
        return new RpcPathSettings(MauiSettings.RpcEndpoints, new FileRpcPathStore(folder), GetNetworkId);
    }

    public static string GetNetworkId()
    {
        // Subnets and gateways - per adapter, or of the active network on Android. A VPN is a network
        // of its own.
        var parts = new SortedSet<string>();
        try {
#if ANDROID
            AddActiveNetwork(parts);
#else
            AddAdapters(parts);
#endif
        }
        catch (Exception) {
            // Some platforms don't let an app look: then only the kind of connection tells networks apart
            parts.Clear();
        }
        if (parts.Count == 0)
            parts.Add(Connectivity.Current.ConnectionProfiles.ToDelimitedString());

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(';', parts)));
        return Convert.ToHexString(hash, 0, 8);
    }

    // Private methods

#if ANDROID
    private static void AddActiveNetwork(SortedSet<string> parts)
    {
        // Android throws on NetworkInterface gateways, so the network RPC actually goes through is read
        // from ConnectivityManager instead
        var context = Platform.AppContext;
        var connectivityManager = (ConnectivityManager?)context.GetSystemService(Context.ConnectivityService);
        if (connectivityManager?.ActiveNetwork is not { } network)
            return;

        var capabilities = connectivityManager.GetNetworkCapabilities(network);
        var linkProperties = connectivityManager.GetLinkProperties(network);
        if (capabilities is null || linkProperties is null)
            return;

        var isVpn = capabilities.HasTransport(TransportType.Vpn);
        if (!isVpn && capabilities.HasTransport(TransportType.Cellular)) {
            // A carrier gives out a new address on every attach, so the operator is what names the network
            var telephonyManager = (TelephonyManager?)context.GetSystemService(Context.TelephonyService);
            var dataTelephonyManager = telephonyManager?.CreateForSubscriptionId(
                SubscriptionManager.DefaultDataSubscriptionId);
            parts.Add($"Cellular|{(dataTelephonyManager ?? telephonyManager)?.NetworkOperator}");
            return;
        }

        var kind = isVpn ? "Vpn"
            : capabilities.HasTransport(TransportType.Wifi) ? "Wifi"
            : capabilities.HasTransport(TransportType.Ethernet) ? "Ethernet"
            : "Other";
        var gateways = linkProperties.Routes
            .Where(x => x.IsDefaultRoute && x.Gateway is { IsAnyLocalAddress: false })
            .Select(x => x.Gateway!.HostAddress)
            .Order();
        var subnets = linkProperties.LinkAddresses
            .Where(x => x.Address is Java.Net.Inet4Address)
            .Select(x => GetSubnet(x.Address!.GetAddress()!, x.PrefixLength));
        parts.Add($"{kind}|{string.Join(',', gateways)}|{string.Join(',', subnets)}");
    }
#else
    private static void AddAdapters(SortedSet<string> parts)
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var properties = adapter.GetIPProperties();
            var gateways = properties.GatewayAddresses
                .Select(x => x.Address.ToString())
                .Where(x => x != "0.0.0.0" && x != "::")
                .Order();
            var subnets = properties.UnicastAddresses
                .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => GetSubnet(x.Address.GetAddressBytes(), x.PrefixLength));
            parts.Add($"{adapter.NetworkInterfaceType}|{string.Join(',', gateways)}|{string.Join(',', subnets)}");
        }
    }
#endif

    private static string GetSubnet(byte[] address, int prefixLength)
    {
        for (var i = 0; i < address.Length; i++) {
            var bits = Math.Clamp(prefixLength - i * 8, 0, 8);
            address[i] &= (byte)(0xFF << (8 - bits));
        }
        return $"{new System.Net.IPAddress(address)}/{prefixLength}";
    }
}
