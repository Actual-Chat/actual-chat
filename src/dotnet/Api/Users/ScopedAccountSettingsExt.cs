namespace ActualChat.Users;

/// <summary>
/// Extension methods on <see cref="ScopedAccountSettings"/> returning <see cref="AccountSettingsAccessor{T}"/>.
/// </summary>
public static partial class ScopedAccountSettingsExt
{
    public static AccountSettingsAccessor<ChatUserSettings> ChatUserSettings(
        this ScopedAccountSettings settings, ChatId chatId)
        => new(settings, Chat.ChatUserSettings.GetKey(chatId));

    public static AccountSettingsAccessor<UserEmailsSettings> UserEmailsSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserEmailsSettings));

    public static AccountSettingsAccessor<UserAppSettings> UserAppSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserAppSettings));

    public static AccountSettingsAccessor<UserTranscriptionEngineSettings> UserTranscriptionEngineSettings(
        this ScopedAccountSettings settings)
        => new(settings, nameof(UserTranscriptionEngineSettings));

    public static AccountSettingsAccessor<UserListeningSettings> UserListeningSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserListeningSettings));

    public static AccountSettingsAccessor<UserLanguageSettings> UserLanguageSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserLanguageSettings));

    public static AccountSettingsAccessor<UserAvatarSettings> UserAvatarSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserAvatarSettings));

    public static AccountSettingsAccessor<UserChatRecordingDetectedLanguage> UserChatRecordingDetectedLanguage(
        this ScopedAccountSettings settings)
        => new(settings, nameof(UserChatRecordingDetectedLanguage));

    public static AccountSettingsAccessor<UserReactionSettings> UserReactionSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserReactionSettings));

    public static AccountSettingsAccessor<UserNavbarSettings> UserNavbarSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserNavbarSettings));

    public static AccountSettingsAccessor<UserBubbleSettings> UserBubbleSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserBubbleSettings));

    public static AccountSettingsAccessor<UserOnboardingSettings> UserOnboardingSettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserOnboardingSettings));

    public static AccountSettingsAccessor<UserTranscodingTestSettings> UserTranscodingTestSettings(
        this ScopedAccountSettings settings)
        => new(settings, nameof(UserTranscodingTestSettings));

    public static AccountSettingsAccessor<UserReplaySettings> UserReplaySettings(this ScopedAccountSettings settings)
        => new(settings, nameof(UserReplaySettings));
}
