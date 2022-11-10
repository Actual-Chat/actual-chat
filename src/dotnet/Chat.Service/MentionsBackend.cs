using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Commands;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

internal class MentionsBackend : DbServiceBase<ChatDbContext>, IMentionsBackend
{
    private IMarkupParser MarkupParser { get; }

    public MentionsBackend(IServiceProvider services, IMarkupParser markupParser) : base(services)
        => MarkupParser = markupParser;

    // [ComputeMethod]
    public virtual async Task<Mention?> GetLast(
        Symbol chatId,
        Symbol authorId,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbMention = await dbContext.Mentions
            .Where(x => x.ChatId == chatId.Value && x.AuthorId == authorId.Value)
            .OrderByDescending(x => x.EntryId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbMention?.ToModel();
    }

    // Events

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent @event, CancellationToken cancellationToken)
    {
        var (entry, author, changeKind) = @event;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChangedAuthorIds = context.Operation().Items.Get<HashSet<Symbol>>();
            if (invChangedAuthorIds != null) {
                foreach (var authorId1 in invChangedAuthorIds)
                    _ = GetLast(entry.ChatId, authorId1, default);
            }
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var markup = MarkupParser.Parse(entry.Content);
        var authorIds = new MentionExtractor().GetMentionedAuthorIds(markup);
        var existingMentions = await dbContext.Mentions
            .Where(x => x.ChatId == entry.ChatId.Value && x.EntryId == entry.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changedAuthorIds = new HashSet<Symbol>();
        if (changeKind is ChangeKind.Remove) {
            dbContext.Mentions.RemoveRange(existingMentions);
            changedAuthorIds.AddRange(existingMentions.Select(m => (Symbol)m.AuthorId));
        }
        else {
            var toRemove = existingMentions.ExceptBy(authorIds, x => x.Id).ToList();
            dbContext.Mentions.RemoveRange(toRemove);

            var toAdd = authorIds
                .Except(existingMentions.Select(x => (Symbol)x.AuthorId))
                .Select(authorId1 => new DbMention {
                    Id = DbMention.ComposeId(entry.ChatId, entry.Id, authorId1),
                    AuthorId = authorId1,
                    ChatId = entry.ChatId,
                    EntryId = entry.Id,
                }).ToList();
            dbContext.Mentions.AddRange(toAdd);

            changedAuthorIds.AddRange(toRemove.Select(m => (Symbol)m.AuthorId));
            changedAuthorIds.AddRange(toAdd.Select(m => (Symbol)m.AuthorId));
        }

        if (changedAuthorIds.Count == 0)
            return;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(changedAuthorIds);
    }
}
