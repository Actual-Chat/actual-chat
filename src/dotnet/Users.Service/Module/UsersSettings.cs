namespace ActualChat.Users.Module;

public class UsersSettings
{
    // DBs
    public string Db { get; set; } = "";

    // Auth provider settings
    public string MicrosoftAccountClientId { get; set; } = "";
    public string MicrosoftAccountClientSecret { get; set; } = "";
    public string GitHubClientId { get; set; } = "";
    public string GitHubClientSecret { get; set; } = "";
}
