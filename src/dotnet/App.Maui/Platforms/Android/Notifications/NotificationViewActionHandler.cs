using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Services;
using Android.Content;

namespace ActualChat.App.Maui;

public class NotificationViewActionHandler
{
    private ILogger Log { get; } = StaticLog.For(typeof(NotificationViewActionHandler));

    public void HandleIntent(Intent intent)
        => TryHandleViewAction(intent);

    private void TryHandleViewAction(Intent intent)
    {
        if (!OrdinalEquals(NotificationHelper.NotificationViewAction, intent.Action))
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
