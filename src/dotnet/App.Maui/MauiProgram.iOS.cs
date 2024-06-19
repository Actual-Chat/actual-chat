using ActualChat.Notification.UI.Blazor;
using ActualChat.Streaming.UI.Blazor.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Firebase.Analytics;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.Core.Platforms.iOS;
using Plugin.Firebase.Crashlytics;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddSingleton(CrossFirebaseCloudMessaging.Current);
        services.AddSingleton(CrossFirebaseAnalytics.Current);
        services.AddSingleton(CrossFirebaseCrashlytics.Current);

        services.AddScoped<IosPushNotifications>(c => new IosPushNotifications(c.UIHub()));
        services.AddTransient<IDeviceTokenRetriever>(c => c.GetRequiredService<IosPushNotifications>());
        services.AddScoped<INotificationsPermission>(c => c.GetRequiredService<IosPushNotifications>());
        services.AddScoped<IRecordingPermissionRequester>(_ => new IosRecordingPermissionRequester());
        services.AddScoped(c => new NativeAppleAuth(c));
        services.AddScoped<TuneUI>(c => new IosTuneUI(c));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
        services.AddScoped<IMediaSaver, IosMediaSaver>();
        services.AddScoped<AddPhotoPermissionHandler>(c => new AddPhotoPermissionHandler(c.UIHub()));
        services.AddTransient<IAppIconBadge>(_ => new IosAppIconBadge());
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    { }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddiOS(ios => ios.FinishedLaunching((app, options) => {
            // Prevents null ref for Windows+iPhone, see:
            // - https://github.com/xamarin/GoogleApisForiOSComponents/issues/577

#if !HOTRESTART
            CrossFirebase.Initialize();
            var isAnalyticsEnabled = Preferences.Default.Get(Constants.Preferences.EnableAnalytics, false);
            CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isAnalyticsEnabled;
            FirebaseCloudMessagingImplementation.Initialize();
#endif
            return false;
        }));
}
