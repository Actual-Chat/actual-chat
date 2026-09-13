namespace ActualChat.UI.Blazor.Services;

public sealed class UnavailablePasskeyClient : IPasskeyClient
{
    private const string NotSupportedMessage = "Passkeys aren't available on this platform.";

    public Task<bool> IsAvailable(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<string> Create(string optionsJson, CancellationToken cancellationToken)
        => throw StandardError.NotSupported(NotSupportedMessage);

    public Task<string> Get(string optionsJson, CancellationToken cancellationToken)
        => throw StandardError.NotSupported(NotSupportedMessage);
}
