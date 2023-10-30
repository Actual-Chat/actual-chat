using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.Chat;

public class TranscriptionSettings(IChats chats, IAuthors authors, IKvas<User> kvas)
{
    public async Task<MustRecordVoice> GetMustRecordVoice(Session session, ChatId chatId, CancellationToken  cancellationToken)
    {
        var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new MustRecordVoice(false, false);
        var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.IsAnonymous)
            return new MustRecordVoice(false, false);
        var userChatSettings = await kvas.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        var mustRecordVoice = userChatSettings.MustRecordVoice.GetValueOrDefault(true);
        return new MustRecordVoice(mustRecordVoice, true);
    }

    public async Task SetMustRecordVoice(Session session, ChatId chatId, bool value, CancellationToken cancellationToken)
    {
        var mustRecordVoice = await GetMustRecordVoice(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!mustRecordVoice.CanChange)
            throw StandardError.Constraint("Cannot change must record voice");
        var userChatSettings = await kvas.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        userChatSettings = userChatSettings with { MustRecordVoice = value };
        await kvas.SetUserChatSettings(chatId, userChatSettings, default).ConfigureAwait(false);
    }

    public record MustRecordVoice(bool Value, bool CanChange);
}
