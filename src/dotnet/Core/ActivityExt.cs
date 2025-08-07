namespace ActualChat;

public static class ActivityExt
{
    private static ILogger? _log;

    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(ActivityExt));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Activity AddSentrySimulatedEvent(this Activity activity, ActivityEvent e)
    {
        // Sentry does not export events from an Activity.
        // To get around this limitation we create inner spans with min duration.
        if (!CanSetCurrent(activity))
            return activity;

        var currentActivity = Activity.Current;
        try {
            Activity.Current = activity;
            using var innerSpan = activity.Source.StartActivity(e.Name, ActivityKind.Internal, (ActivityContext)default, e.Tags);
            innerSpan?.SetEndTime(innerSpan.StartTimeUtc.AddTicks(1));
        }
        finally {
            if (CanSetCurrent(currentActivity))
                Activity.Current = currentActivity;
            else {
                Log.LogWarning("Activity '{ActivityDescription}' can not be set back as Activity.Current. Will set Activity.Current to <null>.", GetDescription(activity));
                Activity.Current = null;
            }
        }
        return activity;
    }

    private static string GetDescription(Activity activity)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append("Id: ");
        sb.Append(activity.Id ?? "<null>");
        sb.Append(", DisplayName: ");
        sb.Append(activity.DisplayName);
        return sb.ToStringAndRelease();
    }

    // This check is taken from Activity.Current setter.
    private static bool CanSetCurrent(Activity? activity)
        => activity == null || (activity.Id != null && !activity.IsStopped);
}
