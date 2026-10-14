using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using ActualChat.Rpc;

namespace ActualChat.App.Maui.Services;

public static class MauiRpcPaths
{
    public static RpcPathSettings CreateSettings()
    {
        var folder = Path.Combine(FileSystem.AppDataDirectory, "rpc-paths");
        return new RpcPathSettings(MauiSettings.RpcEndpoints, new FileRpcPathStore(folder), GetNetworkId);
    }

    // Per adapter: its subnet and gateways. A VPN is an adapter of its own, so it is a network of its own.
    public static string GetNetworkId()
    {
        var parts = new SortedSet<string>();
        try {
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

    private static string GetSubnet(byte[] address, int prefixLength)
    {
        for (var i = 0; i < address.Length; i++) {
            var bits = Math.Clamp(prefixLength - i * 8, 0, 8);
            address[i] &= (byte)(0xFF << (8 - bits));
        }
        return $"{new System.Net.IPAddress(address)}/{prefixLength}";
    }
}
