using Microsoft.Win32;

namespace ActualChat.App.Maui;

/// <summary>
/// Registers the app's custom URL scheme under HKCU. Packaged apps get this from
/// their manifest; this app is unpackaged (WindowsPackageType=None), so it must
/// register itself — and re-register whenever the executable moves.
/// </summary>
public static class WindowsAppScheme
{
    public static void EnsureRegistered()
    {
        var exePath = Environment.ProcessPath;
        if (exePath.IsNullOrEmpty())
            return;

        var command = $"\"{exePath}\" \"%1\"";
        using var schemeKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{MauiSettings.AppScheme}");
        schemeKey.SetValue(null, $"URL:{MauiSettings.AppScheme}");
        schemeKey.SetValue("URL Protocol", "");
        using var commandKey = schemeKey.CreateSubKey(@"shell\open\command");
        if (!Equals(commandKey.GetValue(null), command))
            commandKey.SetValue(null, command);
    }
}
