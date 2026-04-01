using RestEase;

namespace ActualChat.Users;

/// <summary>
/// HTTP client interface for native (iOS/Android) authentication flows.
/// </summary>
[BasePath("native-auth")]
public interface INativeAuthClient
{
    [Get("sign-in-apple")]
    Task SignInApple(
        string userId, string code, string? email, string? name, bool? mustExist = null,
        CancellationToken cancellationToken = default);

    [Get("sign-in-google")]
    Task SignInGoogle(string code, bool? mustExist = null, CancellationToken cancellationToken = default);
}
