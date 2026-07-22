using ActualChat.UI.Blazor.Services;
using Android.App;
using Android.Content;

namespace ActualChat.App.Maui;

/// <summary>
/// Reports abnormal exits (ANRs, crashes) of the previous app process via
/// <see cref="ApplicationExitInfo"/>, attaching the previous session's
/// <see cref="MauiStartupBreadcrumbs"/> — the only way to attribute background-start
/// ANRs, which die before any crash reporter persists its data.
/// </summary>
public static class AndroidProcessExitReporter
{
    private const string LastReportedExitAtKey = "last_reported_process_exit_at";
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
            MauiStartupBreadcrumbs.Add("app rendered");
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

        var exits = activityManager.GetHistoricalProcessExitReasons(context.PackageName, 0, 5);
        var lastReportedAt = MauiPreferences.Get<long>(LastReportedExitAtKey);
        var maxTimestamp = lastReportedAt;
        var isNewestExit = true;
        foreach (var exit in exits) {
            var timestamp = exit.Timestamp;
            var isAbnormal = (ApplicationExitInfoReason)exit.Reason
                is ApplicationExitInfoReason.Anr
                or ApplicationExitInfoReason.Crash
                or ApplicationExitInfoReason.CrashNative;
            if (timestamp <= lastReportedAt || !isAbnormal) {
                isNewestExit = false;
                continue;
            }

            maxTimestamp = Math.Max(maxTimestamp, timestamp);
            // Breadcrumbs cover only the most recent session, so attach them
            // only when the newest recorded exit is the one being reported.
            var breadcrumbs = isNewestExit ? MauiStartupBreadcrumbs.ReadPrevious() : "";
            isNewestExit = false;
            Log.LogWarning(
                "Previous process exit at {At}: {Exit}\nLast session breadcrumbs:\n{Breadcrumbs}",
                DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
                exit.ToString(),
                breadcrumbs);
        }
        if (maxTimestamp > lastReportedAt)
            MauiPreferences.Set(LastReportedExitAtKey, maxTimestamp);
    }
}
