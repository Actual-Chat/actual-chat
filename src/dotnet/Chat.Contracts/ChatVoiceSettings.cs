using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.Chat;

public class ChatVoiceSettings(IServiceProvider services, AccountSettings accountSettings)
{
    private IChats? _chats;
    private IAuthors? _authors;

    protected IServiceProvider Services { get; } = services;
    protected IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    protected IAuthors Authors => _authors ??= Services.GetRequiredService<IAuthors>();
    protected AccountSettings AccountSettings { get; } = accountSettings;

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

        var userChatSettings = await AccountSettings
            .GetUserChatSettings(chatId, cancellationToken)
            .ConfigureAwait(false);
        userChatSettings = userChatSettings with { VoiceMode = voiceMode };
        await AccountSettings.SetUserChatSettings(chatId, userChatSettings, default).ConfigureAwait(false);
    }
}
