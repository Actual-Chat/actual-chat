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
        ChatId chatId,
        Symbol mentionId,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbMention = await dbContext.Mentions
            .Where(x => x.ChatId == chatId.Value && x.MentionId == mentionId.Value)
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
            var invChangedMentionIds = context.Operation().Items.Get<HashSet<Symbol>>();
            if (invChangedMentionIds != null) {
                foreach (var mentionId in invChangedMentionIds)
                    _ = GetLast(entry.ChatId, mentionId, default);
            }
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var markup = MarkupParser.Parse(entry.Content);
        var mentionIds = new MentionExtractor().GetMentionIds(markup);
        var existingMentions = await dbContext.Mentions
            .Where(x => x.ChatId == entry.ChatId.Value && x.EntryId == entry.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changedMentionIds = new HashSet<Symbol>();
        if (changeKind is ChangeKind.Remove) {
            dbContext.Mentions.RemoveRange(existingMentions);
            changedMentionIds.AddRange(existingMentions.Select(m => (Symbol)m.MentionId));
        }
        else {
            var toRemove = existingMentions.ExceptBy(mentionIds, x => (Symbol)x.Id).ToList();
            dbContext.Mentions.RemoveRange(toRemove);

            var toAdd = mentionIds
                .Except(existingMentions.Select(x => (Symbol)x.MentionId))
                .Select(mentionId => new DbMention {
                    Id = DbMention.ComposeId(entry.Id, mentionId),
                    MentionId = mentionId,
                    EntryId = entry.LocalId,
                }).ToList();
            dbContext.Mentions.AddRange(toAdd);

            changedMentionIds.AddRange(toRemove.Select(m => (Symbol)m.MentionId));
            changedMentionIds.AddRange(toAdd.Select(m => (Symbol)m.MentionId));
        }

        if (changedMentionIds.Count == 0)
            return;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(changedMentionIds);
    }
}
