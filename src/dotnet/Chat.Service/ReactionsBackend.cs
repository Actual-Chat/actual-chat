using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for managing emoji reactions on chat entries.
/// </summary>
public class ReactionsBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IReactionsBackend
{
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IMaintenancesBackend MaintenancesBackend { get; } = services.GetRequiredService<IMaintenancesBackend>();

    // [ComputeMethod]
    public virtual async Task<Reaction?> Get(
        ChatEntryId entryId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var reactions = await ListByAuthor(entryId, authorId, cancellationToken).ConfigureAwait(false);
        return reactions.MaxBy(x => x.ModifiedAt);
    }

    // [ComputeMethod]
    public virtual async Task<Reaction[]> ListByAuthor(
        ChatEntryId entryId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbReactions = await dbContext.Reactions
            .Where(x => x.EntryId == entryId.Value && x.AuthorId == authorId.Value)
            .OrderBy(x => x.ModifiedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbReactions.Select(x => x.ToModel()).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ReactionSummary[]> List(
        ChatEntryId entryId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbReactionSummaries = await dbContext.ReactionSummaries
            .Where(x => x.EntryId == entryId.Value && x.Count > 0)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbReactionSummaries.Select(x => x.ToModel()).ToArray();
    }

    // [CommandHandler]
    public virtual async Task OnReact(ReactionsBackend_React command, CancellationToken cancellationToken)
    {
        var reaction = command.Reaction;
        var entryId = reaction.EntryId;
        var chatId = entryId.ChatId;
        var authorId = reaction.AuthorId;
        var emoji = reaction.Emoji;
        var context = CommandContext.GetCurrent();

        var entry = await ChatsBackend.GetEntry(entryId, cancellationToken).Require().ConfigureAwait(false);
        var entryAuthor = await AuthorsBackend.Get(chatId, entry.AuthorId, RequestedAuthorKind.Full, cancellationToken).Require().ConfigureAwait(false);
        var author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken).Require().ConfigureAwait(false);
        await MaintenancesBackend.RequireNotImporting(chatId, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Each emoji toggles on its own: the author's other reactions to the entry stay as they are
        var dbReactions = await dbContext.Reactions.ForUpdate()
            .Where(x => x.EntryId == entryId.Value && x.AuthorId == authorId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var dbReaction = dbReactions.FirstOrDefault(x => x.Emoji == emoji.Value);
        bool mustUpdateHasReactions;
        ChangeKind changeKind;
        if (dbReaction == null) {
            reaction = reaction with {
                Version = VersionGenerator.NextVersion(),
                ModifiedAt = Clocks.SystemClock.Now,
            };
            dbReaction = new DbReaction(reaction);
            dbContext.Add(dbReaction);
            var dbSummary = await UpsertDbSummary(emoji, true).ConfigureAwait(false);
            mustUpdateHasReactions = dbSummary.Count == 1;
            changeKind = ChangeKind.Create;
        }
        else {
            dbContext.Remove(dbReaction);
            var dbSummary = await UpsertDbSummary(emoji, false).ConfigureAwait(false);
            mustUpdateHasReactions = dbSummary.Count == 0;
            changeKind = ChangeKind.Remove;
            reaction = dbReaction.ToModel();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (mustUpdateHasReactions)
            await UpdateHasReactions().ConfigureAwait(false);

        Invalidation.Defer(() => {
            _ = List(entryId, default);
            _ = ListByAuthor(entryId, authorId, default);
        });

        // Raise events
        context.Operation.AddEvent(new ReactionChangedEvent(reaction, entry, entryAuthor, author, changeKind));
        return;

        async Task<DbReactionSummary> UpsertDbSummary(Emoji emoji1, bool mustIncrementCount)
        {
            var dbSummaryId = DbReactionSummary.ComposeId(entryId, emoji1.Value);
            var dbSummary = await dbContext.ReactionSummaries.ForUpdate()
                .FirstOrDefaultAsync(x => x.Id == dbSummaryId, cancellationToken)
                .ConfigureAwait(false);
            if (!mustIncrementCount)
                dbSummary.Require();

            if (dbSummary == null) {
                dbSummary = new DbReactionSummary(
                    new ReactionSummary {
                        Id = Symbol.Empty,
                        EntryId = entryId,
                        FirstAuthorIds = ImmutableList.Create(authorId),
                        Emoji = emoji1,
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
                .AnyAsync(x => x.EntryId == entryId.Value && x.Count > 0, cancellationToken)
                .ConfigureAwait(false);
            entry = entry with { HasReactions = hasReactions }; // intended, as it is used in event
            var changeEntryCommand = new ChatsBackend_ChangeEntry(
                entry.Id,
                null,
                Change.Update(new ChatEntryDiff {
                    HasReactions = hasReactions,
                }));
            await Commander.Call(changeEntryCommand, cancellationToken).ConfigureAwait(false);
            entry = entry with { HasReactions = hasReactions }; // preserving Attachments
        }
    }
}
