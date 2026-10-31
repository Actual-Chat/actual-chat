using ActualChat.Security;

namespace ActualChat.App.Maui;

public sealed class NativeAppleAuth(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;
    private ICommander Commander { get; } = services.Commander();
    private TrueSessionResolver SessionResolver { get; } = services.GetRequiredService<TrueSessionResolver>();

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
        var session = await SessionResolver.GetSession().ConfigureAwait(false);
        var command = new NativeAuth_SignInApple {
            Session = session,
            UserId = userId,
            Code = code,
            Email = email,
            Name = name,
        };
        await Commander.Call(command, true).ConfigureAwait(false);
    }
}
