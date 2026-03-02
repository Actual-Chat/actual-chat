using System.Net;
using System.Net.Sockets;
using ActualChat.Rpc;

namespace ActualChat.Maui;

public sealed class MauiHostNameRemapper : HostNameRemapper
{
    private volatile string? _ip;

    public static void Use()
#if ANDROID || WINDOWS
        => Instance = new MauiHostNameRemapper();
#else
    {
        // On iOS/macCatalyst, MauiHttpClientFactory uses NSUrlSessionHandler (native stack),
        // so SocketsHttpHandler with ConnectCallback cannot be used.
        // See how/where HostNameRemapper.Instance is used.
    }
#endif

    private MauiHostNameRemapper()
    {
        _ip = MauiPreferences.GetHostIp(MauiSettings.Host);
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
            MauiPreferences.SetHostIp(MauiSettings.Host, ip);
        }
#pragma warning disable RCS1075 // Avoid catching general exception
        catch (Exception) {
            // DNS resolution failed - keep using cached value or hostname
        }
#pragma warning restore RCS1075
    }
}
