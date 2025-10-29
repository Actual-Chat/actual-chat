using ActualChat.Kvas;

namespace ActualChat.Users;

public static class UserChatSettingsExt
{
    public static Task<Language> LanguageOrPrimary(
        this UserChatSettings userChatSettings, IKvas<User> kvas,
        CancellationToken cancellationToken = default)
        => userChatSettings.Language is { } language
            ? Task.FromResult(language)
            : kvas.UserLanguageSettings().Get(x => x.Primary, cancellationToken);
}
