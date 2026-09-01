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
            var idTile = Constants.Chat.EntryIdTileLayer.GetTile(entryId.LocalId);
            var tile = await chats.GetLanguageTile(session,
                    entryId.ChatId,
                    idTile.Range,
                    cancellationToken)
                .ConfigureAwait(false);
            var entry = tile.Entries.SingleOrDefault(e => e.Id.LocalId == entryId.LocalId);
            return entry;
        }
        catch (NotFoundException) {
            return null;
        }
    }
}
