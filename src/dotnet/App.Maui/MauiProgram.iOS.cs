using ActualChat.App.Maui.Activities;
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
        services.AddScoped(c => new IosPttUI(c.AppUIHub()));
        services.AddScoped<IDeviceNotifications>(_ => new IosDeviceNotifications());
        services.AddScoped<IRecordingPermissionRequester>(_ => new AppleRecordingPermissionRequester());
        services.AddScoped(c => new NativeAppleAuth(c));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
        services.AddScoped<IFileSaver>(c => new AppleFileSaver(c.UIHub()));
        services.AddScoped<AddPhotoPermissionHandler>(c => new AddPhotoPermissionHandler(c.UIHub()));
        services.AddTransient<IAppIconBadge>(_ => new AppIconBadge());
        services.AddSingleton(c => new IosUploadKeepAlive(c.LogFor<IosUploadKeepAlive>()));
        services.AddScoped<ILiveActivitiesAvailability>(_ => new IosLiveActivitiesAvailability());
        services.AddScoped<ActivitiesBackend>(c => new IosActivitiesBackend(
            c.AppUIHub(),
            c.GetRequiredService<IosUploadKeepAlive>()));
#if IS_DEV_MAUI
        services.AddScoped<IIncomingCallsBridge>(_ => new IosIncomingCallsBridge());
#endif
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
#if IS_DEV_MAUI
            // Push to Talk is dev-only until it's tested: Entitlements.prod.plist doesn't grant
            // com.apple.developer.push-to-talk, and PTChannelManager.Create reports that as an
            // error every launch. Keyed on the property that picks the entitlements file.
            IosPtt.Initialize();
            // Same dev-only gate as PTT: prod entitlements and App Review are a separate task.
            IosVoipPushes.Instance.Initialize();
#endif
#endif
            return false;
        }));
}
