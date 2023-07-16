using ActualChat.Users;

namespace ActualChat.App.Maui;

public sealed class NativeAppleAuth
{
    private IServiceProvider Services { get; }

    public NativeAppleAuth(IServiceProvider services)
        => Services = services;

    public async Task SignIn()
    {
        var options = new AppleSignInAuthenticator.Options() {
            IncludeEmailScope = true,
            IncludeFullNameScope = true,
        };
        var result = await AppleSignInAuthenticator.AuthenticateAsync(options).ConfigureAwait(false);

        var sessionId = Services.Session().Id.Value;
        var code = result.Properties["authorization_code"];
        var email = result.Properties["email"];
        var name = result.Properties["name"];
        var userId = result.Properties["user_id"];
        var nativeAuthClient = Services.GetRequiredService<INativeAuthClient>();
        await nativeAuthClient.SignInApple(sessionId, code, name, email, userId).ConfigureAwait(false);
    }
}
