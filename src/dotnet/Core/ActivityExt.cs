namespace ActualChat;

public static class ActivityExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordException(this Activity activity, Exception ex)
    {
        if (ex != null) {
            ActivityTagsCollection activityTagsCollection = new ActivityTagsCollection {
                    { "exception.type", ex.GetType().FullName},
                    { "exception.stacktrace", ToInvariantString(ex)},
                };
            if (!string.IsNullOrWhiteSpace(ex.Message)) {
                activityTagsCollection.Add("exception.message", ex.Message);
            }
            activity?.AddEvent(new ActivityEvent("exception", default, activityTagsCollection));

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
    }
}
