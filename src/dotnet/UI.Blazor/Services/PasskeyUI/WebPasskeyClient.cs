using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class WebPasskeyClient(UIHub hub) : IPasskeyClient
{
    private const string CancelledError = "cancelled";
    private static readonly string JsClassName = $"{BlazorUICoreModule.ImportName}.Passkeys";

    private IJSRuntime JS { get; } = hub.JS;

    public Task<bool> IsAvailable(CancellationToken cancellationToken)
        => JS.InvokeAsync<bool>($"{JsClassName}.isAvailable", cancellationToken).AsTask();

    public Task<string> Create(string optionsJson, CancellationToken cancellationToken)
        => Run("create", optionsJson, cancellationToken);

    public Task<string> Get(string optionsJson, CancellationToken cancellationToken)
        => Run("get", optionsJson, cancellationToken);

    // Private methods

    private async Task<string> Run(string method, string optionsJson, CancellationToken cancellationToken)
    {
        var result = await JS.InvokeAsync<Result>($"{JsClassName}.{method}", cancellationToken, optionsJson)
            .ConfigureAwait(false);
        if (result.Error == CancelledError)
            throw new PasskeyCancelledException();
        if (!result.Error.IsNullOrEmpty() || result.Json.IsNullOrEmpty())
            throw StandardError.External(result.Error ?? "Passkey ceremony returned nothing.");

        return result.Json;
    }

    // Nested types

    private sealed record Result(string? Json, string? Error);
}
