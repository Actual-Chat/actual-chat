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
    // Keys are the lowercase <prefix> of <prefix>+<suffix>@actual.chat.
    // Unlike PredefinedTotps, these are never honored on production.
    public IReadOnlyDictionary<string, int> PredefinedEmailTotps { get; set; } = ImmutableDictionary<string, int>.Empty;
    public AppUpdateSettings AppUpdates { get; set; } = new();
    public ReviewPromptSettings ReviewPrompt { get; set; } = new();
    // A kill switch: MauiAuthController.Start assumes every browser component the app can reach
    // reports Sec-Fetch-Site: none. Turn this off if some platform turns out not to.
    public bool IsMauiAuthFetchSiteCheckEnabled { get; set; } = true;
    public AccountStatus NewAccountStatus { get; set; } = AccountStatus.Active;
    // Null = on everywhere except production; set explicitly to override
    public bool? IsPasskeyAuthEnabled { get; set; }
    // Empty = the public host of HostInfo.BaseUrl
    public string PasskeyRpId { get; set; } = "";
    // ';'-separated; empty = the origin of HostInfo.BaseUrl. Android app origins look like
    // android:apk-key-hash:<base64url(sha256(signing cert))>
    public string PasskeyOrigins { get; set; } = "";
    public TimeSpan PasskeyChallengeLifetime { get; set; } = TimeSpan.FromMinutes(2);
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

    public string GetPasskeyRpId(HostInfo hostInfo)
        => PasskeyRpId.IsNullOrEmpty()
            ? hostInfo.BaseUrl.EnsureSuffix("/").ToUri().Host
            : PasskeyRpId;

    public HashSet<string> GetPasskeyOrigins(HostInfo hostInfo)
    {
        var origins = PasskeyOrigins
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();
        if (origins.Count == 0)
            origins.Add(hostInfo.BaseUrl.EnsureSuffix("/").ToUri().GetLeftPart(UriPartial.Authority));

        return origins;
    }
}

/// <summary>
/// When the app may ask for a store review: the usage a user must have behind them, and how
/// prompts are spaced once one was shown.
/// </summary>
public sealed class ReviewPromptSettings
{
    public TimeSpan MinAccountAge { get; set; } = TimeSpan.FromDays(3);
    public int MinActiveDays { get; set; } = 3;
    public TimeSpan MinSpeechDuration { get; set; } = TimeSpan.FromMinutes(5);
    public int MinLiveSessions { get; set; } = 1;
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromDays(60);
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromDays(30);
    public int MaxDeclines { get; set; } = 2;
    // A pending prompt older than this is dropped: a backgrounded app must not pop it up hours later
    public TimeSpan PendingTtl { get; set; } = TimeSpan.FromMinutes(10);
}
