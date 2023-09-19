namespace ActualChat.UI.Blazor.Services;

internal sealed class WebClientAuth(IServiceProvider services) : IClientAuth
{
    private ClientAuthHelper? _clientAuthHelper;

    private IServiceProvider Services { get; } = services;
    private ClientAuthHelper ClientAuthHelper => _clientAuthHelper ??= Services.GetRequiredService<ClientAuthHelper>();

    public Task SignIn(string schema)
        => ClientAuthHelper.SignIn(schema).AsTask();

    public Task SignOut()
        => ClientAuthHelper.SignOut().AsTask();

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ClientAuthHelper.GetSchemas();
}
