using RestEase;

namespace ActualChat.Users;

[BasePath("native-auth")]
public interface INativeAuthClient
{
    [Get("sign-in-apple")]
    Task SignInApple(string userId, string code, string? email, string? name,
        CancellationToken cancellationToken = default);

    [Get("sign-in-google")]
    Task SignInGoogle(string code, CancellationToken cancellationToken = default);
}
