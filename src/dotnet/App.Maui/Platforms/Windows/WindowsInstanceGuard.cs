using System.Security.Cryptography;
using System.Text;
using ActualChat.App.Maui.WinUI;
using ActualChat.Concurrency;
using ActualLab.IO;

namespace ActualChat.App.Maui;

/// <summary>
/// Keeps one instance per data folder, whatever its package identity.
/// </summary>
public static class WindowsInstanceGuard
{
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(3);
    private static SingleInstanceGuard? _guard;
    private static ILogger Log => field ??= StaticLog.For(typeof(WindowsInstanceGuard));

    public static Task<bool> TakeOver()
    {
        var guard = new SingleInstanceGuard(GetKey(), GetPidFilePath());
        guard.ExitRequested += OnExitRequested;
        _guard = guard;
        return guard.TakeOver();
    }

    public static void LogStartup()
        // The guard runs before logging is configured, so this is called once it is
        => Log.LogInformation("Instance guard: {Outcome}, key {Key}, data folder {Directory}, error: {Error}",
            _guard?.Outcome, _guard?.Name, FileSystem.AppDataDirectory, _guard?.Error?.Message);

    // Private methods

    private static void OnExitRequested()
    {
        Log.LogWarning("A newer instance asked this one to exit");
        MauiMainThread.BeginDispatchToMainThread(() => App.Current.Quit(), false);
        // A quit that hangs must not leave two instances: the data folder is released only on exit
        Thread.Sleep(GracefulExitTimeout);
        Log.LogWarning("The app didn't quit in {Timeout}, exiting", GracefulExitTimeout.ToShortString());
        Environment.Exit(0);
    }

    private static string GetKey()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(FileSystem.AppDataDirectory.ToLower()));
        return Convert.ToHexString(hash)[..12];
    }

    private static FilePath GetPidFilePath()
        => (FilePath)FileSystem.AppDataDirectory & "instance.pid";
}
