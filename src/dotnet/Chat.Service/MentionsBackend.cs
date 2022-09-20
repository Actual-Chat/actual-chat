using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
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

    [CommandHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent @event, CancellationToken cancellationToken)
    {
        var (chatId, entryId, authorId, content, state) = @event;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            foreach (var authorId1 in context.Operation().Items.Get<string[]>() ?? Array.Empty<string>())
                _ = GetLast(chatId, authorId1, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var markup = MarkupParser.Parse(content);
        var authorIds = new MentionsExtractor().ExtractAuthorIds(markup);
        var existingMentions = await dbContext.Mentions
            .Where(x => x.ChatId == chatId && x.EntryId == entryId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        string[] changes;

        if (state == EntryState.Removed) {
            dbContext.Mentions.RemoveRange(existingMentions);
            changes = existingMentions.Select(x => x.AuthorId).ToArray();
        }
        else {
            var toRemove = existingMentions.ExceptBy(authorIds, x => x.Id).ToList();
            dbContext.Mentions.RemoveRange(toRemove);

            var toAdd = authorIds
                .Except(existingMentions.Select(x => x.AuthorId), StringComparer.Ordinal)
                .Select(authorId1 => new DbMention {
                    Id = DbMention.ComposeId(chatId, entryId, authorId1),
                    AuthorId = authorId,
                    ChatId = chatId,
                    EntryId = entryId,
                }).ToList();
            dbContext.Mentions.AddRange(toAdd);

            changes = toRemove.Select(x => x.AuthorId).Concat(toAdd.Select(x => x.AuthorId)).ToArray();
        }

        if (changes.Length == 0)
            return;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(changes);
    }
}
