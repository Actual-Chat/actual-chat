namespace ActualChat.Chat;

/// <summary>
/// Legacy v2.6 IChats implementation for backward compatibility.
/// Delegates to real IChats and converts results to legacy types.
/// Remove once all clients are migrated past v2.6.
/// </summary>
#pragma warning disable CS0618 // Obsolete
public class LegacyChats(IChats chats) : ILegacyChats
{
    public virtual async Task<LegacyChatNews?> GetLegacyNews(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var news = await chats.GetNews(session, chatId, cancellationToken).ConfigureAwait(false);
        return LegacyChatNews.From(news);
    }

    public virtual async Task<LegacyChatTile> GetLegacyTile(
        Session session, ChatId chatId, int entryKind, Range<long> idTileRange, CancellationToken cancellationToken)
    {
        var tile = await chats.GetTile(session, chatId, idTileRange, cancellationToken).ConfigureAwait(false);
        return LegacyChatTile.From(tile);
    }
}
#pragma warning restore CS0618
