using ActualChat.Kvas;

namespace ActualChat.Chat;

/// <summary>
/// Extension methods for <see cref="ChatUserSettings"/>.
/// </summary>
public static class ChatUserSettingsExt
{
    public static Task<Language> LanguageOrPrimary(
        this ChatUserSettings chatUserSettings, IKvas<Account> kvas,
        CancellationToken cancellationToken = default)
        => chatUserSettings.Language is { } language
            ? Task.FromResult(language)
            : kvas.UserLanguageSettings().Get(x => x.Primary, cancellationToken);
}
