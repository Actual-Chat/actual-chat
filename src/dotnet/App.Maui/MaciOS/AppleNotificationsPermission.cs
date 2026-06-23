using ActualChat.UI;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using UserNotifications;

namespace ActualChat.App.Maui;

public class AppleNotificationsPermission(AppUIHub hub)
    : UIServiceBase<AppUIHub>(hub), INotificationsPermission
{
    private NotificationUI NotificationUI => Hub.NotificationUI;
    private SystemSettingsUI SystemSettingsUI => field ??= Hub.Services.GetRequiredService<SystemSettingsUI>();
    private static UNUserNotificationCenter NotificationCenter => UNUserNotificationCenter.Current;

    public async Task<bool?> IsGranted(CancellationToken cancellationToken = default)
    {
        var settings = await NotificationCenter.GetNotificationSettingsAsync().ConfigureAwait(false);
        return settings.AuthorizationStatus switch {
            UNAuthorizationStatus.NotDetermined => null,
            UNAuthorizationStatus.Authorized => true,
            UNAuthorizationStatus.Provisional => true,
            UNAuthorizationStatus.Ephemeral => true,
            _ => false,
        };
    }

    public Task Request(CancellationToken cancellationToken = default)
        => ForegroundTask.Run(async () => {
            var isGranted = await IsGranted(cancellationToken).ConfigureAwait(true);
            if (isGranted == true) {
                NotificationUI.SetIsGranted(isGranted);
                return;
            }

            var options = UNAuthorizationOptions.Alert
                | UNAuthorizationOptions.Badge
                | UNAuthorizationOptions.Sound;
            var (result, error) = await NotificationCenter.RequestAuthorizationAsync(options).ConfigureAwait(true);
            if (result)
                Log.LogInformation("NotificationCenter.RequestAuthorizationAsync: granted");
            else
                Log.LogWarning("NotificationCenter.RequestAuthorizationAsync: denied, {Error}", error);

            isGranted = await IsGranted(cancellationToken).ConfigureAwait(true);
            if (isGranted == false)
                await SystemSettingsUI.Open().ConfigureAwait(true);
            NotificationUI.SetIsGranted(isGranted);
        }, Log, "Notifications permission request failed", cancellationToken);
}
