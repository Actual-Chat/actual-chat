namespace ActualChat.UI.Blazor.Services;

internal sealed class WebClientAuth : IClientAuth
{
    private readonly ClientAuthHelper _clientAuthHelper;

    public WebClientAuth(ClientAuthHelper clientAuthHelper)
        => _clientAuthHelper = clientAuthHelper;

    public ValueTask SignIn(string scheme)
        => _clientAuthHelper.SignIn(scheme);

    public ValueTask SignOut()
        => _clientAuthHelper.SignOut();

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => _clientAuthHelper.GetSchemas();
}
