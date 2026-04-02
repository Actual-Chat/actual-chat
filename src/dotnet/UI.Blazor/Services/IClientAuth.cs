namespace ActualChat.UI.Blazor.Services;

public interface IClientAuth
{
    [Obsolete("Don't call this method directly, use AccountUI instead!")]
    (string Name, string DisplayName)[] GetSchemas();
    [Obsolete("Don't call this method directly, use AccountUI instead!")]
    Task SignIn(string schema, bool mustExist = false);
    [Obsolete("Don't call this method directly, use AccountUI instead!")]
    Task SignOut();
}
