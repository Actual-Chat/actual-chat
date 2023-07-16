using RestEase;

namespace ActualChat.Users;

[BasePath("native-auth")]
public interface INativeAuthClient
{
    [Get("sign-in-apple")]
    Task SignInApple(
        string sessionId, string userId, string code, string? email, string? name,
        CancellationToken cancellationToken = default);

    [Get("sign-in-google")]
    Task SignInGoogle(string sessionId, string code, CancellationToken cancellationToken = default);
}
