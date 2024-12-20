using ActualChat.Roulette;

namespace ActualChat.Chat;

internal static class RouletteExt
{
    public static async Task<ChatRouletteId> GetChatRouletteId(
        ChatId chatId,
        IAuthorsBackend authorsBackend,
        CancellationToken cancellationToken)
    {
        var authorId1 = new AuthorId(chatId, 1, AssumeValid.Option);
        var authorId2 = new AuthorId(chatId, 2, AssumeValid.Option);
        var author1 = await authorsBackend.Get(chatId, authorId1, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
        var author2 = await authorsBackend.Get(chatId, authorId2, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
        if (author1 is null || author2 is null)
            return ChatRouletteId.None;

        var profileId1 = author1.AvatarId;
        var profileId2 = author2.AvatarId;
        var chatRouletteId = new ChatRouletteId(profileId1, profileId2);
        return chatRouletteId;
    }
}
