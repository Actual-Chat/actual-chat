using ActualLab.Diagnostics;
using StoreKit;

namespace ActualChat.App.Maui;

/// <summary>
/// Whether this process may use CallKit. App Review rejects a China-storefront build that keeps it
/// (MIIT requirement). Decided once: <see cref="IosCalls"/> creates its provider on first use, and
/// PushKit can't be started for half a process.
/// </summary>
public static class IosCallKit
{
    public static bool IsAvailable { get; } = !IsChinaStorefront();

    // Private methods

    private static bool IsChinaStorefront()
    {
        // TODO: switch to StoreKit 2's Storefront.current once .NET for iOS binds it
#pragma warning disable CA1422
        var isChina = SKPaymentQueue.DefaultQueue.Storefront?.CountryCode == "CHN";
#pragma warning restore CA1422
        if (isChina)
            StaticLog.For(typeof(IosCallKit)).LogInformation("CallKit is off: China storefront");
        return isChina;
    }
}
