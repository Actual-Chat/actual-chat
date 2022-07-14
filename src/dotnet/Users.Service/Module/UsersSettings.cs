namespace ActualChat.Users.Module;

public class UsersSettings
{
    // DBs
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";

    // Auth provider settings
    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string MicrosoftAccountClientId { get; set; } = "";
    public string MicrosoftAccountClientSecret { get; set; } = "";

    public AccountStatus NewAccountStatus { get; set; } = AccountStatus.Inactive;
}
