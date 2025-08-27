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
        => await kvas.Get<UserAvatarSettings>(cancellationToken).ConfigureAwait(false) ?? new ();

    public static Task SetUserAvatarSettings(this IKvas<User> kvas, UserAvatarSettings value, CancellationToken cancellationToken)
        => kvas.Set(value, cancellationToken);

    // UserLanguageSettings

    public static async ValueTask<UserLanguageSettings> GetUserLanguageSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
        => await kvas.Get<UserLanguageSettings>(cancellationToken).ConfigureAwait(false) ?? new ();

    public static Task SetUserLanguageSettings(this IKvas<User> kvas, UserLanguageSettings value, CancellationToken cancellationToken)
        => kvas.Set(value, cancellationToken);

    // UserListeningSettings

    public static async ValueTask<UserListeningSettings> GetUserListeningSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
        => await kvas.Get<UserListeningSettings>(cancellationToken).ConfigureAwait(false) ?? new ();

    public static Task SetUserListeningSettings(this IKvas<User> kvas, UserListeningSettings value, CancellationToken cancellationToken)
        => kvas.Set(value, cancellationToken);

    public static Task<UserListeningSettings> UpdateUserListeningSettings(
        this IKvas<User> kvas, Func<UserListeningSettings, UserListeningSettings> updater, CancellationToken cancellationToken)
        => kvas.Update(updater, cancellationToken);

    // TranscriptionEngineSettings

    public static async ValueTask<UserTranscriptionEngineSettings> GetUserTranscriptionEngineSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
        => await kvas.Get<UserTranscriptionEngineSettings>(cancellationToken).ConfigureAwait(false) ?? new ();

    public static Task SetUserTranscriptionEngineSettings(this IKvas<User> kvas, UserTranscriptionEngineSettings value, CancellationToken cancellationToken)
        => kvas.Set(value, cancellationToken);

    // UserAppSettings

    public static async ValueTask<UserAppSettings> GetUserAppSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
        => await kvas.Get<UserAppSettings>(cancellationToken).ConfigureAwait(false) ?? new UserAppSettings();

    public static Task SetUserAppSettings(this IKvas<User> kvas, UserAppSettings value, CancellationToken cancellationToken)
        => kvas.Set(value, cancellationToken);

    public static Task UpdateUserAppSettings(this IKvas<User> kvas, Func<UserAppSettings, UserAppSettings> update, CancellationToken cancellationToken = default)
        => kvas.Update(update, cancellationToken);

    // UserEmailsSettings

    public static async Task<UserEmailsSettings> GetUserEmailsSettings(
        this IKvas<User> kvas,
        CancellationToken cancellationToken)
        => await kvas.Get<UserEmailsSettings>(cancellationToken).ConfigureAwait(false) ?? new ();

    public static Task SetUserEmailsSettings(
        this IKvas<User> kvas,
        UserEmailsSettings value,
        CancellationToken cancellationToken)
        => kvas.Set(value, cancellationToken);
}
