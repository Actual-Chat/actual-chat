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
    public virtual async Task<Reaction?> Get(TextEntryId entryId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var id = DbReaction.ComposeId(entryId, authorId);
        var dbReaction = await dbContext.Reactions.Get(id, cancellationToken)
            .ConfigureAwait(false);
        return dbReaction?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ReactionSummary>> List(
        TextEntryId entryId,
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
        var entryId = reaction.EntryId;
        var chatId = entryId.ChatId;
        var authorId = reaction.AuthorId;

        if (Computed.IsInvalidating()) {
            _ = List(entryId, default);
            _ = Get(entryId, authorId, default);
            return;
        }

        var emoji = Emoji.Get(reaction.EmojiId).Require();
        var entry = await GetChatEntry(entryId, cancellationToken).Require().ConfigureAwait(false);
        var entryAuthor = await AuthorsBackend.Get(chatId, entry.AuthorId, cancellationToken).Require().ConfigureAwait(false);
        var author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).Require().ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbReaction = await dbContext.Reactions
            .Get(DbReaction.ComposeId(entryId, authorId), cancellationToken)
            .ConfigureAwait(false);
        var mustUpdateHasReactions = true;
        var changeKind = ChangeKind.Create;

        if (dbReaction == null) {
            reaction = reaction with {
                Version = VersionGenerator.NextVersion(),
                ModifiedAt = Clocks.SystemClock.Now,
            };
            dbReaction = new DbReaction(reaction);
            dbContext.Add(dbReaction);

            var dbSummary = await UpsertDbSummary(emoji, true).ConfigureAwait(false);
            if (dbSummary.Count > 1)
                mustUpdateHasReactions = false; // There were already reaction before
        }
        else {
            if (emoji.Id == dbReaction.EmojiId) {
                dbContext.Remove(dbReaction);
                var dbSummary = await UpsertDbSummary(emoji, false).ConfigureAwait(false);
                if (dbSummary.Count > 0)
                    mustUpdateHasReactions = false; // Some reactions are still there
            }
            else {
                var oldEmoji = Emoji.Get(dbReaction.EmojiId);
                dbReaction.Version = VersionGenerator.NextVersion(dbReaction.Version);
                dbReaction.EmojiId = emoji.Id;
                dbReaction.ModifiedAt = Clocks.SystemClock.Now;
                await UpsertDbSummary(oldEmoji, false).ConfigureAwait(false);
                await UpsertDbSummary(emoji, true).ConfigureAwait(false);
                mustUpdateHasReactions = false; // Author replaced one reaction with another
            }

            reaction = dbReaction.ToModel();
            changeKind = ChangeKind.Update;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (mustUpdateHasReactions)
            await UpdateHasReactions().ConfigureAwait(false);

        new ReactionChangedEvent(reaction, entry, entryAuthor, author, changeKind)
            .EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId));

        async Task<DbReactionSummary> UpsertDbSummary(Emoji emoji1, bool mustIncrementCount)
        {
            var dbSummaryId = DbReactionSummary.ComposeId(entryId, emoji1);
            var dbSummary = await dbContext.ReactionSummaries.ForUpdate()
                .SingleOrDefaultAsync(x => x.Id == dbSummaryId, cancellationToken)
                .ConfigureAwait(false);
            if (!mustIncrementCount)
                dbSummary.Require();

            if (dbSummary == null) {
                dbSummary = new DbReactionSummary(new ReactionSummary {
                    EntryId = entryId,
                    FirstAuthorIds = ImmutableList.Create(authorId),
                    EmojiId = emoji1.Id,
                    Count = 1,
                    Version = VersionGenerator.NextVersion(),
                });
                dbContext.Add(dbSummary);
            }
            else {
                var summary = dbSummary.ToModel();
                summary = summary.IncrementCount(mustIncrementCount ? 1 : -1);
                if (mustIncrementCount)
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
            var hasReactions = await dbContext.ReactionSummaries
                .AnyAsync(x => x.EntryId == entryId && x.Count > 0, cancellationToken)
                .ConfigureAwait(false);
            entry = entry with { HasReactions = hasReactions };
            entry = await Commander.Call(new IChatsBackend.UpsertEntryCommand(entry), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ChatEntry?> GetChatEntry(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.FirstLayer.GetTile(entryId.LocalId);
        var chatTile = await ChatsBackend.GetTile(
                entryId.ChatId,
                entryId.Kind,
                idTile.Range,
                false,
                cancellationToken)
            .ConfigureAwait(false);
        var entry = chatTile.Entries.FirstOrDefault(x => x.LocalId == entryId.LocalId);
        return entry;
    }
}
