namespace ActualChat.Users;

/// <summary>
/// Extension methods on <see cref="UserSettingsUI"/> returning <see cref="UserSettingsAccessor{T}"/>.
/// </summary>
public static class UserSettingsUIExt
{
    public static UserSettingsAccessor<ChatUserSettings> ChatUserSettings(
        this UserSettingsUI settingsUI, ChatId chatId)
        => new(settingsUI, Chat.ChatUserSettings.GetKey(chatId));

    public static UserSettingsAccessor<UserEmailsSettings> UserEmailsSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserEmailsSettings));

    public static UserSettingsAccessor<UserAppSettings> UserAppSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserAppSettings));

    public static UserSettingsAccessor<UserTranscriptionEngineSettings> UserTranscriptionEngineSettings(
        this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserTranscriptionEngineSettings));

    public static UserSettingsAccessor<UserListeningSettings> UserListeningSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserListeningSettings));

    public static UserSettingsAccessor<UserLanguageSettings> UserLanguageSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserLanguageSettings));

    public static UserSettingsAccessor<UserAvatarSettings> UserAvatarSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserAvatarSettings));

    public static UserSettingsAccessor<UserChatRecordingDetectedLanguage> UserChatRecordingDetectedLanguage(
        this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserChatRecordingDetectedLanguage));

    public static UserSettingsAccessor<UserReactionSettings> UserReactionSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserReactionSettings));

    public static UserSettingsAccessor<UserNavbarSettings> UserNavbarSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserNavbarSettings));

    public static UserSettingsAccessor<UserBubbleSettings> UserBubbleSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserBubbleSettings));

    public static UserSettingsAccessor<UserOnboardingSettings> UserOnboardingSettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserOnboardingSettings));

    public static UserSettingsAccessor<UserTranscodingTestSettings> UserTranscodingTestSettings(
        this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserTranscodingTestSettings));

    public static UserSettingsAccessor<UserReplaySettings> UserReplaySettings(this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserReplaySettings));

    public static UserSettingsAccessor<UserPttSettings> UserPttSettings(
        this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserPttSettings));

    public static UserSettingsAccessor<UserCarAudioSettings> UserCarAudioSettings(
        this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserCarAudioSettings));
}
