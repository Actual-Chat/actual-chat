namespace ActualChat.Users;

/// <summary>
/// Extension methods on <see cref="AccountSettingsUI"/> returning <see cref="AccountSettingsAccessor{T}"/>.
/// </summary>
public static class AccountSettingsUIExt
{
    public static AccountSettingsAccessor<ChatUserSettings> ChatUserSettings(
        this AccountSettingsUI settingsUI, ChatId chatId)
        => new(settingsUI, Chat.ChatUserSettings.GetKey(chatId));

    public static AccountSettingsAccessor<UserEmailsSettings> UserEmailsSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserEmailsSettings));

    public static AccountSettingsAccessor<UserAppSettings> UserAppSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserAppSettings));

    public static AccountSettingsAccessor<UserTranscriptionEngineSettings> UserTranscriptionEngineSettings(
        this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserTranscriptionEngineSettings));

    public static AccountSettingsAccessor<UserListeningSettings> UserListeningSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserListeningSettings));

    public static AccountSettingsAccessor<UserLanguageSettings> UserLanguageSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserLanguageSettings));

    public static AccountSettingsAccessor<UserAvatarSettings> UserAvatarSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserAvatarSettings));

    public static AccountSettingsAccessor<UserChatRecordingDetectedLanguage> UserChatRecordingDetectedLanguage(
        this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserChatRecordingDetectedLanguage));

    public static AccountSettingsAccessor<UserReactionSettings> UserReactionSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserReactionSettings));

    public static AccountSettingsAccessor<UserNavbarSettings> UserNavbarSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserNavbarSettings));

    public static AccountSettingsAccessor<UserBubbleSettings> UserBubbleSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserBubbleSettings));

    public static AccountSettingsAccessor<UserOnboardingSettings> UserOnboardingSettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserOnboardingSettings));

    public static AccountSettingsAccessor<UserTranscodingTestSettings> UserTranscodingTestSettings(
        this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserTranscodingTestSettings));

    public static AccountSettingsAccessor<UserReplaySettings> UserReplaySettings(this AccountSettingsUI settingsUI)
        => new(settingsUI, nameof(UserReplaySettings));
}
