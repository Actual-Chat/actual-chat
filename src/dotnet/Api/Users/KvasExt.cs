using System.Text.RegularExpressions;
using ActualChat.Kvas;

namespace ActualChat.Users;

public static partial class KvasExt
{
    // UserChatSettings

    [GeneratedRegex($@"^\((?<{nameof(ChatId)}>[a-zA-Z0-9-]+)\)$")]
    private static partial Regex UserChatSettingsRegexFactory();
    private static readonly Regex UserChatSettingsRegex = UserChatSettingsRegexFactory();

    public static async ValueTask<UserChatSettings> GetUserChatSettings(this IKvas<User> kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserChatSettings>(UserChatSettings.GetKvasKey(chatId), cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static async ValueTask<IReadOnlyList<(ChatId ChatId, UserChatSettings Value)>> ListUserChatSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var settingItems = await kvas.List<UserChatSettings>(UserChatSettings.Prefix, cancellationToken).ConfigureAwait(false);
        return settingItems
            .Select(tuple => (ParseChatId(tuple.Key), tuple.Value))
            .ToList();

        static ChatId ParseChatId(string tupleKey)
        {
            var matches = UserChatSettingsRegex.Matches(tupleKey);
            return matches.Count == 0
                ? ChatId.None
                : ChatId.ParseOrNone(matches[0].Groups[nameof(ChatId)].Value);
        }
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

    // TranscriptionEngineSettings

    public static async ValueTask<UserTranscriptionEngineSettings> GetUserTranscriptionEngineSettings(this IKvas<User> kvas, CancellationToken cancellationToken)
    {
        var valueOpt = await kvas.TryGet<UserTranscriptionEngineSettings>(UserTranscriptionEngineSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        return valueOpt.IsSome(out var value) ? value : new();
    }

    public static Task SetUserTranscriptionEngineSettings(this IKvas<User> kvas, UserTranscriptionEngineSettings value, CancellationToken cancellationToken)
        => kvas.Set(UserTranscriptionEngineSettings.KvasKey, value, cancellationToken);
}
