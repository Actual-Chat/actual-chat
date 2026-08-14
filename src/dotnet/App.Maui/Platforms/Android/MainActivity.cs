using System.Runtime.Versioning;
using ActualChat.App.Maui.Activities;
using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services;
using ActualLab.Diagnostics;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
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
    private AView? _callCover;

    private ILogger Log { get; } = StaticLog.For<MainActivity>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Before base.OnCreate, which calls setContentView - the listener has to be registered
        // before that for the dismissal logic to see it.
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            SplashScreen!.SetOnExitAnimationListener(new SplashExitAnimator());

        using var _1 = Tracer.MethodRegion();

        if (!AndroidUtils.IsWebViewAvailable()) {
            // Without a WebView provider, BlazorAndroidWebView creation crashes in onStart with
            // MissingWebViewPackageException. Finish() here skips onStart, so the fragment view
            // is never created; a native prompt takes over instead.
            Log.LogWarning("OnCreate: no WebView provider available");
            base.OnCreate(Bundle.Empty);
            StartActivity(new Intent(this, typeof(WebViewMissingActivity)));
            Finish();
            return;
        }

        BlazorWebViewApp.EnsureStarted();
        CloseHeadlessScope();

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
        MauiStartupBreadcrumbs.Add($"MainActivity.OnCreate (isFirstTime: {_isFirstTime})");
        _isFirstTime = false;

        // ReSharper disable once ExplicitCallerInfoArgument
        using(Tracer.Region("Calling base.OnCreate"))
            base.OnCreate(Bundle.Empty);

        // base.OnCreate call hides native splash screen. Set NavigationBar color the same as web splash screen
        // background color to make it look like web splash screen covers the entire screen.
        var splashColor = MauiSettings.SplashBackgroundColor.ToArgbHex();
        AndroidThemeHandler.SetBarsAppearance(splashColor, splashColor);

        // Here rather than anywhere in the app: Android only grants a foreground service the
        // microphone type if it starts while the app is in the foreground, and by the time the app
        // knows its own armed chats it has often been backgrounded already - which costs the walkie
        // reply its mic for the whole life of the service. See ActivitiesBackend.OnArmedChanged.
        if (MauiPreferences.IsWalkieArmed)
            AndroidActivitiesForegroundService.TryStartArmed(this);

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

    // Shows the activity over the lock screen and wakes the screen for an incoming call.
    public void EnableShowWhenLocked()
    {
        SetShowWhenLocked(true);
        SetTurnScreenOn(true);
    }

    // Reverts any over-lock-screen behavior once the ring ends, so the app doesn't linger over the
    // keyguard on later locks (e.g. after answering from the notification while locked).
    public void DisableShowWhenLocked()
    {
        SetShowWhenLocked(false);
        SetTurnScreenOn(false);
        HideCallCover();
    }

    // Opaque cover placed over the WebView the instant the app is brought over the keyguard for a
    // call, so the app's own content never flashes on the lock screen before the (Blazor) call
    // screen has rendered. HideCallCover reveals the already-drawn call screen underneath.
    public void ShowCallCover()
    {
        DebugLog?.LogInformation("CALL_TRACE: ShowCallCover (alreadyShown={AlreadyShown})", _callCover != null);
        if (_callCover != null)
            return;
        if (Window?.DecorView is not ViewGroup decor)
            return;

        var hex = MauiSettings.SplashBackgroundColor.ToArgbHex();
        if (!hex.StartsWith('#'))
            hex = "#" + hex;
        var cover = new AView(this);
        cover.SetBackgroundColor(Android.Graphics.Color.ParseColor(hex));
        decor.AddView(cover, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _callCover = cover;
    }

    public void HideCallCover()
    {
        DebugLog?.LogInformation("CALL_TRACE: HideCallCover (hadCover={HadCover})", _callCover != null);
        if (_callCover?.Parent is ViewGroup parent)
            parent.RemoveView(_callCover);
        _callCover = null;
    }

    // On accept: if the screen is locked, ask the user to unlock so they land in the app instead
    // of being dropped back to the lock screen when the over-keyguard flag is cleared. Reports back
    // whether the screen ended up unlocked (foreground-ready), so audio capture waits for it.
    public void DismissKeyguardForCall(Action<bool> onResult)
    {
        var keyguardManager = (KeyguardManager?)GetSystemService(KeyguardService);
        if (keyguardManager is null || !keyguardManager.IsKeyguardLocked) {
            DisableShowWhenLocked();
            onResult(true);
            return;
        }
        keyguardManager.RequestDismissKeyguard(this, new CallKeyguardDismissCallback(this, onResult));
    }

    private sealed class CallKeyguardDismissCallback(MainActivity activity, Action<bool> onResult)
        : KeyguardManager.KeyguardDismissCallback
    {
        public override void OnDismissSucceeded()
        {
            activity.DisableShowWhenLocked();
            onResult(true);
        }

        public override void OnDismissError()
        {
            activity.DisableShowWhenLocked();
            onResult(false);
        }

        public override void OnDismissCancelled()
        {
            activity.DisableShowWhenLocked();
            onResult(false);
        }
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

    private void CloseHeadlessScope()
    {
        // A live headless scope means a walkie reply may be recording with no UI on screen.
        // TryDetachCurrent clears _current synchronously - a wake or a headset press racing this
        // must see no headless scope, not the one that's about to be stopped and disposed.
        if (HeadlessBlazorScope.TryDetachCurrent("MainActivity.OnCreate") is not { } scope)
            return;

        _ = BackgroundTask.Run(
            () => WalkieTalkieSession.StopAndDispose(scope), Log, "Failed to close the headless walkie scope");
    }

    private void DumpMemoryInfo()
    {
        try {
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
        catch (Exception e) {
            Log.LogWarning(e, "DumpMemoryInfo failed");
        }
    }

    [SupportedOSPlatform("android31.0")]
    private sealed class SplashExitAnimator : JObject, Android.Window.ISplashScreenOnExitAnimationListener
    {
        // Runs entirely after the first frame, so every ms of it is added latency. It is also a
        // main-thread animation, so a long WebView raster frame starves it: at 300ms the splash
        // routinely outlived the first frame by ~330ms rather than by its nominal duration.
        private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(150);

        public void OnSplashScreenExit(Android.Window.SplashScreenView splashScreenView)
        {
            splashScreenView.Animate()!
                .Alpha(0f)
                .SetDuration((long)FadeDuration.TotalMilliseconds)!
                .WithEndAction(new Java.Lang.Runnable(splashScreenView.Remove))!
                .Start();
        }
    }

    private class SplashDelayer(AView contentView) : JObject, ViewTreeObserver.IOnPreDrawListener
    {
        // Cap the pre-draw hold: if the WebView callback never arrives, holding forever starves
        // input dispatch and Android reports a "no focused window" ANR.
        private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(3);
        // WhenFirstSplashRemoved, not WhenFirstWebViewCreated: the latter fires when the WebView is
        // constructed, long before it renders, which is what dropped the logo while the app was blank.
        private static readonly Task WhenRemoved = MauiLoadingUI.WhenFirstSplashRemoved;
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
