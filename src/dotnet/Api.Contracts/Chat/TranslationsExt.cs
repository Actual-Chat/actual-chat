namespace ActualChat.Chat;

public static class TranslationsExt
{
    public static async Task<ChatEntryLanguage?> GetLanguage(
        this ITranslations chats,
        Session session,
        ChatEntryId entryId,
        CancellationToken cancellationToken = default)
    {
        try {
            var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(entryId.LocalId);
            var tile = await chats.GetLanguageTile(session,
                    entryId.ChatId,
                    entryId.Kind,
                    idTile.Range,
                    cancellationToken)
                .ConfigureAwait(false);
            var entry = tile.Entries.SingleOrDefault(e => e is not null && e.Id.LocalId == entryId.LocalId);
            return entry;
        }
        catch (NotFoundException) {
            return null;
        }
    }
}
