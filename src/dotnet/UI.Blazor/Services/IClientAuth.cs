namespace ActualChat.UI.Blazor.Services;

public interface IClientAuth
{
    public const string GoogleSchemeName = Constants.Auth.Google.SchemeName;
    public const string AppleIdSchemeName = Constants.Auth.Apple.SchemeName;
    public const string PhoneSchemeName = Constants.Auth.Phone.SchemeName;

    ValueTask SignIn(string scheme);
    ValueTask SignOut();
    ValueTask<(string Name, string DisplayName)[]> GetSchemas();
}
