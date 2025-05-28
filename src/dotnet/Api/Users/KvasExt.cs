using ActualChat.Kvas;

namespace ActualChat.Users;

public static class KvasExt
{
    // UserChatSettings

    public static async ValueTask<UserChatSettings> GetUserChatSettings(this IKvas<User> kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var value = await kvas.Get<UserChatSettings>(UserChatSettings.GetKvasKey(chatId), cancellationToken).ConfigureAwait(false);
        return value ?? new();
    }

    public static Task SetUserChatSettings(this IKvas<User> kvas, ChatId chatId, UserChatSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserChatSettings.GetKvasKey(chatId), value, cancellationToken);

    public static async Task<UserChatSettings> UpdateUserChatSettings(
        this IKvas<User> kvas, ChatId chatId, Func<UserChatSettings, UserChatSettings> updater, CancellationToken cancellationToken)
    {
        var settings = await kvas.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        settings = updater.Invoke(settings);
        await kvas.SetUserChatSettings(chatId, settings, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    // UserAvatarSettings

    public static async ValueTask<UserAvatarSettings> GetUserAvatarSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var value = await kvas.Get<UserAvatarSettings>(UserAvatarSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return value ?? new();
    }

    public static Task SetUserAvatarSettings(this IKvas<User> kvas, UserAvatarSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserAvatarSettings.KvasKey, value, cancellationToken);

    // UserLanguageSettings

    public static async ValueTask<UserLanguageSettings> GetUserLanguageSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var value = await kvas.Get<UserLanguageSettings>(UserLanguageSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return value ?? new();
    }

    public static Task SetUserLanguageSettings(this IKvas<User> kvas, UserLanguageSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserLanguageSettings.KvasKey, value, cancellationToken);

    // UserListeningSettings

    public static async ValueTask<UserListeningSettings> GetUserListeningSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var value = await kvas.Get<UserListeningSettings>(UserListeningSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return value ?? new();
    }

    public static Task SetUserListeningSettings(this IKvas<User> kvas, UserListeningSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserListeningSettings.KvasKey, value, cancellationToken);

    public static async Task<UserListeningSettings> UpdateUserListeningSettings(
        this IKvas<User> kvas, Func<UserListeningSettings, UserListeningSettings> updater, CancellationToken cancellationToken)
    {
        var settings = await GetUserListeningSettings(kvas, cancellationToken).ConfigureAwait(false);
        settings = updater.Invoke(settings);
        await kvas.SetUserListeningSettings(settings, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    // TranscriptionEngineSettings

    public static async ValueTask<UserTranscriptionEngineSettings> GetUserTranscriptionEngineSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var value = await kvas.Get<UserTranscriptionEngineSettings>(UserTranscriptionEngineSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return value ?? new();
    }

    public static Task SetUserTranscriptionEngineSettings(this IKvas<User> kvas, UserTranscriptionEngineSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserTranscriptionEngineSettings.KvasKey, value, cancellationToken);

    // UserAppSettings

    public static async ValueTask<UserAppSettings> GetUserAppSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var value = await kvas.Get<UserAppSettings>(UserAppSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return value ?? new();
    }

    public static Task SetUserAppSettings(this IKvas<User> kvas, UserAppSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserAppSettings.KvasKey, value, cancellationToken);

    // UserEmailsSettings

    public static async ValueTask<UserEmailsSettings> GetUserEmailsSettings(
        this IKvas<User> kvas,
        CancellationToken cancellationToken)
    {
        var value = await kvas.Get<UserEmailsSettings>(UserEmailsSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return value ?? new();
    }

    public static Task SetUserEmailsSettings(
        this IKvas<User> kvas,
        UserEmailsSettings value,
        CancellationToken cancellationToken)
        => kvas.Set(UserEmailsSettings.KvasKey, value, cancellationToken);
}
