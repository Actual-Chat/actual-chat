using System.Net;
using System.Net.Sockets;
using ActualChat.Rpc;

namespace ActualChat.Maui;

public sealed class MauiHostNameRemapper : HostNameRemapper
{
    private string? _ip;

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
        _ = Resolve();
    }

    public override string Get(string hostName)
        => string.Equals(hostName, MauiSettings.Host, StringComparison.OrdinalIgnoreCase)
            && Volatile.Read(ref _ip) is { } ip
            ? ip
            : hostName;

    // Private methods

    private async Task Resolve()
    {
        try {
            var resolvedAddresses = await Dns.GetHostAddressesAsync(MauiSettings.Host).ConfigureAwait(false);
            var ipv4Addresses = resolvedAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
            var addresses = (ipv4Addresses.Length > 0 ? ipv4Addresses : resolvedAddresses)
                .Distinct()
                .OrderBy(a => a.ToString())
                .ToArray();
            if (addresses.Length == 0)
                return;

            var index = MauiPreferences.InstallationId.GetXxHash3().PositiveModulo(addresses.Length);
            var ip = addresses[index].ToString();
            Interlocked.Exchange(ref _ip, ip);
            MauiPreferences.SetHostIp(MauiSettings.Host, ip);
            // StaticLog.For<MauiHostNameRemapper>()
            //     .LogInformation("Resolved {Host} to {IPAddress}", MauiSettings.Host, ip);
        }
#pragma warning disable RCS1075 // Avoid catching general exception
        catch (Exception) {
            // DNS resolution failed - keep using cached value or hostname
        }
#pragma warning restore RCS1075
    }
}
