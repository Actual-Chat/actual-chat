namespace ActualChat.Users.Module;

public sealed class UsersSettings
{
    public string AvatarPicturesCacheDir { get; set; } = "";
    public int AvatarPicturesCacheCapacity { get; set; } = 10000;
    // Auth provider settings
    public string GoogleClientId { get; set; } = "CannotBeEmptyString";
    public string GoogleClientSecret { get; set; } = "";
    public string GoogleRecaptchaSiteKey { get; set; } = "";
    public string MicrosoftAccountClientId { get; set; } = "CannotBeEmptyString";
    public string MicrosoftAccountClientSecret { get; set; } = "";
    public string AppleClientId { get; set; } = "CannotBeEmptyString";
    public string? AppleKeyId { get; set; } = "CannotBeEmptyString";
    public string AppleTeamId { get; set; } = "CannotBeEmptyString";
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
    public string SMSToApiKey { get; set; } = "";
    public string SMSToFrom { get; set; } = "SMSto";
    public string BlockedPhonePrefixes { get; set; } = "";
    public IReadOnlyDictionary<string, int> PredefinedTotps { get; set; } = ImmutableDictionary<string, int>.Empty;

    public AccountStatus NewAccountStatus { get; set; } = AccountStatus.Active;
    public TimeSpan TotpTimestep { get; set; } = TimeSpan.FromSeconds(60 * 15);
    public int TotpTimestepCount { get; set; } = 2;
    public int TotpRandomSecretLength { get; set; } = 32;
    public TimeSpan TotpLifetime => TotpTimestep * TotpTimestepCount;
    public TimeSpan TotpUIThrottling => TotpTimestep.Clamp(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
    public bool IsTwilioEnabled => !TwilioAccountSid.IsNullOrEmpty()
        && !TwilioApiKey.IsNullOrEmpty()
        && !TwilioApiSecret.IsNullOrEmpty()
        && !TwilioSmsFrom.IsNullOrEmpty();

    public bool IsSmtpEnabled => !SmtpHost.IsNullOrEmpty()
        && !SmtpFrom.IsNullOrEmpty();

    public bool IsSMSToEnabled => !SMSToApiKey.IsNullOrEmpty()
        && !SMSToFrom.IsNullOrEmpty();
}
