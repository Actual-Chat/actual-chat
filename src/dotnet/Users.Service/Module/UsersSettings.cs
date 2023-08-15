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
    public string TwilioAccountSid { get; set; } = "";
    public string TwilioApiKey { get; set; } = "";
    public string TwilioApiSecret { get; set; } = "";
    public string TwilioSmsFrom { get; set; } = "";

    public AccountStatus NewAccountStatus { get; set; } = AccountStatus.Inactive;
    public TimeSpan TotpTimestep { get; set; } = TimeSpan.FromSeconds(30);
    public int TotpTimestepCount { get; set; } = 2;
    public int TotpRandomSecretLength { get; set; } = 32;
    public TimeSpan TotpLifetime => TotpTimestep * TotpTimestepCount;
    public bool IsTwilioEnabled => !TwilioAccountSid.IsNullOrEmpty()
        && !TwilioApiKey.IsNullOrEmpty()
        && !TwilioApiSecret.IsNullOrEmpty()
        && !TwilioSmsFrom.IsNullOrEmpty();
}
