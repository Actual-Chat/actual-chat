using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
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
        Symbol mentionId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbMention = await dbContext.Mentions
            .Where(x => x.ChatId == chatId && x.MentionId == mentionId.Value)
            .OrderByDescending(x => x.EntryId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbMention?.ToModel();
    }

    // Events

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind) = eventCommand;
        var context = CommandContext.GetCurrent();

        if (InvalidationMode.IsOn) {
            var invChangedMentionIds = context.Operation.Items.Get<HashSet<MentionId>>();
            if (invChangedMentionIds != null) {
                foreach (var mentionId in invChangedMentionIds)
                    _ = GetLast(entry.ChatId, mentionId, default);
            }
            return;
        }

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var existingMentions = await dbContext.Mentions
            .Where(x => x.ChatId == entry.ChatId && x.EntryId == entry.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changedMentionIds = new HashSet<MentionId>();
        if (changeKind is ChangeKind.Remove) {
            dbContext.Mentions.RemoveRange(existingMentions);
            changedMentionIds.AddRange(existingMentions.Select(m => new MentionId(m.MentionId)));
        }
        else {
            var mentionIds = await GetMentionIds(entry, cancellationToken).ConfigureAwait(false);
            var toRemove = existingMentions.ExceptBy(mentionIds, x => new MentionId(x.MentionId)).ToList();
            dbContext.Mentions.RemoveRange(toRemove);

            var toAdd = mentionIds
                .Except(existingMentions.Select(x => new MentionId(x.MentionId)))
                .Select(mentionId => new DbMention {
                    Id = DbMention.ComposeId(entry.Id, mentionId),
                    MentionId = mentionId,
                    EntryId = entry.LocalId,
                    ChatId = entry.ChatId,
                }).ToList();
            dbContext.Mentions.AddRange(toAdd);

            changedMentionIds.AddRange(toRemove.Select(m => new MentionId(m.MentionId)));
            changedMentionIds.AddRange(toAdd.Select(m => new MentionId(m.MentionId)));
        }

        if (changedMentionIds.Count == 0)
            return;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set(changedMentionIds);
    }

    private async Task<HashSet<MentionId>> GetMentionIds(ChatEntry entry, CancellationToken cancellationToken)
    {
        var markup = MarkupParser.Parse(entry.Content);
        var mentionIds = new MentionExtractor().GetMentionIds(markup);

        var mentionIdFromReply = await GetMentionIdFromReply(entry, cancellationToken).ConfigureAwait(false);
        if (!mentionIdFromReply.IsNone)
            mentionIds.Add(mentionIdFromReply);

        return mentionIds;
    }

    private async Task<MentionId> GetMentionIdFromReply(ChatEntry entry, CancellationToken cancellationToken)
    {
        if (entry.GetRepliedChatEntryId() is not { } replyId)
            return MentionId.None;

        if (await ChatsBackend.GetEntry(replyId, cancellationToken).ConfigureAwait(false) is not { } reply)
            return MentionId.None;

        return new MentionId(reply.AuthorId, AssumeValid.Option);
    }
}
