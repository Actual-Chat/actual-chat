namespace ActualChat.UI.Blazor.Services;

internal sealed class WebClientAuth : IClientAuth
{
    private ClientAuthHelper ClientAuthHelper { get; }

    public WebClientAuth(IServiceProvider services)
        => ClientAuthHelper = services.GetRequiredService<ClientAuthHelper>();

    public async ValueTask SignIn(string schema)
        => await ClientAuthHelper.SignIn(schema);

    public ValueTask SignOut()
        => ClientAuthHelper.SignOut();

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ClientAuthHelper.GetSchemas();
}
