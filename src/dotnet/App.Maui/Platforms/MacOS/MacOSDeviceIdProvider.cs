using System.Runtime.InteropServices;
using ActualChat.Hashing;
using banditoth.MAUI.DeviceId.Interfaces;
using CoreFoundation;

namespace ActualChat.App.Maui;

/// <summary>
/// <see cref="IDeviceIdProvider"/> for the AppKit backend, where banditoth.MAUI.DeviceId has no
/// build: the device id is a hash of the Mac's hardware UUID, so it survives reinstalls and
/// preference wipes; the installation id is the per-install fallback for when IOKit has none.
/// Not a labs stand-in: the plugin's Apple id is the vendor id, which resets with the app.
/// </summary>
public sealed class MacOSDeviceIdProvider : IDeviceIdProvider
{
    private const string IOKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private static readonly Lazy<string?> HardwareDeviceIdLazy = new(GetHardwareDeviceId);
    private static readonly ILogger Log = StaticLog.For<MacOSDeviceIdProvider>();

    public string GetDeviceId()
        => HardwareDeviceIdLazy.Value ?? GetInstallationId();

    public string GetInstallationId()
        => MauiPreferences.InstallationId;

    // Private methods

    private static string? GetHardwareDeviceId()
    {
        var platformUuid = GetPlatformUuid();
        if (platformUuid.IsNullOrEmpty()) {
            Log.LogWarning("No IOPlatformUUID - falling back to the installation id");
            return null;
        }

        // The raw UUID is a machine-wide fingerprint every app can read, so only a salted hash leaves the process
        var deviceId = $"voxt-mac:{platformUuid}".Hash().SHA256().AlphaNumeric();
        Log.LogInformation("Device id from the hardware UUID: {DeviceId}", deviceId);
        return deviceId;
    }

    private static string? GetPlatformUuid()
    {
        var service = IOServiceGetMatchingService(0, IOServiceMatching("IOPlatformExpertDevice"));
        if (service == 0)
            return null;

        try {
            using var key = new CFString("IOPlatformUUID");
            var value = IORegistryEntryCreateCFProperty(service, key.Handle, IntPtr.Zero, 0);
            return value == IntPtr.Zero ? null : CFString.FromHandle(value, true);
        }
        finally {
            IOObjectRelease(service);
        }
    }

    [DllImport(IOKitLibrary)]
    private static extern uint IOServiceGetMatchingService(uint masterPort, IntPtr matching);
    [DllImport(IOKitLibrary)]
    private static extern IntPtr IOServiceMatching(string name);
    [DllImport(IOKitLibrary)]
    private static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);
    [DllImport(IOKitLibrary)]
    private static extern int IOObjectRelease(uint obj);
}
