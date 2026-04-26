namespace ActualChat.Chat;

/// <summary>
/// Server-side adapter that delegates legacy v2.7 chat RPC calls to the modern
/// <see cref="IChats"/> service and projects the responses to the v2.7 wire shapes
/// (<see cref="LegacyChatEntry"/>, <see cref="LegacyChatNews"/>, <see cref="LegacyChatTile"/>).
/// </summary>
public class LegacyChats(IServiceProvider services) : ILegacyChats
{
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<LegacyChatNews?> GetNews(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var news = await Chats.GetNews(session, chatId, cancellationToken).ConfigureAwait(false);
        return LegacyChatNews.From(news);
    }

    public virtual async Task<LegacyChatTile> GetTile(
        Session session, ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken)
    {
        var tile = await Chats.GetTile(session, chatId, idTileRange, cancellationToken).ConfigureAwait(false);
        return LegacyChatTile.From(tile);
    }

    public virtual async Task<LegacyChatEntry> OnUpsertEntry(
        Chats_UpsertEntry command, CancellationToken cancellationToken)
    {
        var entry = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return LegacyChatEntry.From(entry);
    }
}
