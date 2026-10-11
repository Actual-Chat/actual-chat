using System.Diagnostics;
using ActualLab.IO;

namespace ActualChat.App.Maui;

/// <summary>
/// Registers the running build with the desktop: a launcher entry carrying the app's name and
/// icon, which also claims the voxt:// scheme - the default browser returns from sign-in
/// through it, see <see cref="LinuxActivation"/>.
/// </summary>
internal static class LinuxDesktopEntry
{
    private static readonly string SchemeMimeType = $"x-scheme-handler/{MauiSettings.AppScheme}";
    private static ILogger Log => field ??= StaticLog.For(typeof(LinuxDesktopEntry));

    public static void Ensure(string applicationId)
    {
        try {
            // The window's app id is the application id, and the desktop matches the two by file name
            var fileName = $"{applicationId}.desktop";
            var dataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = new FilePath(dataDir) & "applications" & fileName;
            var content = GetContent(applicationId);
            if (File.Exists(path) && File.ReadAllText(path) == content)
                return;

            Directory.CreateDirectory(path.DirectoryPath);
            File.WriteAllText(path, content);
            using var process = Process.Start("xdg-mime", ["default", fileName, SchemeMimeType]);
            Log.LogInformation("Desktop entry written: {Path}", path);
        }
        catch (Exception e) {
            // Without it the app still runs; only the launcher entry and the sign-in return are missing
            Log.LogError(e, "Failed to write the desktop entry");
        }
    }

    // Private methods

    private static string GetContent(string applicationId)
    {
        var baseDir = new FilePath(AppContext.BaseDirectory);
        var iconPath = baseDir & "hicolor" & "scalable" & "apps" & "appicon.svg";
        return $"""
            [Desktop Entry]
            Type=Application
            Name={App.Current.Windows[0].Title}
            Exec="{Environment.ProcessPath}" %u
            Icon={iconPath}
            Terminal=false
            StartupWMClass={applicationId}
            Categories=Network;InstantMessaging;
            MimeType={SchemeMimeType};

            """;
    }
}
