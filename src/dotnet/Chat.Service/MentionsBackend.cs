using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class MentionsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IMentionsBackend
{
    private IMarkupParser MarkupParser { get; } = services.GetRequiredService<IMarkupParser>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();

    // [ComputeMethod]
    public virtual async Task<Mention?> GetLast(
        ChatId chatId,
        MentionId mentionId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbMention = await dbContext.Mentions
            .Where(x => x.ChatId == chatId.Value && x.MentionId == mentionId.Value)
            .OrderByDescending(x => x.EntryLocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbMention?.ToModel();
    }

    // Events

    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind, _) = eventCommand;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invChangedMentionIds = context.Operation.Items.KeylessGet<HashSet<MentionId>>();
            if (invChangedMentionIds != null) {
                foreach (var mentionId in invChangedMentionIds)
                    _ = GetLast(entry.ChatId, mentionId, default);
            }
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var existingMentions = await dbContext.Mentions
            .Where(x => x.ChatId == entry.ChatId.Value && x.EntryLocalId == entry.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        MentionId[] toAddMentionIds = [];
        var changedMentionIds = new HashSet<MentionId>();
        if (changeKind is ChangeKind.Remove) {
            dbContext.Mentions.RemoveRange(existingMentions);
            changedMentionIds.AddRange(existingMentions.Select(m => MentionId.Parse(m.MentionId)));
        }
        else {
            var mentionIds = await GetMentionIds(entry, cancellationToken).ConfigureAwait(false);
            var toRemove = existingMentions.ExceptBy(mentionIds, x => MentionId.Parse(x.MentionId)).ToList();
            dbContext.Mentions.RemoveRange(toRemove);

            var toAdd = mentionIds
                .Except(existingMentions.Select(x => MentionId.Parse(x.MentionId)))
                .Select(mentionId => new DbMention {
                    Id = DbMention.ComposeId(entry.Id, mentionId),
                    MentionId = mentionId.Value,
                    EntryLocalId = entry.LocalId,
                    ChatId = entry.ChatId.Value,
                }).ToList();
            dbContext.Mentions.AddRange(toAdd);

            changedMentionIds.AddRange(toRemove.Select(m => MentionId.Parse(m.MentionId)));
            toAddMentionIds = toAdd.Select(m => MentionId.Parse(m.MentionId)).ToArray();
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

    private async Task<HashSet<MentionId>> GetMentionIds(ChatEntry entry, CancellationToken cancellationToken)
    {
        var markup = MarkupParser.Parse(entry.Content);
        var mentionIds = MentionExtractor.Instance.GetMentionIds(markup);

        var replyAuthorMentionId = await GetReplyAuthorMentionId(entry, cancellationToken).ConfigureAwait(false);
        if (replyAuthorMentionId is not null)
            mentionIds.Add(replyAuthorMentionId);

        return mentionIds;
    }

    private async Task<MentionId?> GetReplyAuthorMentionId(ChatEntry entry, CancellationToken cancellationToken)
    {
        if (entry.GetRepliedChatEntryId() is not { } replyId)
            return null;
        if (await ChatsBackend.GetEntry(replyId, cancellationToken).ConfigureAwait(false) is not { } reply)
            return null;

        return MentionId.NewAuthor(reply.AuthorId);
    }
}
