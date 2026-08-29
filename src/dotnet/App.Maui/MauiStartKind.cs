namespace ActualChat.App.Maui;

/// <summary>
/// Whether this process was started to show UI, or to service a background wake
/// (an FCM broadcast, a PTT wake) that may never reach an <c>Activity</c>.
/// </summary>
public enum MauiStartKind { Interactive = 0, Headless }

public static class MauiStart
{
    private static int _kind = -1;
    public static MauiStartKind Kind {
        get {
            var kind = Volatile.Read(ref _kind);
            if (kind < 0) {
                kind = (int)Detect();
                Interlocked.CompareExchange(ref _kind, kind, -1);
                kind = Volatile.Read(ref _kind);
            }
            return (MauiStartKind)kind;
        }
    }
    public static bool IsHeadless => Kind == MauiStartKind.Headless;
    public static void MarkInteractive()
        // An Activity means UI is coming: a headless process that gets one - a notification
        // tap - must run everything its start skipped, and skip nothing from here on.
        => Volatile.Write(ref _kind, (int)MauiStartKind.Interactive);

    // Private methods

    private static MauiStartKind Detect()
    {
#if ANDROID
        // An FCM wake reads as a receiver/cached importance, a user launch as Foreground; unknown
        // takes the interactive path, so a broken check costs startup time, not correctness.
        var importance = AndroidUtils.GetProcessInfo()?.Importance;
        return importance is not null && importance != Android.App.Importance.Foreground
            ? MauiStartKind.Headless
            : MauiStartKind.Interactive;
#else
        return MauiStartKind.Interactive;
#endif
    }
}
