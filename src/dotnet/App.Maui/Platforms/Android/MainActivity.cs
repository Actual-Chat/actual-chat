using ActualChat.App.Maui.Services;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Android.Views;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using AView = Android.Views.View;
using JObject = Java.Lang.Object;

namespace ActualChat.App.Maui;

[Activity(
    Theme = "@style/SplashTheme",
    MainLauncher = true,
    // When user tap on a notification which was created by FCM when app was in background mode,
    // It causes creating a new instance of MainActivity. Apparently this happens because Intent has NewTask flag.
    // Creating a new instance of MainActivity causes creating a new instance of MauiBlazorApp
    // even without disposing an existing one.
    // Setting LaunchMode to SingleTask or SingleInstance prevents this behavior.
    // Existing instance of MainActivity is used and Intent is passed to OnNewIntent method.
    // MauiBlazorApp instance is kept.
    // See:
    // - https://stackoverflow.com/questions/25773928/setting-launchmode-singletask-vs-setting-activity-launchmode-singletop
    LaunchMode = LaunchMode.SingleTask,
    DocumentLaunchMode = DocumentLaunchMode.None,
    HardwareAccelerated = true,
    ConfigurationChanges =
        ConfigChanges.UiMode |
        ConfigChanges.Density | ConfigChanges.FontScale | ConfigChanges.FontWeightAdjustment |
        ConfigChanges.ScreenSize |  ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout |
        ConfigChanges.Orientation | ConfigChanges.LayoutDirection |
        ConfigChanges.Touchscreen | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden
    )]
[IntentFilter(
    new [] { Intent.ActionView },
    DataSchemes = new [] { "http", "https" },
    DataHost = MauiSettings.Host,
    DataPaths = new [] { "/" },
    DataPathPrefixes = new [] { "/chat/", "/join/", "/u/", "/user/invite/" },
    AutoVerify = true,
    Categories = new [] { Intent.CategoryDefault, Intent.CategoryBrowsable })]
public partial class MainActivity : MauiAppCompatActivity
{
    private static volatile MainActivity? _current;

    public static MainActivity Current => _current
        ?? throw StandardError.Internal($"{nameof(MainActivity)} isn't created yet.");
    public static readonly TimeSpan MaxPermissionRequestDuration = TimeSpan.FromMinutes(1);
    private static readonly Tracer _tracer = Tracer.Default[nameof(MainActivity)];

    private ActivityResultLauncher _permissionRequestLauncher = null!;
    private TaskCompletionSource? _permissionRequestCompletedSource;

    private ILogger Log { get; set; } = NullLogger.Instance;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        using var _1 = _tracer.Region();

        var isLoaded = false;
        Interlocked.Exchange(ref _current, this);
        if (TryGetScopedServices(out var scopedServices)) {
            var loadingUI = scopedServices.GetRequiredService<LoadingUI>();
            isLoaded = loadingUI.WhenLoaded.IsCompleted;
            // If app is sent to background with back button
            // and user brings it back to foreground by launching app icon or picking app from recents,
            // then warm start happens https://developer.android.com/topic/performance/vitals/launch-time#warm
            // MainActivity is created again, BlazorWebView and MauiBlazorApp also created also,
            // But the new instance of MauiBlazorApp uses same service provider and some services
            // are initialized again.
            // As a result, splash screen is getting hidden early and user sees index.html w/o any content yet.
            // TODO: to think how we can gracefully handle this partial recreation.
        }
        Log = AppServices.LogFor(GetType());
        _tracer.Point($"OnCreate, is loaded: {isLoaded}");

        base.OnCreate(savedInstanceState);
        _tracer.Point("OnCreate, base.OnCreate completed");

        // base.OnCreate call hides native splash screen. Set NavigationBar color the same as web splash screen
        // background color to make it looks like web splash screen covers entire screen.
        AndroidThemeHandler.SetNavigationBarColor(MauiSettings.SplashBackgroundColor);

        // Attempt to have notification reception even after app is swiped out.
        // https://github.com/firebase/quickstart-android/issues/368#issuecomment-683151061
        // seems it does not help
        var componentName = new ComponentName(this, Java.Lang.Class.FromType(typeof(FirebaseMessagingService)));
        PackageManager?.SetComponentEnabledSetting(componentName, ComponentEnabledState.Enabled, ComponentEnableOption.DontKillApp);

        // Create launcher to request permissions
        _permissionRequestLauncher = RegisterForActivityResult(
            new ActivityResultContracts.RequestPermission(),
            new AndroidActivityResultCallback(_ => {
                _permissionRequestCompletedSource?.TrySetResult();
                _permissionRequestCompletedSource = null;
            }));
        CreateNotificationChannel();
        TryHandleNotificationTap(Intent);

        // Keep the splash screen on-screen for longer periods
        // https://developer.android.com/develop/ui/views/launch/splash-screen#suspend-drawing
        var contentView = FindViewById(Android.Resource.Id.Content);
        contentView!.ViewTreeObserver!.AddOnPreDrawListener(new SplashDelayer(contentView));
    }

// NOTE(AY): Doesn't work, not sure why
#if false
    public override void OnCreate(Bundle? savedInstanceState, PersistableBundle? persistentState)
    {
        base.OnCreate(savedInstanceState, persistentState);
        SplashScreen.SetOnExitAnimationListener(new SplashScreenExitAnimationListener());
    }
#endif

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Interlocked.CompareExchange(ref _current, null, this);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        TryHandleNotificationTap(intent);
    }

    public override void OnTrimMemory(TrimMemory level)
    {
        Log.LogInformation("OnTrimMemory, Level: {Level}", level);
        DumpMemoryInfo();
        base.OnTrimMemory(level);
    }

    public Task RequestPermission(string permission, CancellationToken cancellationToken = default)
    {
        var whenCompletedSource = TaskCompletionSourceExt.New();
        _ = Task.Delay(MaxPermissionRequestDuration, cancellationToken)
            .ContinueWith(_ => whenCompletedSource.TrySetResult(), TaskScheduler.Default);
        return RequestPermission(permission, whenCompletedSource);
    }

    public Task RequestPermission(string permission, TaskCompletionSource whenCompletedSource)
    {
        _permissionRequestCompletedSource?.TrySetResult();
        _permissionRequestCompletedSource = whenCompletedSource;
        _permissionRequestLauncher.Launch(permission);
        return whenCompletedSource.Task;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        _permissionRequestCompletedSource?.TrySetResult();
        _permissionRequestCompletedSource = null;
    }

    private void CreateNotificationChannel()
    {
        if (OperatingSystem.IsOSPlatformVersionAtLeast("android", 26)) {
            var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;
            // After you create a notification channel,
            // you cannot change the notification behaviors—the user has complete control at that point.
            // Though you can still change a channel's name and description.
            // https://developer.android.com/develop/ui/views/notifications/channels
            var channel = new NotificationChannel(Constants.Notification.ChannelIds.Default, "Default", NotificationImportance.High);
            notificationManager.CreateNotificationChannel(channel);
        }
    }

    private void TryHandleNotificationTap(Intent? intent)
    {
        var extras = intent?.Extras;
        if (extras == null)
            return;

        var keySet = extras.KeySet()!.ToArray();
        if (!keySet.Contains(Constants.Notification.MessageDataKeys.NotificationId, StringComparer.Ordinal))
            return;

        // a notification action, lets collect message data
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(var key in keySet) {
            if (!Constants.Notification.MessageDataKeys.IsValidKey(key))
                continue;
            if (data.ContainsKey(key))
                continue;

            var extraValue = extras.Get(key);
            if (extraValue != null)
                data.Add(key, extraValue.ToString());
        }

        if (Log.IsEnabled(LogLevel.Information)) {
            var dataAsText = data.Select(c => $"'{c.Key}':'{c.Value}'").ToCommaPhrase();
            Log.LogInformation("NotificationTap, Data: {Data}", dataAsText);
        }

        var url = data.GetValueOrDefault(Constants.Notification.MessageDataKeys.Link);
        if (url.IsNullOrEmpty())
            return;

        var autoNavigationTasks = AppServices.GetRequiredService<AutoNavigationTasks>();
        autoNavigationTasks.Add(DispatchToBlazor(
            c => c.GetRequiredService<NotificationUI>().HandleNotificationNavigation(url),
            $"NotificationUI.HandleNotificationNavigation(\"{url}\")"));
    }

    private void DumpMemoryInfo()
    {
        var activityManager = (ActivityManager)GetSystemService(ActivityService)!;
        var memoryClass = activityManager.MemoryClass;
        Log.LogInformation("MemoryClass: {MemoryClass}", memoryClass);
        var memoryInfo = new ActivityManager.MemoryInfo();
        activityManager.GetMemoryInfo(memoryInfo);
        Log.LogInformation("MemoryInfo: AvailMem={AvailMem}, TotalMem={TotalMem}, LowMemory={LowMemory}, Threshold={Threshold}",
            memoryInfo.AvailMem,
            memoryInfo.TotalMem,
            memoryInfo.LowMemory,
            memoryInfo.Threshold);
        var processInfo = new ActivityManager.RunningAppProcessInfo();
        ActivityManager.GetMyMemoryState(processInfo);
        Log.LogInformation(
            "MyMemoryState: Pid={Pid}, LastTrimLevel={LastTrimLevel}, Lru={Lru}, Importance={Importance}, ImportanceReasonCode={ImportanceReasonCode}",
            processInfo.Pid,
            processInfo.LastTrimLevel,
            processInfo.Lru,
            processInfo.Importance,
            processInfo.ImportanceReasonCode);
    }

    private class SplashDelayer(AView contentView) : JObject, ViewTreeObserver.IOnPreDrawListener
    {
        private static readonly Task WhenRemoved = MauiLoadingUI.WhenFirstWebViewCreated;

        public bool OnPreDraw()
        {
            if (!WhenRemoved.IsCompleted)
                return false;

            contentView.ViewTreeObserver!.RemoveOnPreDrawListener(this);
            return true;
        }
    }
}
