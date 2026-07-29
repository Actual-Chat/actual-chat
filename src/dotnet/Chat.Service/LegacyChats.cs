using ActualChat.Logging;

namespace ActualChat.Chat;

/// <summary>
/// Server-side adapter that delegates legacy v2.7 chat RPC calls to the modern
/// <see cref="IChats"/> service and projects the responses to the v2.7 wire shapes
/// (<see cref="LegacyChatEntry"/>, <see cref="LegacyChatNews"/>, <see cref="LegacyChatTile"/>).
/// </summary>
public class LegacyChats(IServiceProvider services) : ILegacyChats
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log { get; } = services.LogFor<LegacyChats>();

    public virtual async Task<LegacyChatNews?> GetNews(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyChats.GetNews), session, cancellationToken).ConfigureAwait(false);
#pragma warning disable CS0618 // v2.7- clients expect the full LastTextEntry, so slim GetNews doesn't fit here
        var news = await Chats.GetFullNews(session, chatId, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618
        return LegacyChatNews.From(news);
    }

    public virtual async Task<LegacyChatTile> GetTile(
        Session session, ChatId chatId, Range<long> lidTileRange, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyChats.GetTile), session, cancellationToken).ConfigureAwait(false);
        var tile = await Chats.GetTile(session, chatId, lidTileRange, cancellationToken).ConfigureAwait(false);
        return LegacyChatTile.From(tile);
    }

    public virtual async Task<LegacyChatTile> GetTile(
        Session session, ChatId chatId, int entryKind, Range<long> idTileRange, CancellationToken cancellationToken)
    {
        await LogUsage(
                $"{nameof(ILegacyChats.GetTile)}(entryKind)",
                session,
                cancellationToken)
            .ConfigureAwait(false);
        var tile = await Chats.GetTile(session, chatId, idTileRange, cancellationToken).ConfigureAwait(false);
        return LegacyChatTile.From(tile);
    }

    public virtual async Task<LegacyChatEntry> OnUpsertEntry(
        LegacyChats_UpsertEntry command, CancellationToken cancellationToken)
    {
        await LogUsage(nameof(ILegacyChats.OnUpsertEntry), command.Session, cancellationToken).ConfigureAwait(false);
        var entry = await Commander.Call(command.ToModern(), true, cancellationToken).ConfigureAwait(false);
        return LegacyChatEntry.From(entry);
    }

    // Private methods

    private async ValueTask LogUsage(string method, Session session, CancellationToken cancellationToken)
    {
        var entryPoint = $"{nameof(ILegacyChats)}.{method}";
        string? clientInfo = null;
        try {
            var sessionInfo = await Accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
            clientInfo = sessionInfo?.Description;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to resolve client version for legacy API {EntryPoint}", entryPoint);
        }
        Log.LogLegacyApiUsage(entryPoint, session, clientInfo);
    }
}
