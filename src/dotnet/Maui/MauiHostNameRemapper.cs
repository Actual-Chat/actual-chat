using System.Net;
using System.Net.Sockets;
using ActualChat.Rpc;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public sealed class MauiHostNameRemapper : HostNameRemapper
{
    private const string PreferenceKeyPrefix = "host_ip_";

    private volatile string? _ip;

    public static void Use()
        => Instance = new MauiHostNameRemapper();

    private MauiHostNameRemapper()
    {
        var key = PreferenceKeyPrefix + MauiSettings.Host;
        _ip = Preferences.Default.Get(key, "").NullIfEmpty();
        if (_ip == null)
            _ = ResolveAsync();
    }

    public override string Get(string hostName)
        => OrdinalIgnoreCaseEquals(hostName, MauiSettings.Host) && _ip is { } ip
            ? ip
            : hostName;

    // Private methods

    private async Task ResolveAsync()
    {
        try {
            var addresses = await Dns.GetHostAddressesAsync(MauiSettings.Host).ConfigureAwait(false);
            var ipAddress = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
            if (ipAddress == null)
                return;

            var ip = ipAddress.ToString();
            Interlocked.Exchange(ref _ip, ip);
            var key = PreferenceKeyPrefix + MauiSettings.Host;
            Preferences.Default.Set(key, ip);
        }
#pragma warning disable RCS1075 // Avoid catching general exception
        catch (Exception) {
            // DNS resolution failed - keep using cached value or hostname
        }
#pragma warning restore RCS1075
    }
}
