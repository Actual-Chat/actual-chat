namespace ActualChat.UI.Blazor.Services;

public interface IClientAuth
{
    ValueTask SignIn(string scheme);
    ValueTask SignOut();
    ValueTask<(string Name, string DisplayName)[]> GetSchemas();
}
