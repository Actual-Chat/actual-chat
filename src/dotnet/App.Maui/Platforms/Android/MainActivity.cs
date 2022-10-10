using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Android.Gms.Auth.Api.SignIn;
using Android.Util;
using Android.Content;
using Result = Android.App.Result;
using ActualChat.App.Maui.Services;
using ActualChat.Notification;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using AndroidX.Core.Content;

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
    private const string TAG = nameof(MainActivity);
    // GoogleClientIds below are taken for Web application since session authentication performed on the web server.
#if ISDEVMAUI
    private const string ServerGoogleClientId = "784581221205-frrmhss3h51h5c1jaiglpal4olod7kr8.apps.googleusercontent.com";
#else
    private const string ServerGoogleClientId = "936885469539-89riml3ri3rsu35tdh9gtdvrtj4c08fs.apps.googleusercontent.com";
#endif

    internal static readonly int NotificationID = 101;

    private GoogleSignInClient mGoogleSignInClient = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // atempt to have notification reception even after app is swiped out.
        // https://github.com/firebase/quickstart-android/issues/368#issuecomment-683151061
        // seems it does not help
        var componentName = new ComponentName(this, Java.Lang.Class.FromType(typeof(FirebaseMessagingService)));
        PackageManager?.SetComponentEnabledSetting(componentName, ComponentEnabledState.Enabled, ComponentEnableOption.DontKillApp);

        // TODO: move permissions request to where it's really needed
        // https://github.com/dotnet/maui/issues/3694#issuecomment-1014880727
        // https://stackoverflow.com/questions/70229906/blazor-maui-camera-and-microphone-android-permissions
        ActivityCompat.RequestPermissions(this, new[] {
            Manifest.Permission.Camera,
            Manifest.Permission.RecordAudio,
            Manifest.Permission.ModifyAudioSettings
        }, 0);

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
    }

    const string tag = "actual.chat";

    protected override void OnStart()
    {
        Log.Debug(tag, "OnStart");
        base.OnStart();
    }

    protected override void OnStop()
    {
        Log.Debug(tag, "OnStop");
        base.OnStop();
    }

    protected override void OnNewIntent(Intent? intent)
    {
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
                    Log.Debug(TAG, "Could not get an account from intent: " + e.ToString());
                }
            }
            if (resultCode == Result.Ok)
                _ = CheckResult(data!);
            else {
                new AlertDialog.Builder(this)
                    .SetTitle("Google SignIn")
                    .SetMessage($"SignInIntent result is NOK. Actual result: {resultCode}.")
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

        data.TryGetValue(NotificationConstants.MessageDataKeys.Link, out var url);
        if (!url.IsNullOrEmpty() && ScopedServiceLocator.IsInitialized) {
            var handler = ScopedServiceLocator.Services.GetRequiredService<NotificationNavigationHandler>();
            handler.Handle(url);
        }
    }
}
