using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Flows;
using ActualChat.Streaming;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    public const string RemovalMaintenanceOwnerId = "removal";

    private ILiveSessionsBackend LiveSessionsBackend
        => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    // [ComputeMethod] - consolidated
    public virtual async Task<bool> IsRemovalPending(ChatId chatId, CancellationToken cancellationToken)
    {
        // Removal maintenance is what marks a chat as gone: ChatPurgeFlow drains its content and then
        // deletes it. A thread inherits its parent's maintenance, so it goes with the parent too.
        var maintenanceMode = await MaintenancesBackend.GetMode(chatId, cancellationToken).ConfigureAwait(false);
        return maintenanceMode == MaintenanceMode.Removal;
    }

    public virtual async Task<ApiArray<ChatId>> ListSoleOwnedChatIds(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);

        return await ListSoleOwnedChatIds(dbContext, userId, false, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<bool> HasUnpurgedEntries(
        ChatId chatId, Range<long> entryLidRange, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);

        var (start, end) = entryLidRange;
        return await dbContext.ChatEntries
            .AnyAsync(e => e.ChatId == chatId.Value && e.Kind == 0 && !e.IsRemovedAndPurged
                    && e.LocalId >= start && e.LocalId < end,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Range<long>> OnRequestHistoryWipe(
        ChatsBackend_RequestHistoryWipe command, CancellationToken cancellationToken)
    {
        var (chatId, (fromEntryLid, endEntryLid)) = command;
        var context = CommandContext.GetCurrent();
        ArgumentOutOfRangeException.ThrowIfNegative(fromEntryLid);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken).ConfigureAwait(false);
        dbChat.Require();
        var maxEntryLid = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? 0;
        endEntryLid = Math.Min(endEntryLid, maxEntryLid + 1);
        if (fromEntryLid >= endEntryLid)
            return default;

        // A wipe that leaves no message before it takes the whole history, so ChatPurgeFlow
        // keeps it as the boundary it clears up to rather than as one more range
        if (fromEntryLid > 0) {
            var hasEarlierEntries = await dbContext.ChatEntries
                .AnyAsync(e => e.ChatId == chatId.Value && e.Kind == 0 && !e.IsRemoved && !e.IsSystemEntry
                        && e.LocalId < fromEntryLid,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!hasEarlierEntries)
                fromEntryLid = 0;
        }

        // Posted once this operation commits
        var flowId = FlowHub.NewId<ChatPurgeFlow>(chatId.Value);
        var entryLidRange = new Range<long>(fromEntryLid, endEntryLid);
        context.Operation.AddEvent(Flows_ChangeInbox.Post(flowId, new ChatPurgeFlow.WipeHistory(entryLidRange)));
        return entryLidRange;
    }

    // [CommandHandler]
    public virtual async Task OnRequestRemoval(ChatsBackend_RequestRemoval command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbChat is null)
            return;

        // The maintenance marks the removal: from the moment it's set, the chat is gone to readers
        // and closed to writers. ChatPurgeFlow drains it and then deletes it.
        if (!await StartRemovalMaintenance(chatId, cancellationToken).ConfigureAwait(false))
            return;

        // Scheduled right away: the maintenance is already committed, so the purge must be scheduled
        // even if this operation fails - otherwise the chat would stay hidden with nothing to drain it
        await FlowHub.NewResumeEvent<ChatPurgeFlow>(chatId.Value)
            .Schedule(cancellationToken)
            .ConfigureAwait(false);

        var chat = dbChat.ToModel();
        context.Operation.AddEvent(new ChatChangedEvent(chat, chat, ChangeKind.Remove));
        await MarkThreadsForRemoval(dbContext, chatId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRequestUserRemoval(
        ChatsBackend_RequestUserRemoval command,
        CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var context = CommandContext.GetCurrent();

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);

        await RequestSoleOwnedChatsRemoval(dbContext, userId, cancellationToken).ConfigureAwait(false);
        await RequestAuthorEntriesPurge(dbContext, context, userId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<int> OnPurgeEntryBatch(
        ChatsBackend_PurgeEntryBatch command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();
        ArgumentOutOfRangeException.ThrowIfNegative(command.ClearUntilEntryLid);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken).ConfigureAwait(false);
        if (dbChat is null) {
            // A removed Place's root chat ends its maintenance only once the Place is gone too
            if (await NewEndRemovalMaintenanceCommand(chatId, cancellationToken).ConfigureAwait(false) is { } endCmd)
                await Commander.Call(endCmd, true, cancellationToken).ConfigureAwait(false);
            return -1;
        }

        var isRemoval = await IsRemovalPending(chatId, cancellationToken).ConfigureAwait(false);
        // An import owns the chat's entries until it ends, so cleanup waits for it
        if (!isRemoval && await MaintenancesBackend.IsImporting(chatId, cancellationToken).ConfigureAwait(false))
            return 0;

        var wipeEntryLidRange = command.WipeEntryLidRange;
        ArgumentOutOfRangeException.ThrowIfNegative(wipeEntryLidRange.Start);
        var maxEntryLid = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? 0;
        if (command.ClearUntilEntryLid > maxEntryLid || wipeEntryLidRange.End - 1 > maxEntryLid)
            throw StandardError.Constraint("The history cannot be cleared past the end of the chat.");

        var maxClearedEntryLid = isRemoval ? long.MaxValue : command.ClearUntilEntryLid;
        if (!isRemoval && dbChat.RetentionPeriod is { } retention) {
            var cutoff = (Clocks.SystemClock.Now - retention).ToDateTime();
            // The first entry that outlives the cutoff - everything before it has expired
            var firstKeptEntryLid = await dbContext.ChatEntries
                .Where(e => e.ChatId == chatId.Value && e.Kind == 0 && !e.IsRemovedAndPurged
                    && e.LocalId > maxClearedEntryLid
                    && (e.BeginsAt >= cutoff || e.EndsAt >= cutoff
                        || e.ContentStreamId != null && e.ContentStreamId != ""))
                .MinAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? maxEntryLid + 1;
            var conversationStart = await dbContext.Conversations
                .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < firstKeptEntryLid
                    && c.StartEntryLid > maxClearedEntryLid
                    && (c.EndEntryLid >= firstKeptEntryLid || c.EndsAt >= cutoff))
                .MinAsync(c => (long?)c.StartEntryLid, cancellationToken).ConfigureAwait(false);
            if (conversationStart is { } start)
                firstKeptEntryLid = Math.Min(firstKeptEntryLid, start);

            var liveStart = await LiveSessionsBackend
                .GetVisibleStartLid(chatId, cancellationToken).ConfigureAwait(false);
            if (liveStart is { } liveEntryLid)
                firstKeptEntryLid = Math.Min(firstKeptEntryLid, liveEntryLid);
            var activeThreadStartEntryLid = await GetActiveThreadStartEntryLid(
                    dbContext, chatId, maxClearedEntryLid, firstKeptEntryLid, cutoff, cancellationToken)
                .ConfigureAwait(false);
            if (activeThreadStartEntryLid is { } threadStartEntryLid)
                firstKeptEntryLid = Math.Min(firstKeptEntryLid, threadStartEntryLid);
            maxClearedEntryLid = Math.Max(maxClearedEntryLid, firstKeptEntryLid - 1);
        }
        // A wiped range the cleared prefix reaches becomes a part of it
        if (isRemoval || wipeEntryLidRange.IsEmpty || wipeEntryLidRange.Start <= maxClearedEntryLid + 1) {
            if (!isRemoval)
                maxClearedEntryLid = Math.Max(maxClearedEntryLid, wipeEntryLidRange.End - 1);
            wipeEntryLidRange = default;
        }
        var (wipeStartEntryLid, wipeEndEntryLid) = wipeEntryLidRange;

        var localIds = await GetCleanupEntries(dbContext, chatId, maxClearedEntryLid, wipeEntryLidRange, maxEntryLid)
            .OrderBy(e => e.LocalId).Take(Settings.CleanupBatchSize).Select(e => e.LocalId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        if (localIds.Length > 0) {
            var purgeEntriesCmd = new ChatsBackend_PurgeEntries(
                chatId, localIds, null, maxClearedEntryLid, wipeEntryLidRange);
            count = await Commander.Call(purgeEntriesCmd, cancellationToken).ConfigureAwait(false);
        }

        var conversationIds = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value
                && (c.EndEntryLid <= maxClearedEntryLid
                    || c.StartEntryLid >= wipeStartEntryLid && c.EndEntryLid < wipeEndEntryLid))
            .OrderBy(c => c.StartEntryLid).Take(Settings.CleanupBatchSize).Select(c => c.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await PurgeConversations(dbContext, conversationIds, cancellationToken).ConfigureAwait(false);

        if (isRemoval && localIds.Length < Settings.CleanupBatchSize
            && conversationIds.Count < Settings.CleanupBatchSize) {
            if (chatId is PlaceChatId { IsRoot: true } placeRootChatId) {
                // The Place goes last: its other chats drain on their own, and each one that is done
                // wakes this flow up again
                var placeChatIdPrefix = PlaceChatId.IdPrefix + placeRootChatId.PlaceId.Value + "-";
                var hasOtherChats = await dbContext.Chats
                    .AnyAsync(c => c.Id.StartsWith(placeChatIdPrefix) && c.Id != chatId.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (hasOtherChats)
                    return 0;

                // After the commit: the Place is removed in an operation of its own, and the root chat
                // removal inside it would wait on the row lock this one holds. The maintenance keeps
                // the Place hidden until then: removing the root chat resumes this flow, which ends
                // the maintenance once it finds the chat gone - or retries the removal on its next run.
                var removePlaceCmd = new PlacesBackend_Change(
                    placeRootChatId.PlaceId, null, Change.Remove<PlaceDiff>());
                context.Operation.AddEvent(removePlaceCmd);
                return 0;
            }

            var removeChatCmd = new ChatsBackend_Change(chatId, null, Change.Remove<ChatDiff>());
            await Commander.Call(removeChatCmd, cancellationToken).ConfigureAwait(false);
            var outermostChatId = chatId.IsThread(out var threadChatId) ? threadChatId.GetOutermostParent() : chatId;
            if (outermostChatId is PlaceChatId placeChatId) {
                var resumeRootCleanupEvent = FlowHub.NewResumeEvent<ChatPurgeFlow>(
                    placeChatId.PlaceId.RootChatId.Value);
                context.Operation.AddEvent(resumeRootCleanupEvent);
            }
            // After the commit: ended any earlier, a failed removal would show the half-drained chat
            if (await NewEndRemovalMaintenanceCommand(chatId, cancellationToken).ConfigureAwait(false) is { } endCmd)
                context.Operation.AddEvent(endCmd);
            return -1;
        }

        // Retention expires entries on its own, so such a chat has to keep polling; anything else
        // becomes eligible only through a change that schedules its own resume event, so the flow
        // can go dormant instead of waking up forever on a chat nobody touches. Going dormant is
        // gated on there being nothing left to purge at all rather than on nothing being eligible
        // yet: eligibility is a clock comparison, and on a node whose clock trails the one that
        // stamped RemovedAt the grace period can look unfinished right when the resume fires.
        // A cleared entry that is still streaming waits for its stream to end the same way.
        var isIdle = !isRemoval && localIds.Length == 0 && conversationIds.Count == 0;
        if (isIdle && dbChat.RetentionPeriod is null) {
            var hasPendingEntries = await dbContext.ChatEntries
                .AnyAsync(e => e.ChatId == chatId.Value && e.Kind == 0 && !e.IsRemovedAndPurged
                        && (e.IsRemoved || e.LocalId <= maxClearedEntryLid
                            || e.LocalId >= wipeStartEntryLid && e.LocalId < wipeEndEntryLid),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!hasPendingEntries)
                return -1;
        }

        return count + conversationIds.Count;
    }

    // [CommandHandler]
    public virtual async Task<int> OnPurgeAuthorEntries(
        ChatsBackend_PurgeAuthorEntries command,
        CancellationToken cancellationToken)
    {
        var (chatId, authorId) = command;
        // An import owns the chat's entries until it ends, so the purge waits for it - unless the chat
        // itself goes. -1 tells ChatPurgeFlow to keep the author and come back later.
        var isRemoval = await IsRemovalPending(chatId, cancellationToken).ConfigureAwait(false);
        if (!isRemoval && await MaintenancesBackend.IsImporting(chatId, cancellationToken).ConfigureAwait(false))
            return -1;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);

        var localIds = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value
                && e.AuthorId == authorId.Value && e.Kind == 0 && !e.IsRemovedAndPurged)
            .OrderBy(e => e.LocalId).Take(Settings.CleanupBatchSize).Select(e => e.LocalId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (localIds.Length == 0)
            return 0;

        var purgeEntriesCmd = new ChatsBackend_PurgeEntries(chatId, localIds, authorId);
        await Commander.Call(purgeEntriesCmd, true, cancellationToken).ConfigureAwait(false);
        return localIds.Length;
    }

    // [CommandHandler]
    [SuppressMessage("ReSharper", "CSharp14OverloadResolutionWithSpanBreakingChange")]
    public virtual async Task<int> OnPurgeEntries(
        ChatsBackend_PurgeEntries command,
        CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(command.LocalIds.Length, Settings.CleanupBatchSize);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken).ConfigureAwait(false);
        if (dbChat is null)
            return -1;

        var maxClearedEntryLid = command.MaxClearedEntryLid;
        var wipeEntryLidRange = command.WipeEntryLidRange;
        var maxEntryLid = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? 0;
        var eligibleEntries = GetCleanupEntries(dbContext, chatId, maxClearedEntryLid, wipeEntryLidRange, maxEntryLid);
        if (command.RemovedAuthorId is { } removedAuthorId)
            eligibleEntries = dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0
                && !e.IsRemovedAndPurged && e.AuthorId == removedAuthorId.Value
                && command.LocalIds.Contains(e.LocalId));
        var eligibleIds = eligibleEntries.Select(e => e.Id);
        var entries = await dbContext.ChatEntries.ForUpdate()
            .Where(e => command.LocalIds.Contains(e.LocalId) && eligibleIds.Contains(e.Id)).OrderBy(e => e.LocalId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var legacyEntries = command.RemovedAuthorId is null
            ? entries.Where(e => !IsCleared(e.LocalId) && e.RemovedAt == null).ToArray()
            : [];
        foreach (var entry in legacyEntries)
            entry.RemovedAt = Clocks.SystemClock.Now.ToDateTime();
        entries.RemoveAll(e => legacyEntries.Contains(e));
        // Rows removed before RemovedAt existed get their grace period starting now, and unlike an
        // ordinary removal nothing else scheduled a resume for the moment it ends.
        if (legacyEntries.Length > 0)
            context.Operation.AddEvent(FlowHub.NewResumeEvent<ChatPurgeFlow>(chatId.Value)
                .WithDelay(Settings.RemovedEntryRetention));

        var entryIds = entries.Select(e => e.Id).ToArray();
        var localIds = entries.Select(e => e.LocalId).ToArray();
        // A chat being removed loses all of its conversations anyway
        if (command.MaxClearedEntryLid != long.MaxValue && entryIds.Length > 0) {
            var dbConversations = await dbContext.Conversations.Where(c => c.ChatId == chatId.Value
                    && dbContext.ChatEntries.Any(e => entryIds.Contains(e.Id)
                        && e.LocalId >= c.StartEntryLid && e.LocalId <= c.EndEntryLid))
                .Select(c => new { c.Id, c.StartEntryLid, c.EndEntryLid })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            // A summary covering messages that stay is rebuilt without the purged ones rather than
            // thrown away with them - late enough for a purge still in progress to empty it first;
            // only one left with nothing is purged.
            var emptyConversationIds = new List<string>();
            foreach (var dbConversation in dbConversations) {
                var hasSurvivingEntries = await dbContext.ChatEntries
                    .AnyAsync(e => e.ChatId == chatId.Value && e.Kind == 0 && !e.IsRemoved
                        && !entryIds.Contains(e.Id)
                        && e.LocalId >= dbConversation.StartEntryLid && e.LocalId <= dbConversation.EndEntryLid,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!hasSurvivingEntries) {
                    emptyConversationIds.Add(dbConversation.Id);
                    continue;
                }

                var conversationId = ConversationId.Parse(dbConversation.Id);
                context.Operation.AddEvent(FlowHub.NewResumeEvent<ConversationRefreshFlow>(conversationId.Value)
                    .WithDelay(
                        Clocks.SystemClock.Now + Settings.Summarization.ResummarizationDelay,
                        Settings.Summarization.ChatEntrySummarizationDelayQuanta));
            }
            await PurgeConversations(dbContext, emptyConversationIds, cancellationToken).ConfigureAwait(false);
        }
        // A thread goes only with a cleared range that takes its anchor too. An anchor that is merely
        // removed - by its author, or with its author's account - leaves the thread to everyone else.
        foreach (var entry in entries.Where(e => e.IsThreadStartEntry && IsCleared(e.LocalId))) {
            var requestRemovalCmd = new ChatsBackend_RequestRemoval(chatId.CreateThreadId(entry.LocalId));
            await Commander.Call(requestRemovalCmd, cancellationToken).ConfigureAwait(false);
        }

        // Voice and dub audio belong to their entry alone - a forward copies the text and the attachment
        // ids, never these - so they go with it. An attachment's media can be shared by entries in other
        // chats, and with nothing that counts its references, it stays.
        var dubMediaIds = await dbContext.Translations.Where(t => entryIds.Contains(t.EntryId!))
            .Select(t => t.DubMediaId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ownMediaIds = dubMediaIds.Concat(entries.Select(e => e.ToModel().Audio?.MediaId?.Value))
            .Where(id => !id.IsNullOrEmpty()).Distinct().ToArray();
        foreach (var mediaSid in ownMediaIds) {
            var removeMediaCmd = new MediaBackend_Change(MediaId.Parse(mediaSid!), null, Change.Remove<MediaFull>());
            context.Operation.AddEvent(removeMediaCmd);
        }

        var locationIds = entries.Select(e => e.LocationId).Where(id => !id.IsNullOrEmpty()).Distinct().ToArray();
        if (locationIds.Length > 0) {
            var referencedLocationIds = await dbContext.ChatEntries
                .Where(e => e.ChatId == chatId.Value && locationIds.Contains(e.LocationId!)
                    && !entryIds.Contains(e.Id))
                .Select(e => e.LocationId!).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
            var orphanLocationIds = locationIds.Except(referencedLocationIds).ToArray();
            if (orphanLocationIds.Length > 0)
                await dbContext.SharedLocations.Where(l => orphanLocationIds.Contains(l.Id))
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var entry in entries) {
            var removeAttachmentsCmd = new ChatsBackend_RemoveAttachments(ChatEntryId.New(chatId, entry.LocalId));
            await Commander.Call(removeAttachmentsCmd, cancellationToken).ConfigureAwait(false);
        }

        var reactionKeys = await dbContext.Reactions.Where(e => entryIds.Contains(e.EntryId))
            .Select(e => new { e.EntryId, e.AuthorId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var mentionRefs = await dbContext.Mentions
            .Where(e => e.ChatId == chatId.Value && localIds.Contains(e.EntryLid))
            .Select(e => e.MentionRef).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.Reactions.Where(e => entryIds.Contains(e.EntryId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.ReactionSummaries.Where(e => entryIds.Contains(e.EntryId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.Mentions.Where(e => e.ChatId == chatId.Value && localIds.Contains(e.EntryLid))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.ChatEntryLanguages.Where(e => entryIds.Contains(e.Id))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.Translations.Where(e => entryIds.Contains(e.EntryId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.CoachEntries.Where(e => e.ChatId == chatId.Value && localIds.Contains(e.LocalId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (command.RemovedAuthorId is { } coachAuthorId)
            await dbContext.CoachConversations
                .Where(e => e.ChatId == chatId.Value && e.AuthorId == coachAuthorId.Value)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count > 0) {
            var ids = entries.Select(e => ChatEntryId.New(chatId, e.LocalId)).ToArray();
            var updateVisualMediaIndexCmd = new ChatsBackend_UpdateChatVisualMediaIndex(chatId, ids, []);
            await Commander.Call(updateVisualMediaIndexCmd, cancellationToken).ConfigureAwait(false);
            var updateFileIndexCmd = new ChatsBackend_UpdateChatFileIndex(chatId, ids, []);
            await Commander.Call(updateFileIndexCmd, cancellationToken).ConfigureAwait(false);
            var updateLinkIndexCmd = new ChatsBackend_UpdateChatLinkIndex(chatId, ids, []);
            await Commander.Call(updateLinkIndexCmd, cancellationToken).ConfigureAwait(false);
        }

        foreach (var entry in entries) {
            // The anchor of a thread that outlives it stays as a tombstone too: it's how
            // the thread is found once clearing reaches that range.
            var isKeptThreadAnchor = entry.IsThreadStartEntry && !IsCleared(entry.LocalId);
            if (entry.LocalId != maxEntryLid && !isKeptThreadAnchor) {
                dbContext.Remove(entry);
                continue;
            }

            // The allocator recovers its high-water mark from this content-free tombstone after Redis loss.
            entry.UpdateFrom(new TextEntry(ChatEntryId.New(chatId, entry.LocalId), VersionGenerator.NextVersion()) {
                AuthorId = AuthorId.Parse(entry.AuthorId),
                BeginsAt = entry.BeginsAt,
                IsRemoved = true,
            });
            entry.HasAttachments = false;
            entry.IsRemovedAndPurged = true;
            entry.IsThreadStartEntry = isKeptThreadAnchor;
            entry.RemovedAt = Clocks.SystemClock.Now.ToDateTime();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => {
            foreach (var localId in localIds) {
                InvalidateTiles(chatId, localId, ChangeKind.Remove, false);
                _ = GetEntryAttachments(ChatEntryId.New(chatId, localId), default);
            }
            var locations = Services.GetRequiredService<ISharedLocationsBackend>();
            foreach (var locationId in locationIds)
                _ = locations.Get(SharedLocationId.Parse(locationId!), default);
            _ = locations.ListLive(chatId, default);
            var reactions = Services.GetRequiredService<IReactionsBackend>();
            foreach (var entryId in entryIds)
                _ = reactions.List(ChatEntryId.Parse(entryId), default);
            foreach (var key in reactionKeys)
                _ = reactions.Get(ChatEntryId.Parse(key.EntryId), AuthorId.Parse(key.AuthorId), default);
            var mentions = Services.GetRequiredService<IMentionsBackend>();
            foreach (var mentionRef in mentionRefs)
                _ = mentions.GetLast(chatId, MentionRef.Parse(mentionRef), default);
            _ = GetMinLid(chatId, default);
            _ = GetMaxLid(chatId, false, default);
            _ = GetMaxLid(chatId, true, default);
        });
        if (localIds.Length > 0)
            context.Operation.AddEvent(new ChatEntriesPurgedEvent(chatId, localIds));

        return entries.Count + legacyEntries.Length;

        bool IsCleared(long localId)
            => localId <= maxClearedEntryLid || wipeEntryLidRange.Contains(localId);
    }

    // Private methods

    private async Task<ApiArray<ChatId>> ListSoleOwnedChatIds(
        ChatDbContext dbContext, UserId userId, bool includeRemovalPending, CancellationToken cancellationToken)
    {
        var owners = dbContext.Chats
            .Join(dbContext.Roles, c => c.Id, r => r.ChatId, (c, r) => new { c, r })
            .Where(x => x.r.SystemRole == SystemRole.Owner)
            .Join(dbContext.AuthorRoles, x => x.r.Id, r => r.DbRoleId, (x, r) => new { x.c, ar = r })
            .Join(dbContext.Authors, x => x.ar.DbAuthorId, a => a.Id,
                (x, a) => new { ChatSid = x.c.Id, x.c.IsPublic, a.UserId });
        var ownChatSids = owners.Where(x => x.UserId == userId.Value).Select(x => x.ChatSid);
        var ownerRows = await owners
            .Where(x => ownChatSids.Contains(x.ChatSid))
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // An owner whose account is already gone doesn't count: its authors outlive it until
        // the chats' cleanups have purged their entries, and two co-owners leaving one after the other
        // would otherwise leave the chat with nobody to manage it
        var otherUserSids = ownerRows.Select(x => x.UserId).Where(x => x != userId.Value).Distinct().ToList();
        var activeOtherUserSids = (await otherUserSids
            .Select(async userSid => {
                var otherUserId = UserId.Parse(userSid);
                var exists = await AccountsBackend.Exists(otherUserId, cancellationToken).ConfigureAwait(false);
                return exists ? userSid : null;
            })
            .Collect(cancellationToken)
            .ConfigureAwait(false)
            ).SkipNullItems()
            .ToList();
        var soleOwned = ownerRows
            .GroupBy(x => x.ChatSid, StringComparer.Ordinal)
            .Where(g => !g.Any(x => activeOtherUserSids.Contains(x.UserId)))
            .Select(g => (ChatId: ChatId.Parse(g.Key), g.First().IsPublic))
            .ToList();

        // A Place with no other owner goes as a whole, chats and all. In a Place that stays, its owners
        // manage the public chats, so only a private chat with no other owner of its own goes.
        var removedPlaceIds = soleOwned
            .Select(x => x.ChatId)
            .OfType<PlaceChatId>()
            .Where(x => x.IsRoot)
            .Select(x => x.PlaceId)
            .ToHashSet();
        var chatIds = soleOwned
            .Where(x => x.ChatId is not PlaceChatId { IsRoot: false } placeChatId
                || (!removedPlaceIds.Contains(placeChatId.PlaceId) && !x.IsPublic))
            .Select(x => x.ChatId)
            .OrderBy(x => x.Value, StringComparer.Ordinal)
            .ToList();
        if (includeRemovalPending)
            return chatIds.ToApiArray();

        var isRemovalPending = await chatIds
            .Select(chatId => IsRemovalPending(chatId, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return chatIds.Where((_, i) => !isRemovalPending[i]).ToApiArray();
    }

    // A thread is a chat of its own, so it is drained and deleted by its own cleanup. More than
    // a batch of them is left to ChatPurgeFlow, which marks the rest as it purges their anchors.
    private async Task MarkThreadsForRemoval(
        ChatDbContext dbContext, ChatId chatId, CancellationToken cancellationToken)
    {
        var threadStartEntryLids = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId.Value && e.Kind == 0 && e.IsThreadStartEntry)
            .OrderBy(e => e.LocalId).Take(Settings.CleanupBatchSize)
            .Select(e => e.LocalId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var lid in threadStartEntryLids) {
            var requestRemovalCmd = new ChatsBackend_RequestRemoval(chatId.CreateThreadId(lid));
            await Commander.Call(requestRemovalCmd, cancellationToken).ConfigureAwait(false);
        }
    }

    // A thread lives as long as its replies do, so retention keeps its anchor until the thread expires too
    private async Task<long?> GetActiveThreadStartEntryLid(
        ChatDbContext dbContext, ChatId chatId, long maxClearedEntryLid, long firstKeptEntryLid, DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var threadIdPrefix = chatId.Value + ChatId.ThreadIdSeparator;
        var threadSids = await dbContext.Chats.Where(c => c.Id.StartsWith(threadIdPrefix))
            .Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var threadStartEntryLids = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var threadSid in threadSids) {
            if (!ThreadChatId.TryParse(threadSid, out var threadChatId))
                continue;

            while (threadChatId.ParentChatId.IsThread(out var parentThreadChatId))
                threadChatId = parentThreadChatId;
            if (threadChatId.ParentChatId == chatId
                && threadChatId.ThreadId > maxClearedEntryLid && threadChatId.ThreadId < firstKeptEntryLid)
                threadStartEntryLids[threadSid] = threadChatId.ThreadId;
        }
        if (threadStartEntryLids.Count == 0)
            return null;

        var candidateSids = threadStartEntryLids.Keys.ToArray();
        var activeThreadSids = await dbContext.ChatEntries
            .Where(e => candidateSids.Contains(e.ChatId) && e.Kind == 0 && !e.IsRemovedAndPurged
                && (e.BeginsAt >= cutoff || e.EndsAt >= cutoff
                    || e.ContentStreamId != null && e.ContentStreamId != ""))
            .Select(e => e.ChatId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        return activeThreadSids.Count == 0 ? null : activeThreadSids.Min(sid => threadStartEntryLids[sid]);
    }

    private IQueryable<DbChatEntry> GetCleanupEntries(
        ChatDbContext dbContext,
        ChatId chatId,
        long maxClearedEntryLid,
        Range<long> wipeEntryLidRange,
        long maxEntryLid)
    {
        var removedBefore = (Clocks.SystemClock.Now - Settings.RemovedEntryRetention).ToDateTime();
        var (wipeStartEntryLid, wipeEndEntryLid) = wipeEntryLidRange;
        return dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0
            && (!e.IsRemovedAndPurged || e.LocalId != maxEntryLid || e.IsThreadStartEntry)
            && ((e.LocalId <= maxClearedEntryLid || e.LocalId >= wipeStartEntryLid && e.LocalId < wipeEndEntryLid)
                    && (e.ContentStreamId == null || e.ContentStreamId == "")
                || e.IsRemoved && !e.IsRemovedAndPurged && (e.RemovedAt == null || e.RemovedAt < removedBefore)
                // A tombstone a newer entry took the high-water mark from
                || e.IsRemovedAndPurged && e.LocalId != maxEntryLid && !e.IsThreadStartEntry));
    }

    private async Task PurgeConversations(
        ChatDbContext dbContext, List<string> conversationIds, CancellationToken cancellationToken)
    {
        foreach (var conversationId in conversationIds) {
            var id = ConversationId.Parse(conversationId);
            await dbContext.CoachConversations
                .Where(c => c.ChatId == id.ChatId.Value && c.StartEntryLid == id.StartEntryLid)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            foreach (var kind in Enum.GetValues<ConversationTranslationIdKind>()) {
                var prefix = TranslationSourceId.New(id, kind).Value + TranslationId.Delimiter;
                await dbContext.Translations.Where(t => t.ChatId == id.ChatId.Value && t.Id.StartsWith(prefix))
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
            var removeConversationCmd = new ConversationBackend_Change(id, null, Change.Remove<ConversationDiff>());
            await Commander.Call(removeConversationCmd, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<MaintenancesBackend_Set?> NewEndRemovalMaintenanceCommand(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var key = chatId.ToMaintenanceKey();
        var maintenance = await MaintenancesBackend.Get(key, cancellationToken).ConfigureAwait(false);
        if (maintenance.OwnerId != RemovalMaintenanceOwnerId)
            return null;

        // A chat of a Place that goes as a whole leaves the maintenance to the root chat
        var target = chatId is PlaceChatId { IsRoot: false } ? chatId.ToMaintenanceTarget() : null;
        return target is null
            ? new MaintenancesBackend_Set(key, MaintenanceMode.None) { OwnerId = RemovalMaintenanceOwnerId }
            : new MaintenancesBackend_Set(key, MaintenanceMode.Removal) {
                OwnerId = RemovalMaintenanceOwnerId,
                TargetDiff = new([], [target]),
            };
    }

    private async Task EndImport(ChatId chatId, CancellationToken cancellationToken)
    {
        // Only an owner can end an import, and the last one is leaving: an import left running would
        // block the cleanup for good. One that also covers chats that stay is left to their owners.
        var key = chatId.ToMaintenanceKey();
        var maintenance = await MaintenancesBackend.Get(key, cancellationToken).ConfigureAwait(false);
        if (maintenance.Mode != MaintenanceMode.Import)
            return;

        var target = chatId.ToMaintenanceTarget();
        if (chatId is PlaceChatId { IsRoot: false } && !maintenance.Targets.SequenceEqual([target!]))
            return;

        var command = new MaintenancesBackend_Set(key, MaintenanceMode.None) { OwnerId = maintenance.OwnerId };
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task RequestSoleOwnedChatsRemoval(
        ChatDbContext dbContext, UserId userId, CancellationToken cancellationToken)
    {
        // Chats already pending removal are requested again: an earlier attempt may have failed
        // after setting their maintenance but before scheduling their purge
        var chatIds = await ListSoleOwnedChatIds(dbContext, userId, true, cancellationToken)
            .ConfigureAwait(false);
        foreach (var chatId in chatIds) {
            await EndImport(chatId, cancellationToken).ConfigureAwait(false);
            if (chatId is PlaceChatId { IsRoot: true } placeRootChatId) {
                await RequestPlaceRemoval(dbContext, placeRootChatId, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var requestRemovalCmd = new ChatsBackend_RequestRemoval(chatId);
            await Commander.Call(requestRemovalCmd, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RequestAuthorEntriesPurge(
        ChatDbContext dbContext, CommandContext context, UserId userId, CancellationToken cancellationToken)
    {
        // Every chat the user's authors can have written to: a chat with its threads, or a whole Place
        // for a Place author. Each one's purge flow gets the entries, posted once this operation commits.
        var authorSids = await dbContext.Authors.Where(a => a.UserId == userId.Value)
            .Select(a => a.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var removedAuthorIds = new HashSet<AuthorId>();
        foreach (var authorSid in authorSids) {
            var authorId = AuthorId.Parse(authorSid);
            var prefix = authorId.ChatId is PlaceChatId { IsRoot: true } placeChatId
                ? PlaceChatId.IdPrefix + placeChatId.PlaceId.Value + "-"
                : authorId.ChatId.Value + ChatId.ThreadIdSeparator;
            var chatSids = await dbContext.Chats
                .Where(c => c.Id == authorId.ChatId.Value || c.Id.StartsWith(prefix))
                .Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var chatSid in chatSids) {
                var chatId = ChatId.Parse(chatSid);
                var mappedAuthorId = ActualChat.Chat.AuthorsBackend.Remap(authorId, chatId);
                if (!removedAuthorIds.Add(mappedAuthorId))
                    continue;

                var flowId = FlowHub.NewId<ChatPurgeFlow>(chatId.Value);
                var removeAuthorEntries = new ChatPurgeFlow.RemoveAuthorEntries(mappedAuthorId);
                context.Operation.AddEvent(Flows_ChangeInbox.Post(flowId, removeAuthorEntries));
            }
        }
    }

    private async Task RequestPlaceRemoval(
        ChatDbContext dbContext, PlaceChatId placeRootChatId, CancellationToken cancellationToken)
    {
        // The Place's maintenance key covers all of its chats, so this one call stops every write
        // in it; the root chat is marked last, as its cleanup is what removes the Place itself
        await StartRemovalMaintenance(placeRootChatId, cancellationToken).ConfigureAwait(false);
        var idPrefix = PlaceChatId.IdPrefix + placeRootChatId.PlaceId.Value + "-";
        var chatSids = await dbContext.Chats
            .Where(c => c.Id.StartsWith(idPrefix) && c.Id != placeRootChatId.Value)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var chatSid in chatSids) {
            var requestChatRemovalCmd = new ChatsBackend_RequestRemoval(ChatId.Parse(chatSid));
            await Commander.Call(requestChatRemovalCmd, cancellationToken).ConfigureAwait(false);
        }
        var requestRootRemovalCmd = new ChatsBackend_RequestRemoval(placeRootChatId);
        await Commander.Call(requestRootRemovalCmd, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> StartRemovalMaintenance(ChatId chatId, CancellationToken cancellationToken)
    {
        // A Place keeps its chats' maintenance on the root key: the root chat covers the whole Place,
        // any other chat is added to the key's targets
        var key = chatId.ToMaintenanceKey();
        var target = chatId is PlaceChatId { IsRoot: false } ? chatId.ToMaintenanceTarget() : null;
        var maintenance = await MaintenancesBackend.Get(key, cancellationToken).ConfigureAwait(false);
        if (maintenance.Mode != MaintenanceMode.None && maintenance.OwnerId != RemovalMaintenanceOwnerId) {
            // Maintenance belongs to the operation that started it, and only that one may change it
            Log.LogWarning(
                "StartRemovalMaintenance: chat #{ChatId} is in {Mode} maintenance owned by '{OwnerId}', so it stays",
                chatId, maintenance.Mode, maintenance.OwnerId);
            return false;
        }
        if (maintenance.Mode != MaintenanceMode.None
            && (maintenance.Targets.IsEmpty || target is not null && maintenance.Targets.Contains(target)))
            return true;

        // Outermost: maintenance lives in another database, so it can't join this operation
        var setMaintenanceCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Removal) {
            OwnerId = RemovalMaintenanceOwnerId,
            TargetDiff = target is null
                ? default
                : new([target]),
        };
        await Commander.Call(setMaintenanceCmd, true, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
