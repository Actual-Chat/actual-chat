using ActualChat.Chat.Db;
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

    // [CommandHandler]
    public virtual async Task Update(IMentionsBackend.UpdateCommand command, CancellationToken cancellationToken)
    {
        var entry = command.Entry;
        string chatId = entry.ChatId;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            foreach (var authorId in context.Operation().Items.Get<string[]>() ?? Array.Empty<string>())
                _ = GetLast(chatId, authorId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var markup = MarkupParser.Parse(entry.Content);
        var authorIds = new MentionsExtractor().ExtractAuthorIds(markup);
        var existingMentions = await dbContext.Mentions
            .Where(x => x.ChatId == chatId && x.EntryId == entry.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        string[] changes;

        if (entry.IsRemoved) {
            dbContext.Mentions.RemoveRange(existingMentions);
            changes = existingMentions.Select(x => x.AuthorId).ToArray();
        }
        else {
            var toRemove = existingMentions.ExceptBy(authorIds, x => x.Id).ToList();
            dbContext.Mentions.RemoveRange(toRemove);

            var toAdd = authorIds.Except(existingMentions.Select(x => x.AuthorId), StringComparer.Ordinal)
                .Select(authorId => new DbMention {
                    Id = DbMention.ComposeId(chatId, entry.Id, authorId),
                    AuthorId = authorId,
                    ChatId = chatId,
                    EntryId = entry.Id,
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
