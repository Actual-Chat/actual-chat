namespace ActualChat.UI.Blazor.Services;

public interface IClientAuth
{
    [Obsolete("Not really, but you should use AccountUI instead!")]
    (string Name, string DisplayName)[] GetSchemas();
    [Obsolete("Not really, but you should use AccountUI instead!")]
    Task SignIn(string schema);
    [Obsolete("Not really, but you should use AccountUI instead!")]
    Task SignOut();
}
