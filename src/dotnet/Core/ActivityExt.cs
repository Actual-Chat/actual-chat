namespace ActualChat;

public static class ActivityExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordException(this Activity activity, Exception? ex)
    {
        if (ex == null)
            return;

        var activityTagsCollection = new ActivityTagsCollection {
            { "exception.type", ex.GetType().FullName},
            { "exception.stacktrace", ToInvariantString(ex)},
        };
        if (!string.IsNullOrWhiteSpace(ex.Message)) {
            activityTagsCollection.Add("exception.message", ex.Message);
        }
        activity.AddEvent(new ActivityEvent("exception", default, activityTagsCollection));

        static string ToInvariantString(Exception exception)
        {
            var originalUICulture = Thread.CurrentThread.CurrentUICulture;
            try {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
                return exception.ToString();
            }
            finally {
                Thread.CurrentThread.CurrentUICulture = originalUICulture;
            }
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Activity AddSentrySimulatedEvent(this Activity activity, ActivityEvent e)
    {
        // Sentry does not export events from an Activity.
        // To get around this limitation we create inner spans with min duration.
        var currentActivity = Activity.Current;
        try {
            Activity.Current = activity;
            using var innerSpan = activity.Source.StartActivity(e.Name, ActivityKind.Internal, (ActivityContext)default, e.Tags);
            innerSpan?.SetEndTime(innerSpan.StartTimeUtc.AddTicks(1));
        }
        finally {
            Activity.Current = currentActivity;
        }
        return activity;
    }
}
