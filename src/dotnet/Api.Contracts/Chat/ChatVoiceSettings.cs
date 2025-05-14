using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.Chat;

// NOTE(AY): Convert this to a scoped service, but see how its current constructor is used
public sealed class ChatVoiceSettings(IServiceProvider services, AccountSettings accountSettings)
{
    private IServiceProvider Services { get; } = services;
    [field: AllowNull, MaybeNull]
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private AccountSettings AccountSettings { get; } = accountSettings;

    public async Task<ChatVoiceMode> Get(Session session, ChatId chatId, CancellationToken cancellationToken = default)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new ChatVoiceMode(chatId, VoiceMode.JustText, false);

        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.IsAnonymous)
            return new ChatVoiceMode(chatId, VoiceMode.JustText, false);

        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return new ChatVoiceMode(chatId, userChatSettings.VoiceMode, true);
    }

    public async Task Set(Session session, ChatId chatId, VoiceMode voiceMode, CancellationToken cancellationToken = default)
    {
        var chatVoiceMode = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!chatVoiceMode.CanChange)
            throw StandardError.Constraint("Voice streaming mode cannot be changed in this chat.");

        await AccountSettings.UpdateUserChatSettings(chatId, x => x with { VoiceMode = voiceMode }, CancellationToken.None).ConfigureAwait(false);
    }
}
