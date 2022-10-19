using ActualChat.Kvas;

namespace ActualChat.Users;

public static class KvasExt
{
    public static async ValueTask<UserLanguageSettings> GetUserLanguageSettings(this IKvas kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.Get<UserLanguageSettings>(UserLanguageSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserLanguageSettings(this IKvas kvas, UserLanguageSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserLanguageSettings.KvasKey, value, cancellationToken);
}
