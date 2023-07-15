using ActualChat.App.Maui.Services;

namespace ActualChat.App.Maui;

public sealed class NativeAppleSignIn
{
    private readonly MobileAuthClient _mobileAuthClient;

    public NativeAppleSignIn(MobileAuthClient mobileAuthClient)
        => _mobileAuthClient = mobileAuthClient;

    public async Task SignIn()
    {
        var options = new AppleSignInAuthenticator.Options() {
            IncludeEmailScope = true,
            IncludeFullNameScope = true,
        };
        var result = await AppleSignInAuthenticator.AuthenticateAsync(options).ConfigureAwait(false);
        var code = result.Properties["authorization_code"];
        var email = result.Properties["email"];
        var name = result.Properties["name"];
        var userId = result.Properties["user_id"];
        await _mobileAuthClient.SignInAppleWithCode(code, name, email, userId).ConfigureAwait(true);
    }
}
