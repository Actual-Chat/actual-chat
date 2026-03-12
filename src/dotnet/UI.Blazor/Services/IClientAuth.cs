namespace ActualChat.UI.Blazor.Services;

public interface IClientAuth
{
    [Obsolete("Don't call this method directly, use AccountUI instead!")]
    (string Name, string DisplayName)[] GetSchemas();
    [Obsolete("Don't call this method directly, use AccountUI instead!")]
    Task<string?> SignIn(string schema, bool isRegister = false);
    [Obsolete("Don't call this method directly, use AccountUI instead!")]
    Task SignOut();
}
