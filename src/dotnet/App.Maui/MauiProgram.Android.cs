using ActualChat.App.Maui.Activities;
using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.OS;
using Firebase.Messaging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Firebase.Analytics;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private const int MaxFirebaseInitDelays = 8;
    private static readonly TimeSpan FirebaseInitDelay = TimeSpan.FromSeconds(15);
    private static int _isFirebaseInitStarted;
    private static bool _isFirebaseAnalyticsReady;

    // Plugin.Firebase's LogEvent and IsAnalyticsCollectionEnabled setter NRE before Initialize
    public static bool IsFirebaseAnalyticsReady => Volatile.Read(ref _isFirebaseAnalyticsReady);

    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services)
    {
        if (MauiSettings.IsDevApp)
            // Enable delivery data export per instance.
            // https://firebase.google.com/docs/cloud-messaging/understand-delivery?platform=android#enable-message-delivery-data-export
            _ = BackgroundTask.Run(async () => {
                await MauiFirebase.WhenReady.ConfigureAwait(false);
                FirebaseMessaging.Instance.SetDeliveryMetricsExportToBigQuery(true);
            }, Log, "SetDeliveryMetricsExportToBigQuery failed");

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
        services.AddScoped<IFullScreenCallsAvailability>(c =>
            new AndroidFullScreenCallsAvailability(c.LogFor<AndroidFullScreenCallsAvailability>()));
        services.AddScoped<IRecordingPermissionRequester>(_ => new AndroidRecordingPermissionRequester());
        services.AddScoped<BatteryOptimizationHandler>(c => new AndroidBatteryOptimizationHandler(c.AppUIHub()));
        services.AddSingleton(c => new NativeGoogleAuth(c));
        services.AddScoped<IPasskeyClient>(c => new AndroidPasskeyClient(c));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
        services.AddScoped<IMauiLogAccessor>(c => new AndroidLogAccessor(c));
        services.AddScoped<IAudioCapture>(c => new AndroidAudioCapture(c));
        services.AddFusion().AddService<ICarConnection, AndroidCarConnection>(ServiceLifetime.Scoped);
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddAndroid(android => {
            AndroidLifecycleLogger.Activate(android);
            android.OnCreate(OnCreate);
            android.OnPostCreate(OnPostCreate);
            android.OnResume(_ => OnResume());
            // These fire for every activity in the process, and only the live MainActivity hosts
            // the WebView.
            android.OnStart(activity => {
                if (!MainActivity.IsCurrent(activity))
                    return;

                Android.Util.Log.Info(MauiDiagnostics.LogTag, "OnBecameForeground");
                MauiStartupBreadcrumbs.Add("Became foreground");
                SetBackgroundState(false);
                if (MainPage.Current is { Content: null, IsWebViewAttachPending: false } mainPage)
                    BeginDispatchToMainThread(() => mainPage.RecreateWebView());
            });
            android.OnStop(activity => {
                if (!MainActivity.IsCurrent(activity))
                    return;

                Android.Util.Log.Info(MauiDiagnostics.LogTag, "OnBecameBackground");
                MauiStartupBreadcrumbs.Add("Became background");
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
                if (MauiPreferences.IsPttArmed) {
                    // This service is what holds the microphone grant, and Android only ever hands
                    // that to a service started while the app is in the foreground - so stopping it
                    // here costs every later wake its mic, with no way to earn it back.
                    Log.LogInformation("Keeping AndroidActivitiesForegroundService: PTT is armed");
                    return;
                }

                // NOTE(DF): Stop AndroidActivitiesForegroundService when MainActivity is destroyed,
                // because playback and/or recording do not work anyway in this case.
                Log.LogInformation("Stopping AndroidActivitiesForegroundService due to MainActivity destroy");
                AndroidActivitiesBackend.Hide();
            });
            IntentHandler.Activate(android);
        });

    private static void OnResume()
    {
        MauiWebView.LogResume();
        // The gearhead broadcast is the only other signal, and a car can be plugged in or
        // unplugged while we're stopped - without this the state stays cached forever.
        _ = DispatchToBlazor(
            c => c.GetService<ICarConnection>()?.InvalidateProjectionState(),
            nameof(ICarConnection.InvalidateProjectionState));
    }

    private static async Task OnBackPressed(Activity activity)
    {
        var couldStepBack = await DispatchToBlazor(c => c.GetRequiredService<History>().TryStepBack())
            .ConfigureAwait(true);
        if (!couldStepBack)
            activity.MoveTaskToBack(true);
    }

    private static void OnCreate(Activity activity, Bundle? savedInstanceState)
    {
        StartFirebaseAnalytics(activity);
        AndroidProcessExitReporter.Start();
    }

    private static void OnPostCreate(Activity activity, Bundle? savedInstanceState)
    {
        NotificationHelper.EnsureDefaultNotificationChannelExist(
            activity,
            NotificationHelper.Constants.DefaultChannelId);
        NotificationHelper.EnsureActivityChannelsExist(activity);
        ChatAttentionService.Instance.Init();
    }

    private static void StartFirebaseAnalytics(Context context)
    {
        // GMS class loading plus binder calls, which used to run on the main thread inside both
        // Activity.onCreate and Application.onCreate (so on FCM wakes too) - the two windows the
        // low-tier phones ANR in. A worker, only once an Activity exists, and not while the OS is
        // trimming us: the trim that precedes those ANRs is a request for less work, and analytics
        // can start a couple of minutes late.
        if (Interlocked.Exchange(ref _isFirebaseInitStarted, 1) != 0)
            return;

        _ = BackgroundTask.Run(async () => {
            await MauiFirebase.WhenReady.ConfigureAwait(false);
            for (var i = 0; i < MaxFirebaseInitDelays && AndroidUtils.IsUnderMemoryPressure(); i++) {
                Log.LogInformation("Firebase Analytics init deferred: memory pressure");
                await Task.Delay(FirebaseInitDelay).ConfigureAwait(false);
            }
            FirebaseAnalyticsImplementation.Initialize(context);
            var isDataCollectionEnabled = MauiPreferences.IsDataCollectionEnabled == true;
            CrossFirebaseAnalytics.Current.IsAnalyticsCollectionEnabled = isDataCollectionEnabled;
            MauiDiagnostics.SetIsAnalyticsCollectionEnabled(isDataCollectionEnabled);
            Volatile.Write(ref _isFirebaseAnalyticsReady, true);
        }, Log, "Firebase Analytics init failed");
    }

    private static void SetBackgroundState(bool isBackground)
        => MauiBackgroundState.Set(isBackground);
}
