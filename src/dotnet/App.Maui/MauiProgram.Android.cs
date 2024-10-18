using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.OS;
using Firebase;
using Firebase.Messaging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Firebase.Analytics;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static bool _firebaseAppInitialized;

    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services)
    {
        if (MauiSettings.IsDevApp)
            // Enable delivery data export per instance.
            // https://firebase.google.com/docs/cloud-messaging/understand-delivery?platform=android#enable-message-delivery-data-export
            FirebaseMessaging.Instance.SetDeliveryMetricsExportToBigQuery(true);

        services.AddSingleton<Java.Util.Concurrent.IExecutorService>(_ =>
            Java.Util.Concurrent.Executors.NewWorkStealingPool()!);

        services.AddSingleton<IHistoryExitHandler>(_ => new AndroidHistoryExitHandler());
        services.AddSingleton<AndroidContentDownloader>();
        services.AddAlias<IIncomingShareFileDownloader, AndroidContentDownloader>();
        services.AddScoped<IMediaSaver, AndroidMediaSaver>();

        services.AddTransient<IDeviceTokenRetriever>(c => new AndroidDeviceTokenRetriever(c));
        // Temporarily disabled switch between loudspeaker and earpiece
        // to have single audio channel controlled with volume buttons
        //services.AddScoped<IAudioOutputController>(c => new AndroidAudioOutputController(c));
        services.AddScoped<INotificationsPermission>(c => new AndroidNotificationsPermission(c.UIHub()));
        services.AddScoped<IRecordingPermissionRequester>(_ => new AndroidRecordingPermissionRequester());
        services.AddSingleton(c => new NativeGoogleAuth(c));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
        services.AddScoped<IMauiLogAccessor>(c => new AndroidLogAccessor(c.LogFor<AndroidLogAccessor>()));
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddAndroid(android => {
            AndroidLifecycleLogger.Activate(android);
            android.OnCreate(OnCreate);
            android.OnPostCreate(OnPostCreate);
            android.OnResume(_ => MauiWebView.LogResume());
            android.OnPause(_ => MauiLivenessProbe.CancelCheck());
            android.OnActivityResult(AndroidActivityResultHandlers.Invoke);
            android.OnBackPressed(activity => {
                _ = OnBackPressed(activity);
                return true; // We handle it in HandleBackPressed
            });
            IntentHandler.Activate(android);
        });

    private static async Task OnBackPressed(Activity activity)
    {
        var couldStepBack = await DispatchToBlazor(c => c.GetRequiredService<History>().TryStepBack()).ConfigureAwait(true);
        if (!couldStepBack)
            activity.MoveTaskToBack(true);
    }

    private static void OnCreate(Activity activity, Bundle? savedInstanceState)
    {
        InitFirebaseApp(activity);
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = IsDataCollectionEnabled();
    }

    private static void OnPostCreate(Activity activity, Bundle? savedInstanceState)
    {
        NotificationHelper.EnsureDefaultNotificationChannelExist(activity, NotificationHelper.Constants.DefaultChannelId);
        ChatAttentionService.Instance.Init();
    }

    private static bool IsDataCollectionEnabled()
        => Preferences.Default.Get(Constants.Preferences.EnableDataCollectionKey, false);

    private static void ActivateDataCollectionIfEnabled(Context context)
    {
        if (!IsDataCollectionEnabled())
            return;

        InitFirebaseApp(context);
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = true;
    }

    private static bool InitFirebaseApp(Context context)
    {
        if (_firebaseAppInitialized)
            return true;

        _firebaseAppInitialized = true;
        FirebaseApp.InitializeApp(context);
        FirebaseAnalyticsImplementation.Initialize(context);
        return false;
    }
}
