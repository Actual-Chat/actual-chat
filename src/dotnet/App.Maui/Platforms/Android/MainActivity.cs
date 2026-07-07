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
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

[MetaData("android.app.shortcuts", Resource = "@xml/share_targets")]
[Activity(
    Name = MauiSettings.IsDevApp ? "chat.actual.dev.app.MainActivity" : "actual.chat.app.MainActivity",
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
    [Intent.ActionView],
    DataSchemes = ["http", "https"],
    DataHost = MauiSettings.DefaultHost, /* TODO(DF): rework dynamic intent filter configuration */
    DataPaths = ["/"],
    DataPathPrefixes = ["/chat/", "/place/", "/join/", "/u/", "/user/invite/"],
    AutoVerify = true,
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable])]
public partial class MainActivity : MauiAppCompatActivity
{
    private static volatile MainActivity? _current;
    private static volatile bool _isFirstTime = true;

    public static MainActivity Current => _current
        ?? throw StandardError.Internal($"{nameof(MainActivity)} isn't created yet.");
    public static readonly TimeSpan MaxPermissionRequestDuration = TimeSpan.FromMinutes(1);
    private static readonly Tracer Tracer = Tracer.Default[nameof(MainActivity)];

    private ActivityResultLauncher _permissionRequestLauncher = null!;
    private ActivityResultLauncher _pickVisualMediaLauncher = null!;
    private Action<bool>? _onReceivePermissionRequestResult;
    private Action<Uri[]>? _onReceivePickVisualMedialResult;

    private ILogger Log { get; } = StaticLog.For<MainActivity>();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        using var _1 = Tracer.MethodRegion();

        BlazorWebViewApp.EnsureStarted();

        Interlocked.Exchange(ref _current, this);
        // If app is sent to background with back button
        // and user brings it back to foreground by launching app icon or picking app from recents,
        // then warm start happens https://developer.android.com/topic/performance/vitals/launch-time#warm
        // MainActivity is created again, BlazorWebView and MauiBlazorApp also created also,
        // But the new instance of MauiBlazorApp uses same service provider and some services
        // are initialized again.
        // As a result, splash screen is getting hidden early and user sees index.html w/o any content yet.
        // TODO: to think how we can gracefully handle this partial recreation.
        Log.LogInformation("OnCreate: isFirstTime={IsFirstTime}", _isFirstTime);
        _isFirstTime = false;

        // ReSharper disable once ExplicitCallerInfoArgument
        using(Tracer.Region("Calling base.OnCreate"))
            base.OnCreate(Bundle.Empty);

        // base.OnCreate call hides native splash screen. Set NavigationBar color the same as web splash screen
        // background color to make it look like web splash screen covers the entire screen.
        var splashColor = MauiSettings.SplashBackgroundColor.ToArgbHex();
        AndroidThemeHandler.SetBarsAppearance(splashColor, splashColor);

        // Attempt to have notification reception even after app is swiped out.
        // https://github.com/firebase/quickstart-android/issues/368#issuecomment-683151061
        // seems it does not help
        var componentName = new ComponentName(this, Java.Lang.Class.FromType(typeof(FirebaseMessagingService)));
        PackageManager?.SetComponentEnabledSetting(componentName, ComponentEnabledState.Enabled, ComponentEnableOption.DontKillApp);

        // Create launcher to request permissions
        _permissionRequestLauncher = RegisterForActivityResult(
            new ActivityResultContracts.RequestPermission(),
            new AndroidActivityResultCallback(isGranted => {
                _onReceivePermissionRequestResult?.Invoke(isGranted != null && (bool)isGranted);
                _onReceivePermissionRequestResult = null;
            }));

        _pickVisualMediaLauncher = RegisterForActivityResult(
            new ActivityResultContracts.PickMultipleVisualMedia(10),
            new AndroidActivityResultCallback(obj => {
                var list = new List<Uri>();
                if (obj is Android.Runtime.JavaList javaList) {
                    for (var i = 0; i < javaList.Count; i++) {
                        var obj2 = javaList.Get(i);
                        if (obj2 is Uri uri)
                            list.Add(uri);
                    }
                }
                _onReceivePickVisualMedialResult?.Invoke(list.ToArray());
                _onReceivePickVisualMedialResult = null;
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
        base.OnTrimMemory(level);
        // Diagnostics only, so run off the UI thread: OnTrimMemory fires under memory pressure -
        // exactly when the managed runtime is busy collecting - and the JNI-heavy dump below blocks
        // the UI thread long enough to be reported as an ANR.
        _ = Task.Run(DumpMemoryInfo);
    }

    public void RequestPermission(string permission, Action<bool> onReceiveResult, bool throwIfHavePendingRequest = false)
    {
        if (throwIfHavePendingRequest && _onReceivePermissionRequestResult is not null)
            throw StandardError.Constraint("Cannot perform multiple permission requests simultaneously.");

        _onReceivePermissionRequestResult?.Invoke(false);
        _onReceivePermissionRequestResult = onReceiveResult;
        _permissionRequestLauncher.Launch(permission);
    }

    public void PickVisualMedia(PickVisualMediaKind kind, Action<Uri[]> onReceiveResult)
    {
        _onReceivePickVisualMedialResult?.Invoke(Array.Empty<Uri>());
        _onReceivePickVisualMedialResult = onReceiveResult;

        ActivityResultContracts.PickVisualMedia.IVisualMediaType visualMediaType = kind switch {
            PickVisualMediaKind.Image => ActivityResultContracts.PickVisualMedia.ImageOnly.Instance,
            PickVisualMediaKind.Video => ActivityResultContracts.PickVisualMedia.VideoOnly.Instance,
            _ => ActivityResultContracts.PickVisualMedia.ImageAndVideo.Instance,
        };
        var pickVisualMediaRequest = new PickVisualMediaRequest.Builder()
             .SetMediaType(visualMediaType)
             .Build();
        _pickVisualMediaLauncher.Launch(pickVisualMediaRequest);
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
        // Failsafe cap on the pre-draw hold. Suspending the first frame until the WebView/Blazor
        // startup callback fires is fine only if that callback is guaranteed to arrive - but on a
        // stalled/warm-start boot it may not, and holding forever starves input dispatch, which
        // Android reports as a "no focused window" ANR. Release the frame after this regardless.
        private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(4);
        private static readonly Task WhenRemoved = MauiLoadingUI.WhenFirstWebViewCreated;
        private readonly CpuTimestamp _startedAt = CpuTimestamp.Now;

        public bool OnPreDraw()
        {
            if (!WhenRemoved.IsCompleted && CpuTimestamp.Now - _startedAt < MaxDelay)
                return false;

            contentView.ViewTreeObserver!.RemoveOnPreDrawListener(this);
            return true;
        }
    }
}

public enum PickVisualMediaKind { Image, Video, Both }
