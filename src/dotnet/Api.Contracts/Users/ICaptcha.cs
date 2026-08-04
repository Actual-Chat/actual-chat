using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Result of a reCAPTCHA validation request.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record RecaptchaValidationResult(
    [property: Key(0)] bool Success,
    [property: Key(1)] string? ErrorMessage = null,
    [property: Key(2)] float? Score = null);

/// <summary>
/// Service for reCAPTCHA token validation.
/// </summary>
public interface ICaptcha : IRpcService
{
    Task<RecaptchaValidationResult> Validate(string token, string action, CancellationToken cancellationToken);
}
