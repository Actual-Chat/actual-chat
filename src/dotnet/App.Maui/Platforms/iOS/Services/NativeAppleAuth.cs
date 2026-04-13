namespace ActualChat.App.Maui;

public sealed class NativeAppleAuth(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;

    public async Task SignIn(bool mustExist = false)
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
        var nativeAuthClient = Services.GetRequiredService<INativeAuthClient>();
        await nativeAuthClient.SignInApple(userId, code, email, name, mustExist).ConfigureAwait(false);
    }
}
