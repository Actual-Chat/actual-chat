using ActualChat.Kvas;

namespace ActualChat.Users;

public static class KvasExt
{
    // UserChatSettings

    public static async ValueTask<UserChatSettings> GetUserChatSettings(this IKvas kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.Get<UserChatSettings>(UserChatSettings.GetKvasKey(chatId.Value), cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserChatSettings(this IKvas kvas, ChatId chatId, UserChatSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserChatSettings.GetKvasKey(chatId.Value), value, cancellationToken);

    // UserAvatarSettings

    public static async ValueTask<UserAvatarSettings> GetUserAvatarSettings(this IKvas kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.Get<UserAvatarSettings>(UserAvatarSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserAvatarSettings(this IKvas kvas, UserAvatarSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserAvatarSettings.KvasKey, value, cancellationToken);

    // UserLanguageSettings

    public static async ValueTask<UserLanguageSettings> GetUserLanguageSettings(this IKvas kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.Get<UserLanguageSettings>(UserLanguageSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserLanguageSettings(this IKvas kvas, UserLanguageSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserLanguageSettings.KvasKey, value, cancellationToken);
}
