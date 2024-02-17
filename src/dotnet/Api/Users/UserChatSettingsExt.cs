using ActualChat.Kvas;

namespace ActualChat.Users;

public static class UserChatSettingsExt
{
    public static async ValueTask<Language> LanguageOrPrimary(
        this UserChatSettings userChatSettings, IKvas<User> kvas,
        CancellationToken cancellationToken = default)
    {
        var language = userChatSettings.Language;
        if (!language.IsNone)
            return language;

        var userLanguageSettings = await kvas.GetUserLanguageSettings(cancellationToken).ConfigureAwait(false);
        return userLanguageSettings.Primary;
    }
}
