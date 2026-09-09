using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Streaming;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for AI-powered conversation segmentation and summarization.
/// </summary>
public class ConversationsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IConversationsBackend
{
    private const int MaxCallEntries = 1000;
    private static readonly TileLayer<long> EntryIdTiles = Constants.Chat.EntryIdTiles;
    private static readonly TileLayer<long> RangeMetaEntryIdTiles = Constants.Chat.RangeMetaEntryIdTiles;

    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    private IDbEntityResolver<string, DbConversation> DbConversationResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbConversation>>();
    private IConversationSummarizer ConversationSummarizer { get; } = services.GetRequiredService<IConversationSummarizer>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private ILiveSessionsBackend LiveSessionsBackend { get; } = services.GetRequiredService<ILiveSessionsBackend>();
    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();
    private FlowHub FlowHub => field ??= Services.FlowHub();

    // [ComputeMethod]
    public virtual async Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversationId);

        var dbConversation = await DbConversationResolver.Get(conversationId.Value, cancellationToken).ConfigureAwait(false);
        var conversation = dbConversation?.ToModel();
        if (conversation is null)
            return null;

        if (conversation.AttachmentIds.Length > 0) {
            var textEntryIds = conversation.AttachmentIds
                .Select(DbChatEntryAttachment.ExtractEntryId)
                .Distinct()
                .ToArray();
            var textEntries = await textEntryIds
                .Select(c => ChatsBackend.GetEntry(c, cancellationToken).AsTask())
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            var attachments = textEntries
                .SkipNullItems()
                .SelectMany(c => c.Attachments)
                .Where(c => conversation.AttachmentIds.Contains(c.Id))
                .ToArray();
            conversation = conversation with { Attachments = attachments };
        }
        return conversation;
    }

    // [ComputeMethod]
    public virtual async Task<ConversationRangeTile> GetConversationRangeTile(
        ChatId chatId,
        long start,
        CancellationToken cancellationToken)
    {
        var range = RangeMetaEntryIdTiles.AssertIsTileStart(start).Range;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var conversationRanges = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < range.End && c.EndEntryLid >= range.Start)
            .OrderBy(c => c.StartEntryLid)
            .Select(c => new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var previousConversationRange = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.EndEntryLid < range.Start)
            .OrderByDescending(c => c.StartEntryLid)
            .Select(c => (Range<long>?)new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var nextConversationRange = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.StartEntryLid >= range.End)
            .OrderBy(c => c.StartEntryLid)
            .Select(c => (Range<long>?)new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (previousConversationRange is { } previous)
            conversationRanges.Add(previous);
        if (nextConversationRange is { } next)
            conversationRanges.Add(next);

        var liveStartLid = await LiveSessionsBackend.GetVisibleStartLid(chatId, cancellationToken)
            .ConfigureAwait(false);
        if (liveStartLid is { } liveStart) {
            conversationRanges.RemoveAll(r => r.Start == liveStart);
            conversationRanges.Add(new(liveStart, long.MaxValue));
        }

        return ConversationRangeTile.NewNormalized(chatId, range, conversationRanges);
    }

    // [Computed]
    public virtual async Task<Conversation[]> GetTile(
        ChatId chatId, Range<long> range, CancellationToken cancellationToken)
    {
        var tile = RangeMetaEntryIdTiles.GetTile(range);
        var conversationTile = await GetConversationRangeTile(chatId, tile.Start, cancellationToken)
            .ConfigureAwait(false);
        var conversations = await conversationTile.ConversationIds
            .Distinct()
            .Select(cId => Get(cId, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var liveConversation = await LiveSessionsBackend.GetLiveConversation(chatId, cancellationToken)
            .ConfigureAwait(false);
        var records = conversations.SkipNullItems().Where(c => c.Id != liveConversation?.Id);
        if (liveConversation != null)
            records = records.Append(liveConversation);

        return conversationTile.ApplyTo(records, range);
    }

    // Commands

    // [CommandHandler]
    public virtual async Task<Conversation> OnChange(ConversationBackend_Change command, CancellationToken cancellationToken)
    {
        var (conversationId, _, change) = command;
        var chatId = conversationId.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invConversation = context.Operation.Items.KeylessGet<Conversation>();
            if (invConversation != null) {
                _ = Get(invConversation.Id, default);
                foreach (var cidTile in RangeMetaEntryIdTiles.GetCoveringTiles(invConversation.EntryLidRange))
                    _ = GetConversationRangeTile(chatId, cidTile.Range.Start, default);
                var previousConversationId = context.Operation.Items
                    .Get<long>(nameof(ConversationRangeTile.PreviousConversationRange));
                var nextConversationId = context.Operation.Items
                    .Get<long>(nameof(ConversationRangeTile.NextConversationRange));
                if (previousConversationId != default) {
                    var previousCidTile = RangeMetaEntryIdTiles.GetTile(previousConversationId);
                    _ = GetConversationRangeTile(chatId, previousCidTile.Range.Start, default);
                }
                if (nextConversationId != default) {
                    var nextCidTile = RangeMetaEntryIdTiles.GetTile(nextConversationId);
                    _ = GetConversationRangeTile(chatId, nextCidTile.Range.Start, default);
                }
            }
            return null!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Conversations.Lock(conversationId, cancellationToken).ConfigureAwait(false);

        var dbConversation = await dbContext.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId.Value, cancellationToken)
            .ConfigureAwait(false);
        var oldConversation = dbConversation?.ToModel();
        Conversation conversation;
        if (change.IsCreate(out var update)) {
            if (oldConversation != null)
                return oldConversation;

            // Get existing conversations that overlap with the new one
            var startEntryLid = conversationId.StartEntryLid;
            var endEntryLid = change.Create.Value.EndEntryLid;
            var sConversationIds = await dbContext.Conversations
                .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < endEntryLid && c.EndEntryLid >= startEntryLid)
                .Select(c => c.Id)
                .OrderBy(c => c)
                .ToHashSetAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove other overlapping conversations
            await dbContext.Conversations
                .Where(c => sConversationIds.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            conversation = new Conversation(conversationId);
            conversation = ApplyDiff(conversation, update);
            dbConversation = new DbConversation(conversation);
            dbContext.Add(dbConversation);

            await StorePreviousAndNextConversationIds(startEntryLid, endEntryLid).ConfigureAwait(false);
        }
        else if (change.IsUpdate(out update)) {
            // TODO(AK): too many version mismatch errors
            // if (expectedVersion != 0)
            //     dbConversation.RequireVersion(expectedVersion);
            // else
            dbConversation.Require();

            // Update existing conversation
            conversation = ApplyDiff(dbConversation.ToModel(), update);
            dbConversation.UpdateFrom(conversation);

            // Get existing conversations that overlap with the new one
            // and remove other overlapping conversations
            var startEntryLid = conversationId.StartEntryLid;
            var endEntryLid = change.Update.Value.EndEntryLid;
            var sConversationIds = await dbContext.Conversations
                .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < endEntryLid && c.EndEntryLid >= startEntryLid)
                .Select(c => c.Id)
                .OrderBy(c => c)
                .ToHashSetAsync(cancellationToken)
                .ConfigureAwait(false);
            sConversationIds.Remove(conversationId.Value);
            await dbContext.Conversations
                .Where(c => sConversationIds.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await StorePreviousAndNextConversationIds(startEntryLid, endEntryLid).ConfigureAwait(false);
        }
        else if (change.IsRemove()) {
            dbConversation.Require();
            var startEntryLid = dbConversation.StartEntryLid;
            var endEntryLid = dbConversation.EndEntryLid;

            dbContext.Remove(dbConversation);

            await StorePreviousAndNextConversationIds(startEntryLid, endEntryLid).ConfigureAwait(false);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        conversation = dbConversation.Require().ToModel();
        context.Operation.Items.KeylessSet(conversation);

        var titleOrDescriptionChanged = oldConversation is null
            || oldConversation.Title != conversation.Title
            || oldConversation.Description != conversation.Description;
        if (!change.IsRemove() && titleOrDescriptionChanged) {
            var changeKind = change.IsCreate(out _) ? ChangeKind.Create : ChangeKind.Update;
            context.Operation.AddEvent(
                new ConversationChangedEvent(conversation, oldConversation, changeKind, command.IsLiveMaterialization));
        }
        return conversation;

        Conversation ApplyDiff(Conversation originalConversation, ConversationDiff? diff) {
            // Update
            var newConversation = DiffEngine.Patch(originalConversation, diff) with {
                Version = VersionGenerator.NextVersion(originalConversation.Version),
            };
            if (newConversation.EntryLidRange.Start != originalConversation.EntryLidRange.Start)
                throw StandardError.Constraint("EntryRange.Start can't be changed.");

            // Validation - a call is exempt from all of it: nothing summarizes a call, so it has
            // neither the summarizer's three texts nor any message of its own to count.
            if (!newConversation.IsCall) {
                if (newConversation.Title.IsNullOrEmpty())
                    throw StandardError.Constraint("Conversation title cannot be empty.");
                if (newConversation.Description.IsNullOrEmpty())
                    throw StandardError.Constraint("Conversation description cannot be empty.");
                if (newConversation.Summary.IsNullOrEmpty())
                    throw StandardError.Constraint("Conversation summary cannot be empty.");
                if (newConversation.MessageCount <= 0)
                    throw StandardError.Constraint("Conversation message count should be greater than zero.");
            }

            return newConversation;
        }

        async Task StorePreviousAndNextConversationIds(long startEntryLid, long? endEntryLid)
        {
            var previousConversationId = await dbContext.Conversations
                .Where(c => c.ChatId == chatId.Value && c.EndEntryLid < startEntryLid)
                .MaxAsync(c => (long?)c.StartEntryLid, cancellationToken)
                .ConfigureAwait(false);
            var nextConversationId = await dbContext.Conversations
                .Where(c => c.ChatId == chatId.Value && c.StartEntryLid >= endEntryLid)
                .MinAsync(c => (long?)c.StartEntryLid, cancellationToken)
                .ConfigureAwait(false);

            if (previousConversationId != 0)
                context.Operation.Items
                    .Set(nameof(ConversationRangeTile.PreviousConversationRange), previousConversationId);
            if (nextConversationId != 0)
                context.Operation.Items.Set(nameof(ConversationRangeTile.NextConversationRange), nextConversationId);
        }
    }

    [CommandHandler]
    public virtual async Task<Conversation> OnSummarize(
        ConversationBackend_Summarize command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // No invalidation there as we call other commands

        var (chatId, entryIdRanges) = command;
        if (entryIdRanges.Length == 0)
            throw StandardError.Constraint("ConversationBackend_Summarize.EntryIdRanges should not be empty.");

        var startEntryLid = entryIdRanges[0].Start;
        var endEntryLid = entryIdRanges[^1].End - 1;
        var conversationId = ConversationId.New(chatId, startEntryLid);
        var existing = await Get(conversationId, cancellationToken).ConfigureAwait(false);
        var entriesInfo = await GetTextEntries(chatId, entryIdRanges, cancellationToken).ConfigureAwait(false);
        var entries = entriesInfo.TextEntries;
        if (entries.Count == 0) {
            // A call's card outlives its transcript: it records that the call happened, so deleting
            // every message must leave it standing rather than erase the call from the chat.
            if (existing is { IsCall: true })
                return existing;

            // Every entry in the range was removed - a summary of nothing must not survive, but a stale
            // command whose range predates the conversation's growth must not delete the grown one.
            var emptyExisting = existing;
            if (emptyExisting is not null && emptyExisting.EndEntryLid <= endEntryLid) {
                var removeCommand = new ConversationBackend_Change(
                    conversationId, emptyExisting.Version, Change.Remove<ConversationDiff>());
                await DbHub.Commander.Call(removeCommand, false, cancellationToken).ConfigureAwait(false);
            }
            return default!;
        }

        var firstEntry = entries.First();
        var lastEntry = entries.Last();
        var retryCount = 0;
        var summaryResult = ConversationSummarizerResult.Empty;
        while (!summaryResult.HasResult) {
            summaryResult = await ConversationSummarizer.Summarize(entries, cancellationToken).ConfigureAwait(false);
            if (summaryResult.HasResult)
                break;

            if (retryCount++ > 3)
                throw StandardError.Postpone(TimeSpan.FromMinutes(1));

            var postpone = summaryResult.Postpone;
            if (postpone != null)
                await Clocks.SystemClock.Delay(postpone.Value, cancellationToken).ConfigureAwait(false);
            else
                throw StandardError.Postpone(TimeSpan.FromMinutes(1));
        }

        var summary = summaryResult.Summary!;
        var conversation = new Conversation(conversationId) {
            Title = summary.Title,
            Description = summary.Description,
            Summary = summary.Summary,
            MessageCount = entries.Count,
            EndEntryLid = endEntryLid,
            StartsAt = firstEntry.BeginsAt,
            EndsAt = lastEntry.EndsAt ?? lastEntry.BeginsAt,
            AuthorIds = entries
                .GroupBy(a => a.AuthorId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToArray(),
            AttachmentCount = entriesInfo.AttachmentCount,
            AttachmentIds = entriesInfo.Attachments.Select(c => c.Id).ToArray(),
        };
        if (existing is { IsCall: true })
            conversation = KeepCallShape(existing, conversation, entries);

        return await Persist(conversation, command.IsLiveMaterialization, cancellationToken).ConfigureAwait(false);
    }

    [CommandHandler]
    public virtual async Task<Conversation> OnMaterialize(
        ConversationBackend_Materialize command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // Persist runs nested commands; nothing to invalidate here.

        // Persist the live session's already-computed summary as-is — no summarizer call.
        var conversation = command.Conversation;
        var words = 0;
        if (conversation.IsCall)
            (conversation, words) = await SizeCallConversation(conversation, cancellationToken)
                .ConfigureAwait(false);
        var result = await Persist(conversation, isLiveMaterialization: true, cancellationToken)
            .ConfigureAwait(false);
        if (conversation.IsCall)
            await ScheduleCallRefresh(result, words, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private Task ScheduleCallRefresh(Conversation conversation, int words, CancellationToken cancellationToken)
    {
        // A call's live summary only ever covers matured entries, and unlike an ambient session it gets
        // no finalizing pass - the last minutes of talk would never be summarized at all. The gate is
        // the live flow's own, so this can't hand a title to a call that flow would have left alone.
        var summarization = Settings.Summarization;
        if (conversation.MessageCount < summarization.MinLiveConversationEntries
            || words < summarization.MinLiveConversationWords)
            return Task.CompletedTask;

        return FlowHub.NewResumeEvent<ConversationRefreshFlow>(conversation.Id.Value)
            .WithDelay(
                Clocks.SystemClock.Now + summarization.ResummarizationDelay,
                summarization.ChatEntrySummarizationDelayQuanta)
            .Schedule(cancellationToken);
    }

    private async Task<(Conversation Conversation, int Words)> SizeCallConversation(
        Conversation conversation, CancellationToken cancellationToken)
    {
        // Nothing summarizes a call at close, so the two numbers the expansion tier is drawn from have
        // to be counted here instead. A call with no transcript stays collapsed whatever the tier says:
        // an expanded block would be empty, and the card is the whole of what there is to show.
        var startEntryLid = conversation.Id.StartEntryLid;
        var entries = await ChatsBackend
            .ListNewEntries(conversation.Id.ChatId, startEntryLid - 1, MaxCallEntries, cancellationToken)
            .ConfigureAwait(false);
        // Same rule as GetTextEntries, so the count doesn't change under the reader when the refresh
        // below recomputes it: the CallEntry closing the range is not one of the call's messages.
        var messages = entries
            .Where(e => e.LocalId <= conversation.EndEntryLid && !e.IsSystemEntry)
            .ToList();
        if (messages.Count == 0)
            return (conversation with { MessageCount = 0, IsExpandedByDefault = false }, 0);

        var words = messages.Sum(e => WordCount(e.Content));
        return (conversation with {
            MessageCount = messages.Count,
            IsExpandedByDefault = Settings.Summarization.IsExpandedByDefault(words, messages.Count),
        }, words);
    }

    private static int WordCount(string content)
        => content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private Conversation KeepCallShape(
        Conversation existing,
        Conversation summarized,
        IReadOnlyCollection<ChatEntrySlim> entries)
    {
        // The summarizer measures the transcript; the call's own shape is measured from the call. Its
        // range has to keep covering the CallEntry that closes it, or ChatUI stops treating the entry as
        // drawn by this card and shows it a second time as a system line - hence Max, never the
        // summarizer's end alone. The span is talk time, which the entries' timestamps don't give.
        var words = entries.Sum(e => WordCount(e.Content));
        return summarized with {
            CallerId = existing.CallerId,
            EndEntryLid = Math.Max(existing.EndEntryLid, summarized.EndEntryLid),
            StartsAt = existing.StartsAt,
            EndsAt = existing.EndsAt,
            IsExpandedByDefault = Settings.Summarization.IsExpandedByDefault(words, entries.Count),
        };
    }

    private async Task<Conversation> Persist(
        Conversation conversation, bool isLiveMaterialization, CancellationToken cancellationToken)
    {
        var existing = await Get(conversation.Id, cancellationToken).ConfigureAwait(false);
        var change = existing != null
            ? Change.Update(DiffEngine.Diff<Conversation, ConversationDiff>(existing, conversation))
            : Change.Create(new ConversationDiff(conversation));
        var changeCommand = new ConversationBackend_Change(conversation.Id, existing?.Version, change) {
            IsLiveMaterialization = isLiveMaterialization,
        };
        return await DbHub.Commander.Call(changeCommand, false, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Conversation?> OnAppendReply(
        ConversationBackend_AppendReply command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // This handler makes changes only via nested commands

        var (chatId, entryLid, replyIdRange) = command;
        var cidTile = RangeMetaEntryIdTiles.GetTile(entryLid);
        var conversationRangeMeta = await GetConversationRangeTile(
                chatId, cidTile.Range.Start, cancellationToken)
            .ConfigureAwait(false);
        var existingConversations = conversationRangeMeta.ConversationIds;
        if (existingConversations.Length == 0) {
            // Skip the reply as the conversation is not found - the entry group was too small for summarization
            Log.LogInformation("Skipping reply as the conversation for {ChatId} and {EntryLid} is not found", chatId, entryLid);
            return null;
        }

        var conversationId = existingConversations[0];
        var conversation = await Get(conversationId, cancellationToken).ConfigureAwait(false);
        conversation.Require();

        var entriesInfo = await GetTextEntries(chatId, [conversation.EntryLidRange, replyIdRange], cancellationToken).ConfigureAwait(false);
        var entries = entriesInfo.TextEntries;
        var summaryResult = await ConversationSummarizer.Summarize(entries, cancellationToken).ConfigureAwait(false);
        if (!summaryResult.HasResult)
            throw StandardError.Postpone(summaryResult.Postpone ?? TimeSpan.FromMinutes(10));

        var summary = summaryResult.Summary!;
        // Do not update EndEntryLid, StartsAt, EndsAt as the conversation is not continuous
        var diff = new ConversationDiff {
            Title = summary.Title,
            Description = summary.Description,
            Summary = summary.Summary,
            MessageCount = entries.Count,
            AuthorIds = entries
                .GroupBy(a => a.AuthorId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToArray(),
            AttachmentCount = entriesInfo.AttachmentCount,
            AttachmentIds = entriesInfo.Attachments.Select(c => c.Id).ToArray(),
        };
        var change = Change.Update(diff);
        var changeCommand = new ConversationBackend_Change(conversationId, conversation.Version, change);
        return await DbHub.Commander.Call(changeCommand, false, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<ConversationEntriesInfo> GetTextEntries(ChatId chatId, Range<long>[] entryLidRanges, CancellationToken cancellationToken)
    {
        Log.LogInformation("-> GetTextEntries: {Ranges}", entryLidRanges.Select(c => c.ToString()).ToCommaPhrase());
        var idTiles = entryLidRanges
            .SelectMany(idRange => EntryIdTiles.GetCoveringTiles(idRange))
            .ToList();

        var tiles = await idTiles
            .Select(idTile => ChatsBackend.GetTile(chatId, idTile.Range, false, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var textEntries = new List<ChatEntrySlim>();
        var attachments = new List<ChatEntryAttachment>();
        var attachmentCount = 0;
        // Ranges sharing a tile fetch it more than once, so entries must be deduplicated
        var chatEntries = tiles
            .SelectMany(tile => tile.Entries)
            .Where(e => entryLidRanges.Any(r => r.Contains(e.LocalId)))
            // A system entry stores no text - the client builds its wording - so it would reach the
            // summarizer as a blank line, count as a message, and put Wall-E among the participants.
            // A call's range always ends on one; an ordinary conversation can enclose one too.
            .Where(e => !e.IsSystemEntry)
            .DistinctBy(e => e.LocalId)
            .OrderBy(e => e.LocalId)
            .ToArray();
        foreach (var entry in chatEntries) {
            textEntries.Add(new ChatEntrySlim(entry));
            attachmentCount += entry.Attachments.Length;
            foreach(var attachment in entry.Attachments)
                attachments.Add(attachment);
        }
        Log.LogInformation("<- GetTextEntries: ChatId='{ChatId}', StartChatEntryId={StartChatEntryId}, LastChatEntryId={LastChatEntryId}, AttachmentCount={AttachmentCount}, AttachmentIds={AttachmentIds}",
            chatId,
            chatEntries.FirstOrDefault()?.Id.Value ?? "-",
            chatEntries.LastOrDefault()?.Id.Value ?? "-",
            attachmentCount,
            attachments.Select(c => c.Id.Value).ToCommaPhrase());
        return new ConversationEntriesInfo(textEntries, attachments, attachmentCount);
    }

    // Nested types
    private record ConversationEntriesInfo(
        IReadOnlyCollection<ChatEntrySlim> TextEntries,
        IReadOnlyCollection<ChatEntryAttachment> Attachments,
        int AttachmentCount);
}
