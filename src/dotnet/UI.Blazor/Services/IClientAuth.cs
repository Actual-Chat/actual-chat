namespace ActualChat.UI.Blazor.Services;

public interface IClientAuth
{
    ValueTask SignIn(string scheme);
}
