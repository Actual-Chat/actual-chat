using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using ActualChat.Notification;
using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Android.Views;
using Android.Views.Animations;
using Android.Window;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using Microsoft.Maui.Controls.Platform;
using AView = Android.Views.View;

namespace ActualChat.App.Maui;

[Activity(
    Theme = "@style/SplashTheme",
    MainLauncher = true,
    // When user tap on a notification which was created by FCM when app was in background mode,
    // It causes creating a new instance of MainActivity. Apparently this happens because Intent has NewTask flag.
    // Creating a new instance of MainActivity causes creating a new instance of MauiBlazorApp even without disposing an existing one.
    // Setting LaunchMode to SingleTask prevents this behavior. Existing instance of MainActivity is used and Intent is passed to OnNewIntent method.
    // MauiBlazorApp instance is kept.
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges =
        ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density
    )]
[IntentFilter(
    new [] { Intent.ActionView },
    DataSchemes = new [] { "http", "https" },
    DataHost = MauiConstants.Host,
    DataPaths = new [] { "/" },
    DataPathPrefixes = new [] { "/chat/", "/join/", "/u/", "/user/invite/" },
    AutoVerify = true,
    Categories = new [] { Intent.CategoryDefault, Intent.CategoryBrowsable })]
public class MainActivity : MauiAppCompatActivity
{
    internal static readonly int NotificationID = 101;
    internal static readonly int NotificationPermissionID = 832;
    private readonly Tracer _tracer = Tracer.Default[nameof(MainActivity)];

    private ActivityResultLauncher _requestPermissionLauncher = null!;

    private ILogger Log { get; set; } = NullLogger.Instance;

    public static MainActivity? CurrentActivity { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        using var _1 = _tracer.Region();

        var isLoaded = false;
        CurrentActivity = this;
        if (TryGetScopedServices(out var scopedServices)) {
            var loadingUI = scopedServices.GetRequiredService<LoadingUI>();
            isLoaded = loadingUI.WhenLoaded.IsCompleted;
            // If app is sent to background with back button
            // and user brings it back to foreground by launching app icon or picking app from recents,
            // then warm start happens https://developer.android.com/topic/performance/vitals/launch-time#warm
            // MainActivity is created again, BlazorWebView and MauiBlazorApp also created also,
            // But the new instance of MauiBlazorApp uses same service provider and some services are initialized again.
            // Which is not expected.
            // As result, splash screen is hid very early and user sees index.html and other subsequent views.
            // TODO: to think how we can gracefully handle this partial recreation.
        }
        Log = AppServices.LogFor<MainActivity>();
        Log.LogDebug("OnCreate, is loaded: {IsLoaded}", isLoaded);

        base.OnCreate(savedInstanceState);
        Log.LogDebug("base.OnCreate completed");

        // Attempt to have notification reception even after app is swiped out.
        // https://github.com/firebase/quickstart-android/issues/368#issuecomment-683151061
        // seems it does not help
        var componentName = new ComponentName(this, Java.Lang.Class.FromType(typeof(FirebaseMessagingService)));
        PackageManager?.SetComponentEnabledSetting(componentName, ComponentEnabledState.Enabled, ComponentEnableOption.DontKillApp);

        // Create launcher to request permissions
        _requestPermissionLauncher = RegisterForActivityResult(
            new ActivityResultContracts.RequestPermission(),
            new ActivityResultCallback(result => {
                var isGranted = (bool)result!;
                var notificationState = isGranted
                    ? PermissionState.Granted
                    : PermissionState.Denied;
                _ = Task.Run(async () => {
                    var scopedServices1 = await ScopedServicesTask.ConfigureAwait(false);
                    var notificationUI = scopedServices1.GetRequiredService<NotificationUI>();
                    notificationUI.UpdateNotificationStatus(notificationState);
                });
            }));
        CreateNotificationChannel();

        TryHandleNotificationTap(Intent);

        // Keep the splash screen on-screen for longer periods
        // https://developer.android.com/develop/ui/views/launch/splash-screen#suspend-drawing
        var contentView = FindViewById(Android.Resource.Id.Content);
        contentView!.ViewTreeObserver!.AddOnPreDrawListener(new SplashScreenDelayer(contentView));
    }

// NOTE(AY): Doesn't work, not sure why
#if false
    public override void OnCreate(Bundle? savedInstanceState, PersistableBundle? persistentState)
    {
        base.OnCreate(savedInstanceState, persistentState);
        SplashScreen.SetOnExitAnimationListener(new SplashScreenExitAnimationListener());
    }
#endif

    protected override void OnStart()
    {
        _tracer.Point(nameof(OnStart));
        Log.LogDebug(nameof(OnStart));
        base.OnStart();
    }

    protected override void OnResume()
    {
        _tracer.Point(nameof(OnResume));
        Log.LogDebug(nameof(OnResume));
        base.OnResume();
    }

    protected override void OnStop()
    {
        _tracer.Point(nameof(OnStop));
        Log.LogDebug(nameof(OnStop));
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        _tracer.Point(nameof(OnDestroy));
        Log.LogDebug(nameof(OnDestroy));
        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        Log.LogDebug("OnNewIntent");
        base.OnNewIntent(intent);

        TryHandleNotificationTap(intent);
    }

    public void RequestPermissions(string permission)
        => _requestPermissionLauncher.Launch(permission);

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == NotificationPermissionID) {
            var (_, notificationGrant) = permissions
                .Zip(grantResults)
                .FirstOrDefault(tuple => OrdinalEquals(tuple.First, Manifest.Permission.PostNotifications));
            var notificationState = notificationGrant switch {
                Permission.Granted => PermissionState.Granted,
                _ => PermissionState.Denied,
            };
            _ = Task.Run(async () => {
                var scopedServices = await ScopedServicesTask.ConfigureAwait(false);
                var notificationUI = scopedServices.GetRequiredService<NotificationUI>();
                notificationUI.UpdateNotificationStatus(notificationState);
            });
        }
    }

    private void CreateNotificationChannel()
    {
        if (OperatingSystem.IsOSPlatformVersionAtLeast("android", 26)) {
            var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;
            // After you create a notification channel,
            // you cannot change the notification behaviors—the user has complete control at that point.
            // Though you can still change a channel's name and description.
            // https://developer.android.com/develop/ui/views/notifications/channels
            var channel = new NotificationChannel(NotificationConstants.ChannelIds.Default, "Default", NotificationImportance.High);
            notificationManager.CreateNotificationChannel(channel);
        }
    }

    private void TryHandleNotificationTap(Intent? intent)
    {
        var extras = intent?.Extras;
        if (extras == null)
            return;

        var keySet = extras.KeySet()!.ToArray();
        if (!keySet.Contains(NotificationConstants.MessageDataKeys.NotificationId, StringComparer.Ordinal))
            return;

        // a notification action, lets collect message data
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(var key in keySet) {
            if (!NotificationConstants.MessageDataKeys.IsValidKey(key))
                continue;
            if (data.ContainsKey(key))
                continue;
            var extraValue = extras.Get(key);
            if (extraValue != null)
                data.Add(key, extraValue.ToString());
        }

        if (Log.IsEnabled(LogLevel.Debug)) {
            var dataAsText = data.Select(c => $"'{c.Key}':'{c.Value}'").ToCommaPhrase();
            Log.LogDebug("NotificationTap. Data: {Data}", dataAsText);
        }

        var url = data.GetValueOrDefault(NotificationConstants.MessageDataKeys.Link);
        if (url.IsNullOrEmpty())
            return;

        _ = Task.Run(async () => {
            var scopedServices = await ScopedServicesTask.ConfigureAwait(false);
            var notificationUI = scopedServices.GetRequiredService<NotificationUI>();
            notificationUI.HandleNotificationNavigation(url);
        });
    }

    public class SplashScreenExitAnimationListener : GenericAnimatorListener, ISplashScreenOnExitAnimationListener
    {
        public void OnSplashScreenExit(SplashScreenView view)
        {
            Tracer.Default.Point("OnSplashScreenExit");
            var fade = new AlphaAnimation(1f, 0f) {
                Duration = 500,
                Interpolator = new LinearInterpolator(),
            };
            view.AnimationEnd += (_, _) => {
                Tracer.Default.Point("OnSplashScreenExit - AnimationEnd");
                view.Remove();
            };
            view.StartAnimation(fade);
        }
    }
    private class SplashScreenDelayer : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
    {
        private static bool _splashRemoved; // Iron pants to prevent splash screen displayed after app is taken back from background.
        private readonly AView _contentView;
        private readonly Task _whenSplashRemoved = LoadingUI.WhenViewCreated.WithDelay(TimeSpan.FromMilliseconds(50));

        public SplashScreenDelayer(AView contentView)
            => _contentView = contentView;

        public bool OnPreDraw()
        {
            if (_splashRemoved)
                return true;
            if (!_whenSplashRemoved.IsCompleted)
                return false;

            _splashRemoved = true;
            _contentView.ViewTreeObserver!.RemoveOnPreDrawListener(this);
            return true;
        }
    }
}
