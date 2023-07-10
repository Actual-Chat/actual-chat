using ActualChat.Notification;
using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Foundation;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
using UIKit;
using UserNotifications;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

public class PushNotifications : IDeviceTokenRetriever, IHasServices, INotificationPermissions, IDisposable
{
    private NotificationUI? _notificationUI;
    private LoadingUI? _loadingUI;

    public IServiceProvider Services { get; }
    private IFirebaseCloudMessaging Messaging { get; }
    private LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    private NotificationUI NotificationUI => _notificationUI ??= Services.GetRequiredService<NotificationUI>();
    private UNUserNotificationCenter NotificationCenter => UNUserNotificationCenter.Current;
    private ILogger Log { get; }

    public PushNotifications(IServiceProvider services)
    {
        Services = services;
        Messaging = services.GetRequiredService<IFirebaseCloudMessaging>();
        Log = services.LogFor<PushNotifications>();

        Messaging.NotificationTapped += OnNotificationTapped;
    }

    public static void Initialize(UIApplication app, NSDictionary options)
    {
        // Prevents null ref for Windows+iPhone, see:
        // - https://github.com/xamarin/GoogleApisForiOSComponents/issues/577
#if !HOTRESTART
        Firebase.Core.App.Configure();
        FirebaseCloudMessagingImplementation.Initialize();
#endif
    }

    public void Dispose()
        => Messaging.NotificationTapped -= OnNotificationTapped;

    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => Messaging.GetTokenAsync();

    public async Task<PermissionState> GetNotificationPermissionState(CancellationToken cancellationToken)
    {
        var settings = await NotificationCenter.GetNotificationSettingsAsync().ConfigureAwait(false);
        switch (settings.AuthorizationStatus) {
        case UNAuthorizationStatus.NotDetermined:
            return PermissionState.Prompt;
        case UNAuthorizationStatus.Denied:
            return PermissionState.Denied;
        case UNAuthorizationStatus.Authorized:
        case UNAuthorizationStatus.Provisional:
        case UNAuthorizationStatus.Ephemeral:
            return PermissionState.Granted;
        default:
            throw new ArgumentOutOfRangeException(nameof(settings.AuthorizationStatus), settings.AuthorizationStatus, null);
        }
    }

    public async Task RequestNotificationPermissions(CancellationToken cancellationToken)
    {
        // TODO: replace with Messaging.CheckIfValidAsync() when they await result
        if (!UIDevice.CurrentDevice.CheckSystemVersion(10, 0))
            return;

        // For iOS 10 display notification (sent via APNS)
        var options = UNAuthorizationOptions.Alert
            | UNAuthorizationOptions.Badge
            | UNAuthorizationOptions.Sound;
        var (granted, error) = await NotificationCenter.RequestAuthorizationAsync(options)
            .ConfigureAwait(false);
        if (granted)
            Log.LogInformation("RequestNotificationPermissions: granted", granted);
        else
            Log.LogWarning("RequestNotificationPermissions: denied, {Error}", error);

        var newState = await GetNotificationPermissionState(cancellationToken).ConfigureAwait(false);
        NotificationUI.UpdateNotificationStatus(newState);
    }

    private void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        if (!e.Notification.Data.TryGetValue(NotificationConstants.MessageDataKeys.Link, out var url)) {
            Log.LogWarning("No message link received within notification");
            return;
        }

        try {
            NotificationUI.HandleNotificationNavigation(url);
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to handle notification tap");
        }
    }
}
