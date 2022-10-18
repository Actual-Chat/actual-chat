using ActualChat.Kvas;

namespace ActualChat.Users;

public static class KvasExt
{
    public static async ValueTask<LanguageUserSettings> GetLanguageSettings(this IKvas kvas, CancellationToken cancellationToken = default)
    {
        var (hasValue, value) = await kvas.Get<LanguageUserSettings>(LanguageUserSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return hasValue ? value : throw StandardError.Constraint("No language settings found.");
    }

    public static Task SetLanguageSettings(
        this IKvas kvas,
        LanguageUserSettings value,
        CancellationToken cancellationToken = default)
        => kvas.Set(LanguageUserSettings.KvasKey, value, cancellationToken);
}
