using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;
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
            var isDataCollectionEnabled = Preferences.Default.Get(Constants.Preferences.EnableDataCollectionKey, false);
            CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isDataCollectionEnabled;
            FirebaseCloudMessagingImplementation.Initialize();
#endif
            return false;
        }));

    // TODO: Remove after migrating to .NET 9
    private static void FixIosBaseAddress()
    {
        var handlerType = typeof(BlazorWebViewHandler);
        var field = handlerType.GetField("AppOriginUri", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw StandardError.Internal("No AppOriginUri field.");
        field.SetValue(null, new Uri($"app://{MauiSettings.LocalHost}/"));
    }
}
