using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App;
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
        services.AddScoped<IFileSaver, AndroidFileSaver>();

        services.AddTransient<IDeviceTokenRetriever>(c => new AndroidDeviceTokenRetriever(c));
        // Temporarily disabled switch between loudspeaker and earpiece
        // to have single audio channel controlled with volume buttons
        //services.AddScoped<IAudioOutputController>(c => new AndroidAudioOutputController(c));
        services.AddScoped<INotificationsPermission>(c => new AndroidNotificationsPermission(c.AppUIHub()));
        services.AddScoped<IDeviceNotifications>(_ => new AndroidDeviceNotifications());
        services.AddScoped<IIncomingCallsBridge>(_ => new AndroidIncomingCallsBridge());
        services.AddScoped<IRecordingPermissionRequester>(_ => new AndroidRecordingPermissionRequester());
        services.AddScoped<BatteryOptimizationHandler>(c => new AndroidBatteryOptimizationHandler(c.AppUIHub()));
        services.AddSingleton(c => new NativeGoogleAuth(c));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
        services.AddScoped<IMauiLogAccessor>(c => new AndroidLogAccessor(c));
        services.AddScoped<IAudioCapture>(c => new AndroidAudioCapture(c.LogFor<AndroidAudioCapture>()));
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddAndroid(android => {
            AndroidLifecycleLogger.Activate(android);
            android.OnCreate(OnCreate);
            android.OnPostCreate(OnPostCreate);
            android.OnResume(_ => MauiWebView.LogResume());
            android.OnStart(_ => {
                Android.Util.Log.Info(MauiDiagnostics.LogTag, "OnBecameForeground");
                MauiStartupBreadcrumbs.Add("foreground");
                SetBackgroundState(false);
                if (MainPage.Current is { Content: null } mainPage)
                    BeginDispatchToMainThread(() => mainPage.RecreateWebView());
            });
            android.OnStop(_ => {
                Android.Util.Log.Info(MauiDiagnostics.LogTag, "OnBecameBackground");
                MauiStartupBreadcrumbs.Add("background");
                SetBackgroundState(true);
            });
            #if false
            // NOTE(DF): MauiLivenessProbe is switched off for now.
            android.OnPause(_ => MauiLivenessProbe.CancelCheck());
            #endif
            android.OnActivityResult(AndroidActivityResultHandlers.Invoke);
            android.OnBackPressed(activity => {
                _ = OnBackPressed(activity);
                return true; // We handle it in HandleBackPressed
            });
            android.OnDestroy(activity => {
                if (activity is not MainActivity)
                    return;

                AppNavigationQueue.Reset();
                if (MauiPreferences.IsWalkieArmed) {
                    // This service is what holds the microphone grant, and Android only ever hands
                    // that to a service started while the app is in the foreground - so stopping it
                    // here costs every later wake its mic, with no way to earn it back.
                    Log.LogInformation("Keeping AudioWidgetForegroundService: walkie-talkie is armed");
                    return;
                }

                // NOTE(DF): Stop AudioWidgetForegroundService when MainActivity is destroyed,
                // because playback and/or recording do not work anyway in this case.
                Log.LogInformation("Stopping AudioWidgetForegroundService due to MainActivity destroy");
                AndroidAudioWidget.Hide();
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
        var isDataCollectionEnabled = IsDataCollectionEnabled();
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isDataCollectionEnabled;
        MauiDiagnostics.SetIsAnalyticsCollectionEnabled(isDataCollectionEnabled);
        AndroidProcessExitReporter.Start();
    }

    private static void OnPostCreate(Activity activity, Bundle? savedInstanceState)
    {
        NotificationHelper.EnsureDefaultNotificationChannelExist(activity, NotificationHelper.Constants.DefaultChannelId);
        ChatAttentionService.Instance.Init();
    }

    private static bool IsDataCollectionEnabled()
        => MauiPreferences.IsDataCollectionEnabled == true;

    private static void ActivateDataCollectionIfEnabled(Context context)
    {
        if (!IsDataCollectionEnabled())
            return;

        InitFirebaseApp(context);
        CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = true;
        MauiDiagnostics.SetIsAnalyticsCollectionEnabled(true);
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

    private static void SetBackgroundState(bool isBackground)
        => MauiBackgroundState.Set(isBackground);
}
