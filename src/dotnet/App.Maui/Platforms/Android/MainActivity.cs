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

namespace ActualChat.App.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density )]
public class MainActivity : MauiAppCompatActivity
{
    private const int RC_DEFAULT_SIGN_IN = 800;
    private const string TAG = nameof(MainActivity);
    private const string GoogleClientId = "784581221205-frrmhss3h51h5c1jaiglpal4olod7kr8.apps.googleusercontent.com";

    private GoogleSignInClient mGoogleSignInClient = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

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
            .RequestIdToken(GoogleClientId)
            .RequestServerAuthCode(GoogleClientId)
            .Build();

        // Build a GoogleSignInClient with the options specified by gso.
        mGoogleSignInClient = GoogleSignIn.GetClient(this, gso);

        //// Check for existing Google Sign In account, if the user is already signed in
        //// the GoogleSignInAccount will be non-null.
        //var account = GoogleSignIn.GetLastSignedInAccount(this);
        //if (account != null)
        //    OnDefaultSignIn(account);
        // TODO(DM): implement automatic sign in if account not null 
    }

    public Task DefaultSignIn()
    {
        StartActivityForResult(mGoogleSignInClient.SignInIntent, RC_DEFAULT_SIGN_IN);
        return Task.CompletedTask;
    }

    public async Task DefaultSignOut()
    {
        await mGoogleSignInClient.SignOutAsync().ConfigureAwait(true);
        var mobileAuthClient = ServiceLocator.Services.GetRequiredService<MobileAuthClient>();
        await mobileAuthClient.SignOut().ConfigureAwait(true);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == RC_DEFAULT_SIGN_IN) {
            async Task CheckResult(Intent data1)
            {
                try {
                    var account = await GoogleSignIn.GetSignedInAccountFromIntentAsync(data1).ConfigureAwait(true);
                    if (account != null)
                        _ = OnDefaultSignIn(account);
                }
                catch (Android.Gms.Common.Apis.ApiException e) {
                    Log.Debug(TAG, "Could not get an account from intent: " + e.ToString());
                }
            }
            if (resultCode == Result.Ok)
                _ = CheckResult(data!);
        }
    }

    private async Task OnDefaultSignIn(GoogleSignInAccount account)
    {
        var code = account.ServerAuthCode;
        if (!string.IsNullOrEmpty(code)) {
            var mobileAuthClient = ServiceLocator.Services.GetRequiredService<MobileAuthClient>();
            await mobileAuthClient.SignInGoogle(code).ConfigureAwait(true);
        }
    }
}
