namespace ActualChat.Chat;

public static class UserSettingsUIExt
{
    public static async Task<ChatVoiceMode> GetChatVoiceMode(
        this UserSettingsUI userSettingsUI,
        ChatId chatId,
        CancellationToken cancellationToken = default)
    {
        chatId = chatId.GetThreadOutermostParentOrSelf();
        var session = userSettingsUI.Session;
        var services = userSettingsUI.Services;
        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new ChatVoiceMode(chatId, VoiceMode.JustText, false);

        var authors = services.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.IsAnonymous)
            return new ChatVoiceMode(chatId, VoiceMode.JustText, false);

        var voiceMode = await userSettingsUI
            .ChatUserSettings(chatId).Get(x => x.VoiceMode, cancellationToken)
            .ConfigureAwait(false);
        return new ChatVoiceMode(chatId, voiceMode, true);
    }

    public static async Task SetChatVoiceMode(
        this UserSettingsUI userSettingsUI,
        ChatId chatId,
        VoiceMode voiceMode,
        CancellationToken cancellationToken = default)
    {
        chatId = chatId.GetThreadOutermostParentOrSelf();
        var chatVoiceMode = await userSettingsUI
            .GetChatVoiceMode(chatId, cancellationToken)
            .ConfigureAwait(false);
        if (!chatVoiceMode.CanChange)
            throw StandardError.Constraint("Voice streaming mode cannot be changed in this chat.");

        await userSettingsUI
            .ChatUserSettings(chatId).Update(x => x with { VoiceMode = voiceMode }, cancellationToken)
            .ConfigureAwait(false);
    }

    public static Task<ListeningMode> GetListeningMode(
        this UserSettingsUI userSettingsUI,
        ChatId chatId,
        CancellationToken cancellationToken = default)
        => userSettingsUI.ChatUserSettings(chatId).Get(x => x.ListeningMode, cancellationToken);

    public static Task SetListeningMode(
        this UserSettingsUI userSettingsUI,
        ChatId chatId,
        ListeningMode listeningMode,
        CancellationToken cancellationToken = default)
        => userSettingsUI.ChatUserSettings(chatId).Update(x => x with { ListeningMode = listeningMode }, cancellationToken);
}
