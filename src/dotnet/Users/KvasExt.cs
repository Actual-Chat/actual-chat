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

    // UserOnboardingSettings

    public static async ValueTask<UserOnboardingSettings> GetOnboardingSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserOnboardingSettings>(UserOnboardingSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetOnboardingSettings(this IKvas<User> kvas, UserOnboardingSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserOnboardingSettings.KvasKey, value, cancellationToken);

    // TranscriptionEngineSettings

    public static async ValueTask<UserTranscriptionEngineSettings> GetUserTranscriptionEngineSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserTranscriptionEngineSettings>(UserTranscriptionEngineSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserTranscriptionEngineSettings(this IKvas<User> kvas, UserTranscriptionEngineSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserTranscriptionEngineSettings.KvasKey, value, cancellationToken);
}
