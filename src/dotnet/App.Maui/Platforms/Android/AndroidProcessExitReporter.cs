using ActualChat.UI.Blazor.Services;
using Android.App;
using Android.Content;

namespace ActualChat.App.Maui;

/// <summary>
/// Reports how earlier app processes ended via <see cref="ApplicationExitInfo"/>: ANRs and crashes as
/// warnings with the previous session's <see cref="MauiStartupBreadcrumbs"/> — the only way to attribute
/// background-start ANRs, which die before any crash reporter persists its data — and other exits as info.
/// </summary>
public static class AndroidProcessExitReporter
{
    private const string LastReportedExitAtKey = "last_reported_process_exit_at";
    private const int MaxExitCount = 16;
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(10);

    private static int _isStarted;

    private static ILogger Log => field ??= StaticLog.For(typeof(AndroidProcessExitReporter));

    public static void Start()
    {
        if (!MauiSettings.Diagnostics.EnableStartupBreadcrumbs)
            return;
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return;
        if (Interlocked.Exchange(ref _isStarted, 1) != 0)
            return;

        _ = BackgroundTask.Run(async () => {
            await LoadingUI.WhenAppRendered.ConfigureAwait(false);
            MauiStartupBreadcrumbs.Add("App rendered");
            await Task.Delay(StartDelay).ConfigureAwait(false);
            Report();
        }, Log, "Previous process exit reporting failed");
    }

    // Private methods

    private static void Report()
    {
        var context = Android.App.Application.Context;
        if (context.GetSystemService(Context.ActivityService) is not ActivityManager activityManager)
            return;

        var exits = activityManager.GetHistoricalProcessExitReasons(context.PackageName, 0, MaxExitCount);
        var lastReportedAt = MauiPreferences.Get<long>(LastReportedExitAtKey);
        var maxTimestamp = lastReportedAt;
        var isNewestOwnExit = true;
        foreach (var exit in exits) {
            var timestamp = exit.Timestamp;
            // WebView's sandboxed renderer is listed under this package too and goes down with the app,
            // so only its own crashes say anything; the breadcrumbs belong to the app's process
            var isOwnProcess = exit.ProcessName == context.PackageName;
            var isNewest = isOwnProcess && isNewestOwnExit;
            if (isOwnProcess)
                isNewestOwnExit = false;
            if (timestamp <= lastReportedAt)
                continue;

            maxTimestamp = Math.Max(maxTimestamp, timestamp);
            var at = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
            var isAbnormal = (ApplicationExitInfoReason)exit.Reason
                is ApplicationExitInfoReason.Anr
                or ApplicationExitInfoReason.Crash
                or ApplicationExitInfoReason.CrashNative;
            if (!isAbnormal) {
                // Low memory, a signal from an OEM battery saver, excessive resource use: no crash
                // report is filed for these, yet each explains an app or a call that just stopped
                if (isOwnProcess)
                    Log.LogInformation("Previous process exit at {At}: {Exit}", at, exit.ToString());
                continue;
            }

            // Breadcrumbs cover only the most recent session, so attach them
            // only when the newest recorded exit is the one being reported.
            var breadcrumbs = isNewest ? MauiStartupBreadcrumbs.ReadPrevious() : "";
            Log.LogWarning(
                "Previous process exit at {At}: {Exit}\nLast session breadcrumbs:\n{Breadcrumbs}",
                at,
                exit.ToString(),
                breadcrumbs);
        }
        if (maxTimestamp > lastReportedAt)
            MauiPreferences.Set(LastReportedExitAtKey, maxTimestamp);
    }
}
