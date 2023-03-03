namespace ActualChat.UI.Blazor.Services;

internal sealed class WebClientAuth : IClientAuth
{
    private ClientAuthHelper ClientAuthHelper { get; }

    public WebClientAuth(IServiceProvider services)
    {
        ClientAuthHelper = services.GetRequiredService<ClientAuthHelper>();
    }

    public async ValueTask SignIn(string scheme)
        => await ClientAuthHelper.SignIn(scheme);

    public ValueTask SignOut()
        => ClientAuthHelper.SignOut();

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ClientAuthHelper.GetSchemas();
}
