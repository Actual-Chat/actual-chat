namespace ActualChat.App.Maui.Activities;

internal static partial class IosLiveActivities
{
    [LibraryImport("__Internal", EntryPoint = "voxt_activity_start_or_update",
        StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StartOrUpdate(int kind, string title, string subtitle, double progress);
    [LibraryImport("__Internal", EntryPoint = "voxt_activity_end")]
    public static partial void End();
    [LibraryImport("__Internal", EntryPoint = "voxt_activities_enabled")]
    public static partial int AreEnabled();
}
