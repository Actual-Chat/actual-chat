using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.Chat;

public class VoiceSettings(IChats chats, IAuthors authors, IKvas<User> kvas)
{
    public async Task<VoiceModeSettings> GetVoiceMode(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new VoiceModeSettings(false, false);

        var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.IsAnonymous)
            return new VoiceModeSettings(false, false);

        var userChatSettings = await kvas.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        var mustStreamVoice = userChatSettings.VoiceMode is not VoiceMode.JustText;
        return new VoiceModeSettings(mustStreamVoice, true);
    }

    public async Task SetVoiceMode(Session session, ChatId chatId, bool value, CancellationToken cancellationToken)
    {
        var mustRecordVoice = await GetVoiceMode(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!mustRecordVoice.CanChange)
            throw StandardError.Constraint("Cannot change must record voice");

        var userChatSettings = await kvas.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        userChatSettings = userChatSettings with { VoiceMode = value ? VoiceMode.TextAndVoice : VoiceMode.JustText };
        await kvas.SetUserChatSettings(chatId, userChatSettings, default).ConfigureAwait(false);
    }

    public record VoiceModeSettings(bool MustStreamVoice, bool CanChange);
}
