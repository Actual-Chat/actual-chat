using ActualChat.Kvas;

namespace ActualChat.Users;

public static class KvasExt
{
    public static KvasAccessor<UserChatSettings> UserChatSettings(this IKvas<User> kvas, ChatId chatId)
        => new (kvas, Users.UserChatSettings.GetKvasKey(chatId));

    public static KvasAccessor<UserEmailsSettings> UserEmailsSettings(this IKvas<User> kvas)
        => kvas.For<UserEmailsSettings>();

    public static KvasAccessor<UserAppSettings> UserAppSettings(this IKvas<User> kvas)
        => kvas.For<UserAppSettings>();

    public static KvasAccessor<LocalAppSettings> LocalAppSettings(this IKvas<User> kvas)
        => kvas.For<LocalAppSettings>();

    public static KvasAccessor<UserTranscriptionEngineSettings> UserTranscriptionEngineSettings(this IKvas<User> kvas)
        => kvas.For<UserTranscriptionEngineSettings>();

    public static KvasAccessor<UserListeningSettings> UserListeningSettings(this IKvas<User> kvas)
        => kvas.For<UserListeningSettings>();

    public static KvasAccessor<UserLanguageSettings> UserLanguageSettings(this IKvas<User> kvas)
        => kvas.For<UserLanguageSettings>();

    public static KvasAccessor<UserAvatarSettings> UserAvatarSettings(this IKvas<User> kvas)
        => kvas.For<UserAvatarSettings>();
}
