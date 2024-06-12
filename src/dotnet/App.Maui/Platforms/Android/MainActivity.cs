using ActualChat.App.Maui.Services;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
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
    DataHost = MauiSettings.DefaultHost, /* TODO(DF): rework dynamic intent filter configuration */
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
    private static readonly Tracer Tracer = Tracer.Default[nameof(MainActivity)];

    private ActivityResultLauncher _permissionRequestLauncher = null!;
    private TaskCompletionSource? _permissionRequestCompletedSource;

    private ILogger Log { get; set; } = NullLogger.Instance;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        using var _1 = Tracer.Region();

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

        Log.LogInformation("OnCreate. IsLoaded={IsLoaded}", isLoaded);

        // ReSharper disable once ExplicitCallerInfoArgument
        using(Tracer.Region("Calling base.OnCreate"))
            base.OnCreate(Bundle.Empty);

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

        // Keep the splash screen on-screen for longer periods
        // https://developer.android.com/develop/ui/views/launch/splash-screen#suspend-drawing
        var contentView = FindViewById(Android.Resource.Id.Content);
        contentView!.ViewTreeObserver!.AddOnPreDrawListener(new SplashDelayer(contentView));
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Interlocked.CompareExchange(ref _current, null, this);
    }

    public override void OnTrimMemory(TrimMemory level)
    {
        Log.LogInformation("OnTrimMemory, Level: {Level}", level);
        DumpMemoryInfo();
        base.OnTrimMemory(level);
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
