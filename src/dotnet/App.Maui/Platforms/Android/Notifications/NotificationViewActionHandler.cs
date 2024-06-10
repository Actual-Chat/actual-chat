using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.OS;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public class NotificationViewActionHandler
{
    private ILogger Log { get; set; } = NullLogger.Instance;

    public void OnPostCreate(Activity activity, Bundle? savedInstanceState)
    {
        Log = AppServices.LogFor(GetType());
        TryHandleViewAction(activity.Intent);
        NotificationHelper.EnsureDefaultNotificationChannelExist(activity, NotificationHelper.Constants.DefaultChannelId);
    }

    public void OnNewIntent(Activity activity, Intent? intent)
        => TryHandleViewAction(intent);

    private void TryHandleViewAction(Intent? intent)
    {
        if (intent is null || !OrdinalEquals(NotificationHelper.NotificationViewAction, intent.Action))
            return;

        if (intent.Data is null)
            return;

        var link = intent.Data.ToString()!;
        Log.LogInformation("-> TryHandleNotificationTap, Url: '{Url}'", link);
        var autoNavigationTasks = AppServices.GetRequiredService<AutoNavigationTasks>();
        autoNavigationTasks.Add(DispatchToBlazor(
            c => c.GetRequiredService<NotificationUI>().HandleNotificationNavigation(link),
            $"NotificationUI.HandleNotificationNavigation(\"{link}\")"));
    }
}
