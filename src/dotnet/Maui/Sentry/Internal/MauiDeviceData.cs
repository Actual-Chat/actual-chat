#if MACOS
using CoreFoundation;
using Foundation;
#endif
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Sentry.Extensibility;
using Sentry.Protocol;
using Device = Sentry.Protocol.Device;

namespace ActualChat.Maui.Sentry.Internal;

internal static class MauiDeviceData
{
#if ANDROID
    private static readonly Lock JniLock = new();
#else
    private static int _isDisplayCaptureQueued;
    private static int _isDisplayChangeTracked;
#endif
    private static DisplaySnapshot? _displaySnapshot;

#if MACOS
    // Essentials' MainThread is its "not implemented" neutral build on the macos TFM
    // TODO(maui-labs): use MainThread once it's implemented there
    private static bool IsMainThread => NSThread.Current.IsMainThread;
#elif !ANDROID
    private static bool IsMainThread => MainThread.IsMainThread;
#endif

    public static void ApplyMauiDeviceData(this Device device, IDiagnosticLogger? logger)
    {
        try {
            // TODO: Add more device data where indicated

            // https://docs.microsoft.com/dotnet/maui/platform-integration/device/information
            var deviceInfo = DeviceInfo.Current;
            if (deviceInfo.Platform == DevicePlatform.Unknown) {
                // return early so we don't get NotImplementedExceptions (i.e., in unit tests, etc.)
                return;
            }
            device.Name ??= deviceInfo.Name;
            device.Manufacturer ??= deviceInfo.Manufacturer;
            device.Model ??= deviceInfo.Model;
#if ANDROID
            // DeviceInfo.Idiom is not threadsafe on Android
            // See: https://github.com/getsentry/sentry-dotnet/issues/3627
            lock (JniLock)
            {
                device.DeviceType ??= deviceInfo.Idiom.ToString();
            }
#else
            device.DeviceType ??= deviceInfo.Idiom.ToString();
#endif
            device.Simulator ??= deviceInfo.DeviceType switch {
                DeviceType.Virtual => true,
                DeviceType.Physical => false,
                _ => null
            };
            // device.Brand ??= ?
            // device.Family ??= ?
            // device.ModelId ??= ?
            // device.Architecture ??= ?
            // ? = deviceInfo.Platform;
            // ? = deviceInfo.VersionString;

            // Do not collect battery info.
            // https://docs.microsoft.com/dotnet/maui/platform-integration/device/battery
            // try {
            //     var battery = Battery.Default;
            //     device.BatteryLevel ??= battery.ChargeLevel < 0 ? null : (short)battery.ChargeLevel;
            //     device.BatteryStatus ??= battery.State.ToString();
            //     device.IsCharging ??= battery.State switch {
            //         BatteryState.Unknown => null,
            //         BatteryState.Charging => true,
            //         _ => false
            //     };
            // }
            // catch (PermissionException) {
            //     logger?.LogDebug("No permission to read battery state from the device");
            // }

            // https://docs.microsoft.com/dotnet/maui/platform-integration/communication/networking#using-connectivity
            try {
                device.IsOnline ??= Connectivity.NetworkAccess == NetworkAccess.Internet;
            }
            catch (PermissionException) {
                logger?.LogDebug("No permission to read network state from the device");
            }

#if ANDROID
            // DeviceDisplay.Current is not threadsafe on Android.
            // See: https://github.com/getsentry/sentry-dotnet/issues/3627
            lock (JniLock)
                ReadDisplaySnapshot().ApplyTo(device);
#else
            // DeviceDisplay.Current must be read on the UI thread here, see
            // https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device/display?view=net-maui-8.0&tabs=macios#platform-differences
            // An event logged off the UI thread gets the last snapshot instead of waiting for it:
            // this runs inside the logging call, and the UI thread may itself be waiting on the
            // thread that logs - which is how it deadlocked into a background watchdog kill (#4869).
            if (IsMainThread)
                ReadDisplaySnapshot().ApplyTo(device);
            else if (Volatile.Read(ref _displaySnapshot) is { } displaySnapshot)
                displaySnapshot.ApplyTo(device);
            else if (Interlocked.Exchange(ref _isDisplayCaptureQueued, 1) == 0)
                BeginInvokeOnMainThread(() => ReadDisplaySnapshot());
#endif

            // https://docs.microsoft.com/dotnet/maui/platform-integration/device/vibrate
            device.SupportsVibration ??= Vibration.Default.IsSupported;

            // https://docs.microsoft.com/dotnet/maui/platform-integration/device/sensors
            device.SupportsAccelerometer ??= Accelerometer.IsSupported;
            device.SupportsGyroscope ??= Gyroscope.IsSupported;

            // https://docs.microsoft.com/dotnet/maui/platform-integration/device/geolocation
            // TODO: How to get without actually trying to make a location request?
            // device.SupportsLocationService ??= Geolocation.Default.???

            // device.SupportsAudio ??= ?

            // device.MemorySize ??=
            // device.FreeMemory ??=
            // device.UsableMemory ??=
            // device.LowMemory ??=

            // device.StorageSize ??=
            // device.FreeStorage ??=
            // device.ExternalStorageSize ??=
            // device.ExternalFreeStorage ??=

            // device.BootTime ??=
            // device.DeviceUniqueIdentifier ??=

            //device.CpuDescription ??= ?
            //device.ProcessorCount ??= ?
            //device.ProcessorFrequency ??= ?
        }
        catch (Exception ex) {
            // Log, but swallow the exception so we can continue sending events
            logger?.LogError(ex, "Error getting MAUI device information");
        }
    }

    // Private methods

    private static DisplaySnapshot ReadDisplaySnapshot()
    {
        // https://docs.microsoft.com/dotnet/maui/platform-integration/device/display
        var snapshot = ToSnapshot(DeviceDisplay.MainDisplayInfo);
        Volatile.Write(ref _displaySnapshot, snapshot);
#if !ANDROID
        TrackDisplayChanges();
#endif
        return snapshot;
    }

    private static DisplaySnapshot ToSnapshot(DisplayInfo display)
        => new(
            $"{(int)display.Width}x{(int)display.Height}",
            (float)display.Density,
            display.Orientation switch {
                DisplayOrientation.Portrait => DeviceOrientation.Portrait,
                DisplayOrientation.Landscape => DeviceOrientation.Landscape,
                _ => null,
            });

#if !ANDROID
    private static void TrackDisplayChanges()
    {
        // Rotation and monitor changes arrive on the UI thread, so the snapshot stays current
        // for events logged elsewhere
        if (Interlocked.Exchange(ref _isDisplayChangeTracked, 1) != 0)
            return;

        DeviceDisplay.Current.MainDisplayInfoChanged += (_, e)
            => Volatile.Write(ref _displaySnapshot, ToSnapshot(e.DisplayInfo));
    }
#endif

#if MACOS
    private static void BeginInvokeOnMainThread(Action action)
        => DispatchQueue.MainQueue.DispatchAsync(action);
#elif !ANDROID
    private static void BeginInvokeOnMainThread(Action action)
        => MainThread.BeginInvokeOnMainThread(action);
#endif

    // Nested types

    private sealed record DisplaySnapshot(
        string ScreenResolution,
        float ScreenDensity,
        DeviceOrientation? Orientation)
    {
        public void ApplyTo(Device device)
        {
            device.ScreenResolution ??= ScreenResolution;
            device.ScreenDensity ??= ScreenDensity;
            device.Orientation ??= Orientation;
        }
    }
}
