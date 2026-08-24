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
    public string AppleAppId { get; set; } = "";
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
    public string TelegramGatewayToken { get; set; } = "";
    public TimeSpan? TelegramGatewayTtl { get; set; }
    public string BlockedPhonePrefixes { get; set; } = "";
    public string SkipTelegramPhonePrefixes { get; set; } = "";
    public IReadOnlyDictionary<string, int> PredefinedTotps { get; set; } = ImmutableDictionary<string, int>.Empty;
    // A kill switch: MauiAuthController.Start assumes every browser component the app can reach
    // reports Sec-Fetch-Site: none. Turn this off if some platform turns out not to.
    public bool IsMauiAuthFetchSiteCheckEnabled { get; set; } = true;
    public AccountStatus NewAccountStatus { get; set; } = AccountStatus.Active;
    public TimeSpan TotpCodeLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public int TotpMaxAttemptCount { get; set; } = 5;
    public TimeSpan TotpUIThrottling => TotpCodeLifetime.Clamp(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
    public bool IsTwilioEnabled => !TwilioAccountSid.IsNullOrEmpty()
        && !TwilioApiKey.IsNullOrEmpty()
        && !TwilioApiSecret.IsNullOrEmpty()
        && !TwilioSmsFrom.IsNullOrEmpty();
    public bool IsSmtpEnabled => !SmtpHost.IsNullOrEmpty()
        && !SmtpFrom.IsNullOrEmpty();
    public bool IsSMSToEnabled => !SMSToApiKey.IsNullOrEmpty()
        && !SMSToFrom.IsNullOrEmpty();

    public bool IsTelegramGatewayEnabled => !TelegramGatewayToken.IsNullOrEmpty();
    // Unset means the message lives exactly as long as the code it carries. Telegram Gateway rejects
    // anything outside 30s..1h and refunds the fee for a message it couldn't deliver within the ttl.
    public TimeSpan TelegramGatewayMessageTtl
        => (TelegramGatewayTtl ?? TotpCodeLifetime).Clamp(TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));
}
