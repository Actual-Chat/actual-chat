namespace ActualChat.Users.Module;

public sealed class UsersSettings
{
    // DBs
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";

    // Auth provider settings
    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string MicrosoftAccountClientId { get; set; } = "";
    public string MicrosoftAccountClientSecret { get; set; } = "";
    public string AppleClientId { get; set; } = "";
    public string AppleAppId { get; set; } = "";
    public string? AppleKeyId { get; set; } = "";
    public string AppleTeamId { get; set; } = "";
    public string ApplePrivateKeyPath { get; set; } = "";

    public AccountStatus NewAccountStatus { get; set; } = AccountStatus.Inactive;
}
