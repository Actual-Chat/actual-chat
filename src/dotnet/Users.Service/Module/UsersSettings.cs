namespace ActualChat.Users.Module;

public sealed class UsersSettings
{
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
    public string SmtpFrom { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 25;
    public string SmtpLogin { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public bool SmtpUseSsl { get; set; } = false;
    public string TwilioAccountSid { get; set; } = "";
    public string TwilioApiKey { get; set; } = "";
    public string TwilioApiSecret { get; set; } = "";
    public string TwilioSmsFrom { get; set; } = "";
    public IReadOnlyDictionary<string, int> PredefinedTotps { get; set; } = ImmutableDictionary<string, int>.Empty;

    public AccountStatus NewAccountStatus { get; set; } = AccountStatus.Active;
    public TimeSpan TotpTimestep { get; set; } = TimeSpan.FromSeconds(60);
    public int TotpTimestepCount { get; set; } = 2;
    public int TotpRandomSecretLength { get; set; } = 32;
    public TimeSpan TotpLifetime => TotpTimestep * TotpTimestepCount;
    public TimeSpan TotpUIThrottling => TotpTimestep;
    public bool IsTwilioEnabled => !TwilioAccountSid.IsNullOrEmpty()
        && !TwilioApiKey.IsNullOrEmpty()
        && !TwilioApiSecret.IsNullOrEmpty()
        && !TwilioSmsFrom.IsNullOrEmpty();
}
