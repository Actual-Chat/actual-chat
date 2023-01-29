using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Android.Gms.Auth.Api.SignIn;
using Android.Content;
using Result = Android.App.Result;
using ActualChat.App.Maui.Services;
using ActualChat.Notification;
using ActualChat.UI.Blazor.Services;
using Android.Views;

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
public class MainActivity : MauiAppCompatActivity
{
    private const int RC_SIGN_IN_GOOGLE = 800;
    // GoogleClientIds below are taken for Web application since session authentication performed on the web server.
#if ISDEVMAUI
    private const string ServerGoogleClientId = "367046672456-75p2d55jama2mtivjbcgp0hkaa6jsihq.apps.googleusercontent.com";
#else
    private const string ServerGoogleClientId = "936885469539-89riml3ri3rsu35tdh9gtdvrtj4c08fs.apps.googleusercontent.com";
#endif

    internal static readonly int NotificationID = 101;

    private GoogleSignInClient mGoogleSignInClient = null!;

    private ILogger Log { get; set; } = NullLogger.Instance;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var isLoaded = false;
        if (ScopedServicesAccessor.IsInitialized) {
            var loadingUI = ScopedServicesAccessor.ScopedServices.GetRequiredService<LoadingUI>();
            isLoaded = loadingUI.WhenLoaded.IsCompleted;
            // If app is put to background with back button
            // and user brings app to foreground by launching app icon or picking app from recents,
            // then warm start happens https://developer.android.com/topic/performance/vitals/launch-time#warm
            // MainActivity is created again, BlazorWebView and MauiBlazorApp also created also,
            // But new instance of MauiBlazorApp uses same service provider and some services are initialized again.
            // Which is not expected.
            // As result, splash screen is hid very early and user sees index.html and other subsequent views.
            // TODO: to think how we can gracefully handle this partial recreation.
        }
        Log = AppServices.LogFor<MainActivity>();
        Log.LogDebug("MainActivity.OnCreate, is loaded: {IsLoaded}", isLoaded);

        base.OnCreate(savedInstanceState);

        Log.LogDebug("MainActivity.base.OnCreate completed");

        // Attempt to have notification reception even after app is swiped out.
        // https://github.com/firebase/quickstart-android/issues/368#issuecomment-683151061
        // seems it does not help
        var componentName = new ComponentName(this, Java.Lang.Class.FromType(typeof(FirebaseMessagingService)));
        PackageManager?.SetComponentEnabledSetting(componentName, ComponentEnabledState.Enabled, ComponentEnableOption.DontKillApp);

        // Configure sign-in to request the user's ID, email address, and basic
        // profile. ID and basic profile are included in DEFAULT_SIGN_IN.
        GoogleSignInOptions gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestEmail()
            .RequestIdToken(ServerGoogleClientId)
            .RequestServerAuthCode(ServerGoogleClientId)
            .Build();

        // Build a GoogleSignInClient with the options specified by gso.
        mGoogleSignInClient = GoogleSignIn.GetClient(this, gso);

        _ = AutoSignInOnStart();

        CreateNotificationChannel();

        TryProcessNotificationTap(Intent);

        // Keep the splash screen on-screen for longer periods
        // https://developer.android.com/develop/ui/views/launch/splash-screen#suspend-drawing
        var content = FindViewById(Android.Resource.Id.Content);
        content!.ViewTreeObserver!.AddOnPreDrawListener(new PreDrawListener());
    }

    protected override void OnStart()
    {
        Log.LogDebug("MainActivity.OnStart");
        base.OnStart();
    }

    protected override void OnStop()
    {
        Log.LogDebug("MainActivity.OnStop");
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        Log.LogDebug("MainActivity.OnDestroy");
        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        Log.LogDebug("MainActivity.OnNewIntent");
        base.OnNewIntent(intent);

        TryProcessNotificationTap(intent);
    }

    public Task SignInWithGoogle()
    {
        StartActivityForResult(mGoogleSignInClient.SignInIntent, RC_SIGN_IN_GOOGLE);
        return Task.CompletedTask;
    }

    public bool IsSignedInWithGoogle()
    {
        // Check for existing Google Sign In account, if the user is already signed in
        // the GoogleSignInAccount will be non-null.
        var account = GoogleSignIn.GetLastSignedInAccount(this);
        return account != null;
    }

    public async Task SignOutWithGoogle()
    {
        await mGoogleSignInClient.SignOutAsync().ConfigureAwait(true);
        var mobileAuthClient = AppServices.GetRequiredService<MobileAuthClient>();
        await mobileAuthClient.SignOut().ConfigureAwait(true);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == RC_SIGN_IN_GOOGLE) {
            async Task CheckResult(Intent data1)
            {
                try {
                    var account = await GoogleSignIn.GetSignedInAccountFromIntentAsync(data1).ConfigureAwait(true);
                    if (account != null)
                        _ = OnSignInWithGoogle(account);
                }
                catch (Android.Gms.Common.Apis.ApiException e) {
                    Log.LogDebug(e, "Could not get an account from intent");
                }
            }
            if (resultCode == Result.Ok)
                _ = CheckResult(data!);
            else {
                Log.LogDebug("Google SignIn. SignInIntent result is NOK. Actual result: {ResultCode}", resultCode);
                new AlertDialog.Builder(this)
                    .SetTitle("Google SignIn")!
                    .SetMessage($"SignInIntent result is NOK. Actual result: {resultCode}.")!
                    .Show();
            }
        }
    }

    private async Task OnSignInWithGoogle(GoogleSignInAccount account)
    {
        var code = account.ServerAuthCode;
        if (string.IsNullOrEmpty(code))
            return;
        var mobileAuthClient = AppServices.GetRequiredService<MobileAuthClient>();
        await mobileAuthClient.SignInGoogle(code).ConfigureAwait(true);
    }

    private async Task AutoSignInOnStart()
    {
        // Check for existing Google Sign In account, if it exists then request authentication code and authenticate session.
        if (IsSignedInWithGoogle())
            await SignInWithGoogle().ConfigureAwait(true);
    }

    private void CreateNotificationChannel()
    {
        if (OperatingSystem.IsOSPlatformVersionAtLeast("android", 26)) {
            var notificationManager = (NotificationManager)GetSystemService(Android.Content.Context.NotificationService)!;
            // After you create a notification channel,
            // you cannot change the notification behaviors—the user has complete control at that point.
            // Though you can still change a channel's name and description.
            // https://developer.android.com/develop/ui/views/notifications/channels
            var channel = new NotificationChannel(NotificationConstants.ChannelIds.Default, "Default", NotificationImportance.High);
            notificationManager.CreateNotificationChannel(channel);
        }
    }

    private void TryProcessNotificationTap(Intent? intent)
    {
        var extras = intent?.Extras;
        if (extras == null)
            return;

        var keySet = extras.KeySet()!.ToArray();
        if (!keySet.Contains(NotificationConstants.MessageDataKeys.NotificationId, StringComparer.Ordinal))
            return;

        Log.LogDebug($"MainActivity.NotificationTap");

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

        var url = data.GetValueOrDefault(NotificationConstants.MessageDataKeys.Link);
        if (url.IsNullOrEmpty())
            return;

        async Task Handle()
        {
            await ScopedServicesAccessor.WhenInitialized.ConfigureAwait(true);
            var serviceProvider = ScopedServicesAccessor.ScopedServices;
            var loadingUI = serviceProvider.GetRequiredService<LoadingUI>();
            await loadingUI.WhenLoaded.ConfigureAwait(true);
            var handler = serviceProvider.GetRequiredService<NotificationNavigationHandler>();
            Log.LogDebug("MainActivity.NotificationTap navigates to '{Url}'", url);
            _ = handler.Handle(url);
        }
        _ = Handle();
    }

    private class PreDrawListener : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
    {
        public bool OnPreDraw()
            => ScopedServicesAccessor.IsInitialized
                && ScopedServicesAccessor.ScopedServices.GetRequiredService<LoadingUI>().WhenLoaded.IsCompleted;
    }
}
