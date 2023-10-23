using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Foundation;
using Microsoft.AspNetCore.Components;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
using UIKit;
using UserNotifications;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

public class PushNotifications : IDeviceTokenRetriever, INotificationsPermission, IDisposable
{
    private NotificationUI? _notificationUI;
    private History? _history;
    private NavigationManager? _nav;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private IFirebaseCloudMessaging Messaging { get; }
    private History History  => _history ??= Services.GetRequiredService<History>();
    private UrlMapper UrlMapper => History.UrlMapper;
    private NavigationManager Nav => _nav ??= Services.GetRequiredService<NavigationManager>();
    private NotificationUI NotificationUI => _notificationUI ??= Services.GetRequiredService<NotificationUI>();
    private UNUserNotificationCenter NotificationCenter => UNUserNotificationCenter.Current;
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public PushNotifications(IServiceProvider services)
    {
        Services = services;
        Messaging = services.GetRequiredService<IFirebaseCloudMessaging>();
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

            // TODO: replace with Messaging.CheckIfValidAsync()?
            if (!UIDevice.CurrentDevice.CheckSystemVersion(10, 0))
                return;

            // For iOS 10 display notification (sent via APNS)
            var options = UNAuthorizationOptions.Alert
                | UNAuthorizationOptions.Badge
                | UNAuthorizationOptions.Sound;
            var (result, error) = await NotificationCenter.RequestAuthorizationAsync(options).ConfigureAwait(true);
            if (result)
                Log.LogInformation("RequestNotificationPermission: granted");
            else
                Log.LogWarning("RequestNotificationPermission: denied, {Error}", error);

            isGranted = await IsGranted(cancellationToken).ConfigureAwait(true);
            NotificationUI.SetIsGranted(isGranted);
        }, Log, "Notifications permission request failed", cancellationToken);

    private static void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        var notificationUrl = null as string;
        if (e.Notification.Data.TryGetValue(Constants.Notification.MessageDataKeys.Link, out var url))
            notificationUrl = url;
        _ = DispatchToBlazor(
            c => c.GetRequiredService<PushNotifications>().HandleNotificationTap(notificationUrl),
            $"PushNotifications.HandleNotificationTap(\"{url}\")");
    }

    private void HandleNotificationTap(string? url)
    {
        if (url.IsNullOrEmpty()) {
            Log.LogWarning("No message link received within notification");
            return;
        }
        Log.LogInformation("HandleNotificationTap: {Url}", url);

        // TODO(AK): resolve notification hang issue when code below is used
        // var autoNavigationTasks = AppServices.GetRequiredService<AutoNavigationTasks>();
        // autoNavigationTasks.Add(ForegroundTask.Run(async () => {
        //     var scopedServices = await ScopedServicesTask.ConfigureAwait(false);
        //     var notificationUI = scopedServices.GetRequiredService<NotificationUI>();
        //     await notificationUI.HandleNotificationNavigation(url).ConfigureAwait(false);
        // }, Log, "Failed to handle notification tap"));

        // Dirty hack as we have BaseUrl - https://actual.chat/ but local url should be app://0.0.0.0/
        var localUrl = url
            .Replace(UrlMapper.BaseUrl, "")
            .Replace("app://0.0.0.0/", "");

        Log.LogInformation("OnNotificationTapped: navigating to {LocalUrl}", localUrl);

        Nav.NavigateTo(localUrl, new NavigationOptions {
            ForceLoad = false,
            ReplaceHistoryEntry = false,
        });
    }
}
