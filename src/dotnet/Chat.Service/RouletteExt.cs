using ActualChat.Roulette;

namespace ActualChat.Chat;

internal static class RouletteExt
{
    public static async Task<ChatRouletteId> GetChatRouletteId(
        ChatId chatId,
        IAuthorsBackend authorsBackend,
        CancellationToken cancellationToken)
    {
        var authorId1 = AuthorId.New(chatId, 1);
        var authorId2 = AuthorId.New(chatId, 2);
        var author1 = await authorsBackend.Get(chatId, authorId1, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
        var author2 = await authorsBackend.Get(chatId, authorId2, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
        if (author1 is null || author2 is null)
            return ChatRouletteId.None;

        var profileId1 = author1.AvatarId;
        var profileId2 = author2.AvatarId;
        var chatRouletteId = new ChatRouletteId(profileId1, profileId2);
        return chatRouletteId;
    }
}
