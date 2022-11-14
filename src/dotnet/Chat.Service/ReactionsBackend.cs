using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Commands;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

internal class ReactionsBackend : DbServiceBase<ChatDbContext>, IReactionsBackend
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private IChatsBackend ChatsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }

    public ReactionsBackend(IServiceProvider services) : base(services)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Reaction?> Get(string entryId, string authorId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbReaction = await dbContext.Reactions.Get(DbReaction.ComposeId(entryId, authorId), cancellationToken)
            .ConfigureAwait(false);
        return dbReaction?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ReactionSummary>> List(
        string entryId,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbReactionSummaries = await dbContext.ReactionSummaries
            .Where(x => x.EntryId == entryId && x.Count > 0)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbReactionSummaries.Select(x => x.ToModel()).ToImmutableArray();
    }

    // [CommandHandler]
    public virtual async Task React(IReactionsBackend.ReactCommand command, CancellationToken cancellationToken)
    {
        var reaction = command.Reaction;
        var chatEntryId = reaction.EntryId;
        var chatId = chatEntryId.ChatId;
        var authorId = reaction.AuthorId;
        if (Computed.IsInvalidating()) {
            _ = List(chatEntryId, default);
            _ = Get(chatEntryId, authorId, default);
            return;
        }

        var entry = await GetChatEntry(chatId, chatEntryId.LocalId, cancellationToken).Require().ConfigureAwait(false);
        var entryAuthor = await AuthorsBackend.Get(chatId, entry.AuthorId, cancellationToken).Require().ConfigureAwait(false);
        var author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).Require().ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbReaction = await dbContext.Reactions
            .Get(DbReaction.ComposeId(chatEntryId, authorId), cancellationToken)
            .ConfigureAwait(false);
        var needsHasReactionsUpdate = true;
        var changeKind = ChangeKind.Create;
        if (dbReaction == null) {
            reaction = reaction with {
                Version = VersionGenerator.NextVersion(),
                ModifiedAt = Clocks.SystemClock.Now,
            };
            dbReaction = new DbReaction(command.Reaction);
            dbContext.Add(dbReaction);
            var dbSummary = await UpsertDbSummary(reaction.Emoji, true).ConfigureAwait(false);
            if (dbSummary.Count > 1)
                needsHasReactionsUpdate = false; // there were already reaction before;
        }
        else {
            var dbSummary = await UpsertDbSummary(dbReaction.Emoji, false).ConfigureAwait(false);
            if (dbSummary.Count > 0)
                needsHasReactionsUpdate = false; // there are still some reactions left

            if (dbReaction.Emoji.Equals(reaction.Emoji, StringComparison.OrdinalIgnoreCase))
                dbContext.Remove(dbReaction);
            else {
                dbReaction.Version = VersionGenerator.NextVersion(dbReaction.Version);
                dbReaction.Emoji = reaction.Emoji;
                dbReaction.ModifiedAt = Clocks.SystemClock.Now;
                dbSummary = await UpsertDbSummary(reaction.Emoji, true).ConfigureAwait(false);
                if (dbSummary.Count > 1)
                    needsHasReactionsUpdate = false; // There were already reaction before
            }
            reaction = dbReaction.ToModel();
            changeKind = ChangeKind.Update;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (needsHasReactionsUpdate)
            await UpdateHasReactions().ConfigureAwait(false);

        new ReactionChangedEvent(reaction, entry, entryAuthor, author, changeKind)
            .EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId));

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
                    EntryId = chatEntryId,
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
                .AnyAsync(x => x.EntryId == chatEntryId.Value && x.Count > 0, cancellationToken)
                .ConfigureAwait(false);
            entry = entry with { HasReactions = hasReactionsAfter };
            entry = await Commander.Call(new IChatsBackend.UpsertEntryCommand(entry), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ChatEntry?> GetChatEntry(string chatId, long entryId, CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.FirstLayer.GetTile(entryId);
        var chatTile = await ChatsBackend.GetTile(
                chatId,
                ChatEntryKind.Text,
                idTile.Range,
                false,
                cancellationToken)
            .ConfigureAwait(false);
        var entry = chatTile.Entries.FirstOrDefault(x => x.Id == entryId);
        return entry;
    }
}
