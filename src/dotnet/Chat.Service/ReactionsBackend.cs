using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

internal class ReactionsBackend : DbServiceBase<ChatDbContext>, IReactionsBackend
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private IChatsBackend ChatsBackend { get; }

    public ReactionsBackend(IServiceProvider services, IChatsBackend chatsBackend) : base(services)
        => ChatsBackend = chatsBackend;

    // [ComputeMethod]
    public virtual async Task<Reaction?> Get(Symbol chatEntryId, Symbol chatAuthorId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbReaction = await dbContext.Reactions.Get(DbReaction.ComposeId(chatEntryId, chatAuthorId), cancellationToken)
            .ConfigureAwait(false);
        return dbReaction?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ReactionSummary>> List(
        string chatEntryId,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbReactionSummaries = await dbContext.ReactionSummaries
            .Where(x => x.ChatEntryId == chatEntryId && x.Count > 0)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbReactionSummaries.Select(x => x.ToModel()).ToImmutableArray();
    }

    // [CommandHandler]
    public virtual async Task React(IReactionsBackend.ReactCommand command, CancellationToken cancellationToken)
    {
        var (authorId, chatId, entryId, newEmoji) = command.Reaction;
        var chatEntryId = new ParsedChatEntryId(chatId, ChatEntryType.Text, entryId).ToString();
        if (Computed.IsInvalidating()) {
            _ = List(chatEntryId, default);
            _ = Get(chatEntryId, authorId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbReaction = await dbContext.Reactions
            .Get(DbReaction.ComposeId(chatEntryId, authorId), cancellationToken)
            .ConfigureAwait(false);
        var needsHasReactionsUpdate = true;
        if (dbReaction == null) {
            dbReaction = new DbReaction(command.Reaction) {
                Version = VersionGenerator.NextVersion(),
                ModifiedAt = Clocks.SystemClock.Now,
            };
            dbContext.Add(dbReaction);
            var dbSummary = await UpsertDbSummary(newEmoji, true).ConfigureAwait(false);
            if (dbSummary.Count > 1)
                needsHasReactionsUpdate = false; // there were already reaction before
        }
        else {
            var dbSummary = await UpsertDbSummary(dbReaction.Emoji, false).ConfigureAwait(false);
            if (dbSummary.Count > 0)
                needsHasReactionsUpdate = false; // there are still some reactions left

            if (dbReaction.Emoji.Equals(newEmoji, StringComparison.OrdinalIgnoreCase))
                dbContext.Remove(dbReaction);
            else {
                dbReaction.Emoji = newEmoji;
                dbReaction.Version = VersionGenerator.NextVersion(dbReaction.Version);
                dbReaction.ModifiedAt = Clocks.SystemClock.Now;
                dbSummary = await UpsertDbSummary(newEmoji, true).ConfigureAwait(false);
                if (dbSummary.Count > 1)
                    needsHasReactionsUpdate = false; // there were already reaction before
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (needsHasReactionsUpdate)
            await UpdateHasReactions().ConfigureAwait(false);

        ValueTask<DbReactionSummary?> GetDbSummary(string emoji)
        {
            var id = DbReactionSummary.ComposeId(chatEntryId, emoji);
            return dbContext.ReactionSummaries.Get(id, cancellationToken);
        }

        async Task<DbReactionSummary> UpsertDbSummary(string emoji, bool increase)
        {
            var dbSummary = await GetDbSummary(emoji).ConfigureAwait(false);
            if (!increase)
                dbSummary = dbSummary.Require();

            if (dbSummary == null) {
                dbSummary = new DbReactionSummary(new ReactionSummary {
                    ChatEntryId = chatEntryId,
                    FirstAuthorIds = ImmutableList.Create(authorId),
                    Emoji = emoji,
                    Count = 1,
                    Version = VersionGenerator.NextVersion(),
                });
                dbContext.Add(dbSummary);
            }
            else {
                var summary = dbSummary.ToModel();
                summary = increase ? summary.Increase() : summary.Decrease();
                if (increase)
                    summary = summary.AddAuthor(authorId);
                else {
                    summary = summary.RemoveAuthor(authorId);
                    // TODO: use reaction authors ordered by modified date.
                }

                dbSummary.UpdateFrom(summary);
                dbSummary.Version = VersionGenerator.NextVersion(summary.Version);
            }
            return dbSummary;
        }

        async Task UpdateHasReactions()
        {
            var hasReactionsAfter = await dbContext.ReactionSummaries
                .AnyAsync(x => x.ChatEntryId == chatEntryId && x.Count > 0, cancellationToken)
                .ConfigureAwait(false);
            var idTile = IdTileStack.FirstLayer.GetTile(entryId);
            var chatTile = await ChatsBackend.GetTile(
                    chatId,
                    ChatEntryType.Text,
                    idTile.Range,
                    false,
                    cancellationToken)
                .ConfigureAwait(false);
            var entry = chatTile.Entries.First(x => x.Id == entryId);
            entry = entry with { HasReactions = hasReactionsAfter };
            await Commander.Call(new IChatsBackend.UpsertEntryCommand(entry), cancellationToken).ConfigureAwait(false);
        }
    }
}
