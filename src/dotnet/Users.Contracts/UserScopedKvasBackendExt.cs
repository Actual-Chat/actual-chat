using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Typed accessors over <see cref="UserScopedKvasBackend"/> for user-scoped settings.
/// </summary>
public static class UserScopedKvasBackendExt
{
    public static KvasAccessor<ChatUserSettings> ChatUserSettings(this UserScopedKvasBackend kvas, ChatId chatId)
        => new (kvas, Chat.ChatUserSettings.GetKey(chatId));

    public static KvasAccessor<UserEmailsSettings> UserEmailsSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserEmailsSettings>();

    public static KvasAccessor<UserAppSettings> UserAppSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserAppSettings>();

    public static KvasAccessor<UserTranscriptionEngineSettings> UserTranscriptionEngineSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserTranscriptionEngineSettings>();

    public static KvasAccessor<UserListeningSettings> UserListeningSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserListeningSettings>();

    public static KvasAccessor<UserLanguageSettings> UserLanguageSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserLanguageSettings>();

    public static KvasAccessor<UserAvatarSettings> UserAvatarSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserAvatarSettings>();

    public static KvasAccessor<UserChatRecordingDetectedLanguage> UserChatRecordingDetectedLanguage(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserChatRecordingDetectedLanguage>();

    public static KvasAccessor<UserReactionSettings> UserReactionSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserReactionSettings>();

    public static KvasAccessor<UserWalkieTalkieSettings> UserWalkieTalkieSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserWalkieTalkieSettings>();
}
