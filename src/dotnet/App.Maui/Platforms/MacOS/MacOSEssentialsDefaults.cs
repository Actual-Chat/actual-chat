using LabsEssentialsExtensions = Microsoft.Maui.Platforms.MacOS.Essentials.EssentialsExtensions;

namespace ActualChat.App.Maui;

// TODO(maui-labs): delete once the labs Essentials package exposes a public way to set the
// statics, or MAUI Essentials ships a macos implementation (then Program.Main drops the call too).
/// <summary>
/// Applies the labs Essentials statics patch (FileSystem, Preferences, DeviceInfo, ...) before
/// any managed code runs: AddMacOSEssentials() does the same, but only once the MauiAppBuilder
/// exists - too late for MauiProgram's static ctor, whose MauiDiagnostics.Initialize() already
/// reads FileSystem.AppDataDirectory (log file) and CacheDirectory (Sentry cache).
/// </summary>
public static class MacOSEssentialsDefaults
{
    public static void Apply()
        => (typeof(LabsEssentialsExtensions)
                .GetMethod("SetEssentialsDefaults", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw StandardError.Constraint("No 'SetEssentialsDefaults' method in EssentialsExtensions - maui-labs renamed it?"))
            .Invoke(null, null);
}
