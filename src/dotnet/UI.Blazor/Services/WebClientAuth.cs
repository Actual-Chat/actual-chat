namespace ActualChat.UI.Blazor.Services;

internal sealed class WebClientAuth : IClientAuth
{
    private ClientAuthHelper ClientAuthHelper { get; }
    private NavigationManager Nav { get; }

    public WebClientAuth(IServiceProvider services)
    {
        ClientAuthHelper = services.GetRequiredService<ClientAuthHelper>();
        Nav = services.GetRequiredService<NavigationManager>();
    }

    public async ValueTask SignIn(string scheme)
    {
        await ClientAuthHelper.SignIn(scheme);
        Nav.NavigateTo(Links.Chat(default), true);
    }

    public ValueTask SignOut()
        => ClientAuthHelper.SignOut();

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ClientAuthHelper.GetSchemas();
}
