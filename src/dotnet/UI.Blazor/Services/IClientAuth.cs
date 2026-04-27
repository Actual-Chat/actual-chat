namespace ActualChat.UI.Blazor.Services;

public interface IClientAuth
{
    [Obsolete("2025.05: Don't call this method directly, use AccountUI instead!")]
    (string Name, string DisplayName)[] GetSchemas();
    [Obsolete("2025.05: Don't call this method directly, use AccountUI instead!")]
    Task SignIn(string schema, bool mustExist = false);
    [Obsolete("2025.05: Don't call this method directly, use AccountUI instead!")]
    Task SignOut();
}
