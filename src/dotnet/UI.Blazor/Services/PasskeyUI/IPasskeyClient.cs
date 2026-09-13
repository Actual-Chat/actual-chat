namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Platform passkey (WebAuthn) ceremonies over standard WebAuthn JSON: the same
/// options/response shapes the server produces and verifies, on every platform.
/// </summary>
public interface IPasskeyClient
{
    Task<bool> IsAvailable(CancellationToken cancellationToken);
    // Throws PasskeyCancelledException when the user dismisses the prompt
    Task<string> Create(string optionsJson, CancellationToken cancellationToken);
    Task<string> Get(string optionsJson, CancellationToken cancellationToken);
}
