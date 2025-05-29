using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public static class TemporalsExt
{
    // UserChatSettings

    public static async ValueTask<UserChatSettings> GetUserChatSettings(
        this Temporals temporals, ChatId chatId, CancellationToken cancellationToken)
    {
        var kvasKey = UserChatSettings.GetKvasKey(chatId);
        var value = await temporals.Get<UserChatSettings>(kvasKey).ConfigureAwait(false);
        value ??= await temporals.Hub.AccountSettings.Get<UserChatSettings>(kvasKey, cancellationToken).ConfigureAwait(false);
        return value ?? new();
    }

    public static Task SetUserChatSettings(
        this Temporals temporals, ChatId chatId, UserChatSettings value, CancellationToken cancellationToken)
    {
        var kvasKey = UserChatSettings.GetKvasKey(chatId);
        temporals.Set(kvasKey, value);
        return temporals.Hub.AccountSettings.Set(kvasKey, value, cancellationToken);
    }

    public static async Task<UserChatSettings> UpdateUserChatSettings(
        this Temporals temporals, ChatId chatId, Func<UserChatSettings, UserChatSettings> updater, CancellationToken cancellationToken)
    {
        var settings = await temporals.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        settings = updater.Invoke(settings);
        await temporals.SetUserChatSettings(chatId, settings, cancellationToken).ConfigureAwait(false);
        return settings;
    }
}
