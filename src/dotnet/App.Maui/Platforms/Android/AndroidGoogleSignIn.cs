using ActualChat.App.Maui.Services;
using Android.Content;
using Android.Gms.Auth.Api.SignIn;
using Activity = Android.App.Activity;
using Result = Android.App.Result;

namespace ActualChat.App.Maui;

public sealed class AndroidGoogleSignIn
{
    private AndroidGoogleSignInImpl? _impl;

    private IServiceProvider Services { get; }

    private AndroidGoogleSignInImpl Impl {
        get {
            var mainActivity = MainActivity.CurrentActivity;
            if (mainActivity == null)
                throw StandardError.Constraint("No current main activity.");
            if (_impl != null && _impl.MainActivity != mainActivity) {
                _impl.Dispose();
                _impl = null;
            }
            _impl ??= new AndroidGoogleSignInImpl(mainActivity, Services);
            return _impl;
        }
    }

    public AndroidGoogleSignIn(IServiceProvider services)
        => Services = services;

    public Task SignIn()
        => Impl.SignIn();

    public bool IsSignedIn()
        => Impl.IsSignedIn();

    public Task SignOut()
        => Impl.SignOut();

    private sealed class AndroidGoogleSignInImpl
    {
        private const int GoogleSignInRequestCode = 800;
        // GoogleClientIds below are taken for Web application since session authentication performed on the web server.
#if ISDEVMAUI
        private const string ServerGoogleClientId =
            "367046672456-75p2d55jama2mtivjbcgp0hkaa6jsihq.apps.googleusercontent.com";
#else
        private const string ServerGoogleClientId =
            "936885469539-89riml3ri3rsu35tdh9gtdvrtj4c08fs.apps.googleusercontent.com";
#endif

        private readonly GoogleSignInClient _googleSignInClient;

        public MainActivity MainActivity { get; }

        private IServiceProvider Services { get; }
        private ILogger Log { get; }

        public AndroidGoogleSignInImpl(MainActivity mainActivity, IServiceProvider services)
        {
            MainActivity = mainActivity;
            Services = services;
            Log = services.LogFor<AndroidGoogleSignIn>();

            // Configure sign-in to request the user's ID, email address, and basic
            // profile. ID and basic profile are included in DEFAULT_SIGN_IN.
            var gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
                .RequestEmail()
                .RequestIdToken(ServerGoogleClientId)
                .RequestServerAuthCode(ServerGoogleClientId)
                .Build();
            // Build a GoogleSignInClient with the options specified by gso.
            _googleSignInClient = GoogleSignIn.GetClient(MainActivity, gso);
            ActivityResultCallbackRegistry.OnActivityResult += Callback;
        }

        public Task SignIn()
        {
            var signInIntent = _googleSignInClient.SignInIntent;
            MainActivity.StartActivityForResult(signInIntent, GoogleSignInRequestCode);
            return Task.CompletedTask;
        }

        public bool IsSignedIn()
        {
            // Check for existing Google Sign In account, if the user is already signed in
            // the GoogleSignInAccount will be non-null.
            var account = GoogleSignIn.GetLastSignedInAccount(MainActivity);
            return account != null;
        }

        public Task SignOut()
            => _googleSignInClient.SignOutAsync();

        private void Callback(Activity activity, int requestCode, Result resultCode, Intent? data)
        {
            if (requestCode != GoogleSignInRequestCode)
                return;
            if (activity != MainActivity)
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
            await mobileAuthClient.SignInGoogle(code).ConfigureAwait(true);
        }

        public void Dispose()
        {
            ActivityResultCallbackRegistry.OnActivityResult -= Callback;
            _googleSignInClient.Dispose();
        }
    }
}
