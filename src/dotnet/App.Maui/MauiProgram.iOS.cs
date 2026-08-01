using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
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
    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services)
    {
        services.AddSingleton(CrossFirebaseCloudMessaging.Current);
        services.AddSingleton(CrossFirebaseAnalytics.Current);
        services.AddSingleton(CrossFirebaseCrashlytics.Current);

        services.AddScoped<IosPushNotifications>(c => new IosPushNotifications(c.AppUIHub()));
        services.AddTransient<IDeviceTokenRetriever>(c => c.GetRequiredService<IosPushNotifications>());
        services.AddScoped<INotificationsPermission>(c => c.GetRequiredService<IosPushNotifications>());
        services.AddScoped<IDeviceNotifications>(_ => new IosDeviceNotifications());
        services.AddScoped<IRecordingPermissionRequester>(_ => new AppleRecordingPermissionRequester());
        services.AddScoped(c => new NativeAppleAuth(c));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
        services.AddScoped<IFileSaver>(c => new AppleFileSaver(c.UIHub()));
        services.AddScoped<AddPhotoPermissionHandler>(c => new AddPhotoPermissionHandler(c.UIHub()));
        services.AddTransient<IAppIconBadge>(_ => new AppIconBadge());
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddiOS(ios => ios.FinishedLaunching((app, options) => {
            // Prevents null ref for Windows+iPhone, see:
            // - https://github.com/xamarin/GoogleApisForiOSComponents/issues/577

#if !HOTRESTART
            CrossFirebase.Initialize();
            var isDataCollectionEnabled = MauiPreferences.IsDataCollectionEnabled == true;
            CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isDataCollectionEnabled;
            FirebaseCloudMessagingImplementation.Initialize();
#endif
            return false;
        }));
}
