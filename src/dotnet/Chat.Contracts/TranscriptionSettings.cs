using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.Chat;

public class TranscriptionSettings(IChats chats, IAuthors authors, IKvas<User> kvas)
{
    public async Task<PersistVoice> GetPersistVoice(Session session, ChatId chatId, CancellationToken  cancellationToken)
    {
        var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new PersistVoice(false, false);
        var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.IsAnonymous)
            return new PersistVoice(false, false);
        var userChatSettings = await kvas.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        var mustRecordVoice = userChatSettings.PersistVoice.GetValueOrDefault(true);
        return new PersistVoice(mustRecordVoice, true);
    }

    public async Task SetPersistVoice(Session session, ChatId chatId, bool value, CancellationToken cancellationToken)
    {
        var mustRecordVoice = await GetPersistVoice(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!mustRecordVoice.CanChange)
            throw StandardError.Constraint("Cannot change must record voice");
        var userChatSettings = await kvas.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        userChatSettings = userChatSettings with { PersistVoice = value };
        await kvas.SetUserChatSettings(chatId, userChatSettings, default).ConfigureAwait(false);
    }

    public record PersistVoice(bool Value, bool CanChange);
}
