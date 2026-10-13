using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.AppModel;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.DataTransfer;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Devices;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Networking;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Storage;

namespace ActualChat.App.Maui;

// TODO(maui-labs): delete once the labs Essentials package sets all of these itself, or MAUI
// Essentials ships a Linux implementation (then Program.Main drops the call too).

/// <summary>
/// Points the MAUI Essentials static facades at the labs Linux implementations before any managed
/// code runs: the GTK backend lives on the plain net TFM, where every facade is the "not
/// implemented" neutral build. AddLinuxGtk4Essentials() patches only five of them, and only once
/// the MauiAppBuilder exists - too late for MauiProgram's static ctor.
/// </summary>
public static class LinuxEssentialsDefaults
{
    public static void Apply()
    {
        Set(typeof(FileSystem), new LinuxFileSystem());
        Set(typeof(Preferences), new LinuxPreferences());
        Set(typeof(SecureStorage), new LinuxSecureStorage());
        Set(typeof(DeviceInfo), new LinuxDeviceInfo());
        Set(typeof(DeviceDisplay), new LinuxDeviceDisplay());
        Set(typeof(Connectivity), new LinuxConnectivity());
        Set(typeof(Browser), new LinuxBrowser());
        Set(typeof(Launcher), new LinuxLauncher());
        Set(typeof(Map), new LinuxMap());
        Set(typeof(Clipboard), new LinuxClipboard());
        Set(typeof(Share), new LinuxShare());
        Set(typeof(Geolocation), new LinuxGeolocation());
        Set(typeof(Vibration), new LinuxVibration());
        Set(typeof(Accelerometer), new LinuxAccelerometer());
        Set(typeof(Gyroscope), new LinuxGyroscope());
    }

    // Private methods

    private static void Set(Type facade, object implementation)
    {
        var setter = facade
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name is "SetCurrent" or "SetDefault"
                && m.GetParameters() is [var p] && p.ParameterType.IsInstanceOfType(implementation));
        if (setter is null)
            throw StandardError.Constraint($"No 'SetCurrent' or 'SetDefault' in {facade.Name} - MAUI renamed it?");

        setter.Invoke(null, [implementation]);
    }
}
