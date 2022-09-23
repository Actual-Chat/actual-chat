namespace ActualChat.UI.Blazor.Services;

internal sealed class WebClientAuth : IClientAuth
{
    private ClientAuthHelper ClientAuthHelper { get; }

    public WebClientAuth(ClientAuthHelper clientAuthHelper)
        => ClientAuthHelper = clientAuthHelper;

    public ValueTask SignIn(string scheme)
        => ClientAuthHelper.SignIn(scheme);

    public ValueTask SignOut()
        => ClientAuthHelper.SignOut();

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ClientAuthHelper.GetSchemas();
}
