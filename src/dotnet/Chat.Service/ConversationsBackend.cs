using ActualChat.Chat.Db;
using ActualChat.Chat.ML;
using ActualChat.Db;
using ActualChat.Streaming;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for AI-powered conversation segmentation and summarization.
/// </summary>
public class ConversationsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IConversationsBackend
{
    private static readonly TileLayer<long> EntryIdTileLayer = Constants.Chat.EntryIdTileLayer;
    private static readonly TileLayer<long> RangeIdTileLayer = Constants.Chat.RangeIdTileLayer;

    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    private IDbEntityResolver<string, DbConversation> DbConversationResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbConversation>>();
    private IConversationSummarizer ConversationSummarizer { get; } = services.GetRequiredService<IConversationSummarizer>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private ILiveSessionsBackend LiveSessionsBackend { get; } = services.GetRequiredService<ILiveSessionsBackend>();

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
    public virtual async Task<ConversationRangeMeta> GetRangeMeta(
        ChatId chatId,
        long idTileStart,
        CancellationToken cancellationToken)
    {
        var idTile = RangeIdTileLayer.AssertIsTileStart(idTileStart);
        var idTileRange = idTile.Range;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var conversationRanges = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < idTileRange.End && c.EndEntryLid >= idTileRange.Start)
            .OrderBy(c => c.StartEntryLid)
            .Select(c => new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var previousConversationRange = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.EndEntryLid < idTileRange.Start)
            .OrderByDescending(c => c.StartEntryLid)
            .Select(c => (Range<long>?)new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var nextConversationRange = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.StartEntryLid >= idTileRange.End)
            .OrderBy(c => c.StartEntryLid)
            .Select(c => (Range<long>?)new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // A latched live session owns [V, +inf) - it always runs to the chat's tail - so it replaces every
        // solo-era conversation from V on (the UI swaps the regular block for the live one). The emitted
        // range still needs a finite end for the range math downstream, hence the cap at the chat's end;
        // that read is isolated because depending on the lid range would invalidate this on every message.
        var liveStartLid = await LiveSessionsBackend.GetVisibleStartLid(chatId, cancellationToken)
            .ConfigureAwait(false);
        if (liveStartLid is { } liveStart && idTileRange.End > liveStart) {
            Range<long> chatLidRange;
            using (Computed.BeginIsolation())
                chatLidRange = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
            var liveRange = new Range<long>(liveStart, Math.Max(chatLidRange.End, liveStart + 1));
            conversationRanges = conversationRanges
                .Where(r => r.End <= liveStart)
                .Append(liveRange)
                .OrderBy(r => r.Start)
                .ToList();
            if (previousConversationRange is { } prev && prev.End > liveStart)
                previousConversationRange = null;
            if (nextConversationRange is { } next && next.End > liveStart)
                nextConversationRange = null;
        }

        return new ConversationRangeMeta(chatId,
            conversationRanges.ToArray(),
            previousConversationRange,
            nextConversationRange);
    }

    // [Computed]
    public virtual async Task<Conversation[]> GetTile(ChatId chatId, Range<long> lidTileRange, CancellationToken cancellationToken)
    {
        var idTile = RangeIdTileLayer.GetTile(lidTileRange);
        var conversationTile = await GetRangeMeta(chatId, idTile.Start, cancellationToken).ConfigureAwait(false);
        var conversations = await conversationTile.ConversationIds
            .Distinct()
            .Select(cId => Get(cId, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var result = conversations
            .Where(c => c != null && !c.EntryLidRange.IntersectWith(lidTileRange).IsEmpty)
            .OrderBy(c => c!.EntryLidRange.Start)
            .ToList()!;

        // A latched live session owns its range: drop any solo-era conversations it overlaps and inject
        // its synthetic block instead (the UI swaps the regular block for the live one).
        var liveConversation = await LiveSessionsBackend.GetLiveConversation(chatId, cancellationToken)
            .ConfigureAwait(false);
        if (liveConversation is not null
            && !liveConversation.EntryLidRange.IntersectWith(lidTileRange).IsEmpty) {
            result = result
                .Where(c => c!.EntryLidRange.IntersectWith(liveConversation.EntryLidRange).IsEmpty)
                .Append(liveConversation)
                .OrderBy(c => c!.EntryLidRange.Start)
                .ToList()!;
        }

        return result.ToArray()!;
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
                foreach (var idTile in RangeIdTileLayer.GetCoveringTiles(invConversation.EntryLidRange))
                    _ = GetRangeMeta(chatId, idTile.Range.Start, default);
                var previousConversationId = context.Operation.Items.Get<long>(nameof(ConversationRangeMeta.PreviousConversationLidRange));
                var nextConversationId = context.Operation.Items.Get<long>(nameof(ConversationRangeMeta.NextConversationLidRange));
                if (previousConversationId != default) {
                    var previousIdTile = RangeIdTileLayer.GetTile(previousConversationId);
                    _ = GetRangeMeta(chatId, previousIdTile.Range.Start, default);
                }
                if (nextConversationId != default) {
                    var nextIdTile = RangeIdTileLayer.GetTile(nextConversationId);
                    _ = GetRangeMeta(chatId, nextIdTile.Range.Start, default);
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

            // Validation
            if (newConversation.Title.IsNullOrEmpty())
                throw StandardError.Constraint("Conversation title cannot be empty.");
            if (newConversation.Description.IsNullOrEmpty())
                throw StandardError.Constraint("Conversation description cannot be empty.");
            if (newConversation.Summary.IsNullOrEmpty())
                throw StandardError.Constraint("Conversation summary cannot be empty.");
            if (newConversation.MessageCount <= 0)
                throw StandardError.Constraint("Conversation message count should be greater than zero.");

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
                context.Operation.Items.Set(nameof(ConversationRangeMeta.PreviousConversationLidRange), previousConversationId);
            if (nextConversationId != 0)
                context.Operation.Items.Set(nameof(ConversationRangeMeta.NextConversationLidRange), nextConversationId);
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
        var entriesInfo = await GetTextEntries(chatId, entryIdRanges, cancellationToken).ConfigureAwait(false);
        var entries = entriesInfo.TextEntries;
        if (entries.Count == 0)
            return default!;

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
        return await Persist(command.Conversation, isLiveMaterialization: true, cancellationToken).ConfigureAwait(false);
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
        var conversationTile = RangeIdTileLayer.GetTile(entryLid);
        var conversationRangeMeta = await GetRangeMeta(chatId, conversationTile.Range.Start, cancellationToken)
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
            .SelectMany(idRange => EntryIdTileLayer.GetCoveringTiles(idRange))
            .ToList();

        var tiles = await idTiles
            .Select(idTile => ChatsBackend.GetTile(chatId, idTile.Range, false, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var textEntries = new List<ChatEntrySlim>();
        var attachments = new List<ChatEntryAttachment>();
        var attachmentCount = 0;
        var chatEntries = tiles
            .SelectMany(tile => tile.Entries)
            .Where(e => entryLidRanges.Any(r => r.Contains(e.LocalId)))
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
