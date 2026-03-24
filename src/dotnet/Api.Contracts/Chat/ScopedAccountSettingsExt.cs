namespace ActualChat.Chat;

public static class ChatAccountSettingsExt
{
    public static async Task<ChatVoiceMode> GetChatVoiceMode(
        this AccountSettingsUI accountSettingsUI,
        ChatId chatId,
        CancellationToken cancellationToken = default)
    {
        chatId = chatId.GetThreadOutermostParentOrSelf();
        var session = accountSettingsUI.Session;
        var services = accountSettingsUI.Services;
        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new ChatVoiceMode(chatId, VoiceMode.JustText, false);

        var authors = services.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.IsAnonymous)
            return new ChatVoiceMode(chatId, VoiceMode.JustText, false);

        var voiceMode = await accountSettingsUI
            .ChatUserSettings(chatId).Get(x => x.VoiceMode, cancellationToken)
            .ConfigureAwait(false);
        return new ChatVoiceMode(chatId, voiceMode, true);
    }

    public static async Task SetChatVoiceMode(
        this AccountSettingsUI accountSettingsUI,
        ChatId chatId,
        VoiceMode voiceMode,
        CancellationToken cancellationToken = default)
    {
        chatId = chatId.GetThreadOutermostParentOrSelf();
        var chatVoiceMode = await accountSettingsUI
            .GetChatVoiceMode(chatId, cancellationToken)
            .ConfigureAwait(false);
        if (!chatVoiceMode.CanChange)
            throw StandardError.Constraint("Voice streaming mode cannot be changed in this chat.");

        await accountSettingsUI
            .ChatUserSettings(chatId).Update(x => x with { VoiceMode = voiceMode }, cancellationToken)
            .ConfigureAwait(false);
    }

    public static Task<ListeningMode> GetListeningMode(
        this AccountSettingsUI accountSettingsUI,
        ChatId chatId,
        CancellationToken cancellationToken = default)
        => accountSettingsUI.ChatUserSettings(chatId).Get(x => x.ListeningMode, cancellationToken);

    public static Task SetListeningMode(
        this AccountSettingsUI accountSettingsUI,
        ChatId chatId,
        ListeningMode listeningMode,
        CancellationToken cancellationToken = default)
        => accountSettingsUI.ChatUserSettings(chatId).Update(x => x with { ListeningMode = listeningMode }, cancellationToken);
}
