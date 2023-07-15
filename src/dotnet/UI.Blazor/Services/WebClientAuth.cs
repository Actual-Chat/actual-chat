namespace ActualChat.UI.Blazor.Services;

internal sealed class WebClientAuth : IClientAuth
{
    private ClientAuthHelper? _clientAuthHelper;

    private IServiceProvider Services { get; }
    private ClientAuthHelper ClientAuthHelper => _clientAuthHelper ??= Services.GetRequiredService<ClientAuthHelper>();

    public WebClientAuth(IServiceProvider services)
        => Services = services;

    public async Task SignIn(string schema)
        => await ClientAuthHelper.SignIn(schema);

    public async Task SignOut()
        => await ClientAuthHelper.SignOut();

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ClientAuthHelper.GetSchemas();
}
