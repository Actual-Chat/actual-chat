using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Extension methods for <see cref="UserChatSettings"/>.
/// </summary>
public static class UserChatSettingsExt
{
    public static Task<Language> LanguageOrPrimary(
        this UserChatSettings userChatSettings, IKvas<Account> kvas,
        CancellationToken cancellationToken = default)
        => userChatSettings.Language is { } language
            ? Task.FromResult(language)
            : kvas.UserLanguageSettings().Get(x => x.Primary, cancellationToken);
}
