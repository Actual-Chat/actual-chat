using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// KVAS (key-value async store) extension methods for user settings.
/// </summary>
public static class KvasExt
{
    public static KvasAccessor<UserChatSettings> UserChatSettings(this IKvas<Account> kvas, ChatId chatId)
        => new (kvas, Users.UserChatSettings.GetKvasKey(chatId));

    public static KvasAccessor<UserEmailsSettings> UserEmailsSettings(this IKvas<Account> kvas)
        => kvas.For<UserEmailsSettings>();

    public static KvasAccessor<UserAppSettings> UserAppSettings(this IKvas<Account> kvas)
        => kvas.For<UserAppSettings>();

    public static KvasAccessor<LocalAppSettings> LocalAppSettings(this IKvas kvas)
        => kvas.For<LocalAppSettings>();

    public static KvasAccessor<UserTranscriptionEngineSettings> UserTranscriptionEngineSettings(this IKvas<Account> kvas)
        => kvas.For<UserTranscriptionEngineSettings>();

    public static KvasAccessor<UserListeningSettings> UserListeningSettings(this IKvas<Account> kvas)
        => kvas.For<UserListeningSettings>();

    public static KvasAccessor<UserLanguageSettings> UserLanguageSettings(this IKvas<Account> kvas)
        => kvas.For<UserLanguageSettings>();

    public static KvasAccessor<UserAvatarSettings> UserAvatarSettings(this IKvas<Account> kvas)
        => kvas.For<UserAvatarSettings>();

    public static KvasAccessor<UserChatRecordingDetectedLanguage> UserChatRecordingDetectedLanguage(this IKvas<Account> kvas)
        => kvas.For<UserChatRecordingDetectedLanguage>();

    public static KvasAccessor<UserReactionSettings> UserReactionSettings(this IKvas<Account> kvas)
        => kvas.For<UserReactionSettings>();
}
