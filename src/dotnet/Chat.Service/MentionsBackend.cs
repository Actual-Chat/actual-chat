using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for tracking and querying chat mentions.
/// </summary>
public class MentionsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IMentionsBackend
{
    private IMarkupParser MarkupParser { get; } = services.GetRequiredService<IMarkupParser>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();

    // [ComputeMethod]
    public virtual async Task<Mention?> GetLast(
        ChatId chatId,
        MentionRef mentionId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbMention = await dbContext.Mentions
            .Where(x => x.ChatId == chatId.Value && x.MentionRef == mentionId.Value)
            .OrderByDescending(x => x.EntryLid)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbMention?.ToModel();
    }

    // Events

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind, _) = eventCommand;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invChangedMentionIds = context.Operation.Items.KeylessGet<HashSet<MentionRef>>();
            if (invChangedMentionIds != null) {
                foreach (var mentionId in invChangedMentionIds)
                    _ = GetLast(entry.ChatId, mentionId, default);
            }
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var existingMentions = await dbContext.Mentions
            .Where(x => x.ChatId == entry.ChatId.Value && x.EntryLid == entry.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        MentionRef[] toAddMentionIds = [];
        var changedMentionIds = new HashSet<MentionRef>();
        if (changeKind is ChangeKind.Remove) {
            dbContext.Mentions.RemoveRange(existingMentions);
            changedMentionIds.AddRange(existingMentions.Select(m => MentionRef.Parse(m.MentionRef)));
        }
        else {
            var mentionIds = await GetMentionIds(entry, cancellationToken).ConfigureAwait(false);
            var toRemove = existingMentions.ExceptBy(mentionIds, x => MentionRef.Parse(x.MentionRef)).ToList();
            dbContext.Mentions.RemoveRange(toRemove);

            var toAdd = mentionIds
                .Except(existingMentions.Select(x => MentionRef.Parse(x.MentionRef)))
                .Select(mentionId => new DbMention {
                    Id = DbMention.ComposeId(entry.Id, mentionId),
                    MentionRef = mentionId.Value,
                    EntryLid = entry.LocalId,
                    ChatId = entry.ChatId.Value,
                }).ToList();
            dbContext.Mentions.AddRange(toAdd);

            changedMentionIds.AddRange(toRemove.Select(m => MentionRef.Parse(m.MentionRef)));
            toAddMentionIds = toAdd.Select(m => MentionRef.Parse(m.MentionRef)).ToArray();
            changedMentionIds.AddRange(toAddMentionIds);
        }

        if (changedMentionIds.Count == 0)
            return;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(changedMentionIds);

        var chatId = eventCommand.Entry.ChatId;
        if (chatId.IsThread(out var threadChatId) && toAddMentionIds.Length > 0)
            context.Operation.AddEvent(new UserMentionedInThreadChatEvent(threadChatId, toAddMentionIds));
    }

    private async Task<HashSet<MentionRef>> GetMentionIds(ChatEntry entry, CancellationToken cancellationToken)
    {
        var markup = MarkupParser.Parse(entry.Content);
        var mentionIds = MentionExtractor.Instance.GetMentionIds(markup);

        var replyAuthorMentionId = await GetReplyAuthorMentionId(entry, cancellationToken).ConfigureAwait(false);
        if (replyAuthorMentionId is not null)
            mentionIds.Add(replyAuthorMentionId);

        return mentionIds;
    }

    private async Task<MentionRef?> GetReplyAuthorMentionId(ChatEntry entry, CancellationToken cancellationToken)
    {
        if (entry.GetRepliedChatEntryId() is not { } replyId)
            return null;
        if (await ChatsBackend.GetEntry(replyId, cancellationToken).ConfigureAwait(false) is not { } reply)
            return null;

        return MentionRef.NewAuthor(reply.AuthorId);
    }
}
