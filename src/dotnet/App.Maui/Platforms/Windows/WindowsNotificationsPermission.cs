using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

/// <summary>
/// Reads the app's toast setting. Windows never prompts for it - notifications are on until
/// the user turns them off - so a request can only lead to the system settings page.
/// </summary>
public class WindowsNotificationsPermission(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), INotificationsPermission
{
    private static readonly Uri SettingsUri = new("ms-settings:notifications");

    private NotificationUI NotificationUI => field ??= Hub.Services.GetRequiredService<NotificationUI>();

    public Task<bool?> IsGranted(CancellationToken cancellationToken = default)
        => Task.FromResult((bool?)WindowsAppNotifications.IsEnabled);

    public Task Request(CancellationToken cancellationToken = default)
        => ForegroundTask.Run(async () => {
            var isGranted = WindowsAppNotifications.IsEnabled;
            if (!isGranted)
                await Launcher.OpenAsync(SettingsUri).ConfigureAwait(true);
            NotificationUI.SetIsGranted(isGranted);
        }, Log, "Notifications permission request failed", cancellationToken);
}
