using ActualChat.App.Maui.Services;
using Android.Content;
using Android.Gms.Auth.Api.SignIn;
using Activity = Android.App.Activity;
using Result = Android.App.Result;

namespace ActualChat.App.Maui;

public sealed class NativeGoogleSignIn
{
    private const int GoogleSignInRequestCode = 800;
    // GoogleClientIds below are taken for Web application since session authentication performed on the web server.
#if IS_DEV_MAUI
    private const string ServerGoogleClientId =
        "367046672456-75p2d55jama2mtivjbcgp0hkaa6jsihq.apps.googleusercontent.com";
#else
    private const string ServerGoogleClientId =
        "936885469539-89riml3ri3rsu35tdh9gtdvrtj4c08fs.apps.googleusercontent.com";
#endif

    private readonly GoogleSignInClient _googleSignInClient;

    private IServiceProvider Services { get; }
    private ILogger Log { get; }

    public NativeGoogleSignIn(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        // Configure sign-in to request the user's ID, email address, and basic
        // profile. ID and basic profile are included in DEFAULT_SIGN_IN.
        var gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestEmail()
            .RequestIdToken(ServerGoogleClientId)
            .RequestServerAuthCode(ServerGoogleClientId)
            .Build();
        // Build a GoogleSignInClient with the options specified by gso.
        _googleSignInClient = GoogleSignIn.GetClient(MainActivity.Current, gso);
        AndroidActivityResultHandlers.Register(OnActivityResult);
    }

    public void Dispose()
    {
        AndroidActivityResultHandlers.Unregister(OnActivityResult);
        _googleSignInClient.Dispose();
    }

    public Task SignIn()
    {
        var signInIntent = _googleSignInClient.SignInIntent;
        MainActivity.Current.StartActivityForResult(signInIntent, GoogleSignInRequestCode);
        return Task.CompletedTask;
    }

    public bool IsSignedIn()
    {
        // Check for existing Google Sign In account, if the user is already signed in
        // the GoogleSignInAccount will be non-null.
        var account = GoogleSignIn.GetLastSignedInAccount(MainActivity.Current);
        return account != null;
    }

    public Task SignOut()
        => _googleSignInClient.SignOutAsync();

    // Private methods

    private void OnActivityResult(Activity activity, int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != GoogleSignInRequestCode)
            return;

        async Task CheckResult(Intent data1)
        {
            try {
                var account = await GoogleSignIn.GetSignedInAccountFromIntentAsync(data1).ConfigureAwait(true);
                if (account != null)
                    _ = OnSignIn(account);
            }
            catch (Android.Gms.Common.Apis.ApiException e) {
                Log.LogDebug(e, "Could not get an account from intent");
            }
        }

        if (resultCode == Result.Ok)
            _ = CheckResult(data!);
        else
            Log.LogDebug("Google SignIn. SignInIntent result is NOK. Actual result: {ResultCode}", resultCode);
    }

    private async Task OnSignIn(GoogleSignInAccount account)
    {
        var code = account.ServerAuthCode;
        if (code.IsNullOrEmpty())
            return;

        var mobileAuthClient = Services.GetRequiredService<MobileAuthClient>();
        await mobileAuthClient.SignInGoogleWithCode(code).ConfigureAwait(true);
    }
}
