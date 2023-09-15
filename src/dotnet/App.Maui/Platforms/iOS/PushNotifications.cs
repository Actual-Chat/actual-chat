using ActualChat.Notification;
using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services.Internal;
using Foundation;
using Microsoft.AspNetCore.Components;
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
    private History? _history;
    private NavigationManager? _nav;
    private HistoryItemIdFormatter? _itemIdFormatter;

    public IServiceProvider Services { get; }
    private IFirebaseCloudMessaging Messaging { get; }
    private History History  => _history ??= Services.GetRequiredService<History>();
    private UrlMapper UrlMapper => History.UrlMapper;
    private NavigationManager Nav => _nav ??= Services.GetRequiredService<NavigationManager>();
    private HistoryItemIdFormatter ItemIdFormatter => _itemIdFormatter ??= Services.GetRequiredService<HistoryItemIdFormatter>();
    private LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    private NotificationUI NotificationUI => _notificationUI ??= Services.GetRequiredService<NotificationUI>();
    private UNUserNotificationCenter NotificationCenter => UNUserNotificationCenter.Current;
    private ILogger Log { get; }

    public PushNotifications(IServiceProvider services)
    {
        Services = services;
        Messaging = services.GetRequiredService<IFirebaseCloudMessaging>();
        Log = services.LogFor(GetType());

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

    public async Task<PermissionState> GetPermissionState(CancellationToken cancellationToken)
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

    public async Task RequestNotificationPermission(CancellationToken cancellationToken)
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

        var state = await GetPermissionState(cancellationToken).ConfigureAwait(false);
        NotificationUI.SetPermissionState(state);
    }

    private void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        if (!e.Notification.Data.TryGetValue(NotificationConstants.MessageDataKeys.Link, out var url)) {
            Log.LogWarning("No message link received within notification");
            return;
        }
        Log.LogInformation("OnNotificationTapped: {Url}", url);

        if (url.IsNullOrEmpty())
            return;

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

        Nav.NavigateTo(localUrl, new NavigationOptions() {
            ForceLoad = false,
            ReplaceHistoryEntry = false,
            HistoryEntryState = ItemIdFormatter.Format(100500),
        });
    }
}
