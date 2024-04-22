using ActualChat.Kvas;

namespace ActualChat.Users;

public static class KvasExt
{
    // UserChatSettings

    public static async ValueTask<UserChatSettings> GetUserChatSettings(this IKvas<User> kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserChatSettings>(UserChatSettings.GetKvasKey(chatId), cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserChatSettings(this IKvas<User> kvas, ChatId chatId, UserChatSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserChatSettings.GetKvasKey(chatId), value, cancellationToken);

    // UserAvatarSettings

    public static async ValueTask<UserAvatarSettings> GetUserAvatarSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserAvatarSettings>(UserAvatarSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserAvatarSettings(this IKvas<User> kvas, UserAvatarSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserAvatarSettings.KvasKey, value, cancellationToken);

    // UserLanguageSettings

    public static async ValueTask<UserLanguageSettings> GetUserLanguageSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserLanguageSettings>(UserLanguageSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserLanguageSettings(this IKvas<User> kvas, UserLanguageSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserLanguageSettings.KvasKey, value, cancellationToken);

    // UserListeningSettings

    public static async ValueTask<UserListeningSettings> GetUserListeningSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserListeningSettings>(UserListeningSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserListeningSettings(this IKvas<User> kvas, UserListeningSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserListeningSettings.KvasKey, value, cancellationToken);

    public static async Task AddContinuouslyListenedChat(this IKvas<User> kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var settings = await GetUserListeningSettings(kvas, cancellationToken).ConfigureAwait(false);
        await kvas.Set(UserListeningSettings.KvasKey, settings.Add(chatId), cancellationToken).ConfigureAwait(false);
    }

    public static async Task RemoveContinuouslyListenedChat(this IKvas<User> kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var settings = await GetUserListeningSettings(kvas, cancellationToken).ConfigureAwait(false);
        await kvas.Set(UserListeningSettings.KvasKey, settings.Remove(chatId), cancellationToken).ConfigureAwait(false);
    }

    // TranscriptionEngineSettings

    public static async ValueTask<UserTranscriptionEngineSettings> GetUserTranscriptionEngineSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserTranscriptionEngineSettings>(UserTranscriptionEngineSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserTranscriptionEngineSettings(this IKvas<User> kvas, UserTranscriptionEngineSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserTranscriptionEngineSettings.KvasKey, value, cancellationToken);
}
