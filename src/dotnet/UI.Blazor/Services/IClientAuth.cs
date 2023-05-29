namespace ActualChat.UI.Blazor.Services;

public interface IClientAuth
{
    public const string GoogleSchemeName = "Google";
    public const string FacebookSchemeName = "Facebook";
    public const string AppleIdSchemeName = "Apple";

    ValueTask SignIn(string scheme);
    ValueTask SignOut();
    ValueTask<(string Name, string DisplayName)[]> GetSchemas();
}
