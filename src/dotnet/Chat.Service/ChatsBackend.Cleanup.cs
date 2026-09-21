using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Media;
using ActualChat.Streaming;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    public virtual async Task<long> GetVisibilityBoundary(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId is ThreadChatId threadId) {
            var parentBoundary = await GetVisibilityBoundary(threadId.ParentChatId, cancellationToken)
                .ConfigureAwait(false);
            if (threadId.ThreadId < parentBoundary)
                return long.MaxValue;
        }

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);
        return await dbContext.Chats.Where(c => c.Id == chatId.Value)
            .Select(c => (long?)(c.IsRemoving ? long.MaxValue : c.MinVisibleEntryLid))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? long.MaxValue;
    }

    public virtual async Task<long[]> ListEntryIdsForCleanup(
        ChatId chatId, Range<long> idRange, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, Settings.CleanupBatchSize);
        var boundary = await GetVisibilityBoundary(chatId, cancellationToken).ConfigureAwait(false);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);
        var maxLid = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? 0;
        return await GetCleanupEntries(dbContext, chatId, boundary, maxLid)
            .Where(e => e.LocalId >= idRange.Start && e.LocalId < idRange.End)
            .OrderBy(e => e.LocalId).Take(limit).Select(e => e.LocalId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<long> OnAdvanceVisibilityBoundary(
        ChatsBackend_AdvanceVisibilityBoundary command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = GetVisibilityBoundary(chatId, default);
            _ = Get(chatId, default);
            return default;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(command.MinVisibleEntryLid);
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);
        var dbChat = await dbContext.Chats.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken).ConfigureAwait(false);
        dbChat.Require();
        if (command.MinVisibleEntryLid <= dbChat.MinVisibleEntryLid)
            return dbChat.MinVisibleEntryLid;

        dbChat.RequireVersion(command.ExpectedVersion);
        var maxLid = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? 0;
        if (command.MinVisibleEntryLid > maxLid + 1)
            throw StandardError.Constraint("The visibility boundary cannot exceed the end of the chat.");

        dbChat.MinVisibleEntryLid = command.MinVisibleEntryLid;
        dbChat.Version = VersionGenerator.NextVersion(dbChat.Version);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(FlowHub.NewResumeEvent<ChatCleanupFlow>(chatId.Value));
        return dbChat.MinVisibleEntryLid;
    }

    public virtual async Task OnMarkForRemoval(ChatsBackend_MarkForRemoval command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = Get(chatId, default);
            _ = GetVisibilityBoundary(chatId, default);
            _ = GetPublicChatIdsFor(null, default);
            var invChat = context.Operation.Items.KeylessGet<Chat>();
            if (invChat is { TemplateId: not null, TemplatedForUserId: not null })
                _ = GetTemplatedChatFor(invChat.TemplateId, invChat.TemplatedForUserId, default);
            if (chatId is PlaceChatId placeChatId) {
                _ = GetPublicChatIdsFor(placeChatId.PlaceId, default);
                _ = ListPlaceChatIds(placeChatId.PlaceId, default);
            }
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);
        var dbChat = await dbContext.Chats.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken).ConfigureAwait(false);
        if (dbChat is null || dbChat.IsRemoving)
            return;

        var oldChat = dbChat.ToModel();
        var maxLid = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? 0;
        dbChat.IsRemoving = true;
        dbChat.MinVisibleEntryLid = Math.Max(dbChat.MinVisibleEntryLid, maxLid + 1);
        dbChat.Version = VersionGenerator.NextVersion(dbChat.Version);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(oldChat);
        context.Operation.AddEvent(new ChatChangedEvent(dbChat.ToModel(), oldChat, ChangeKind.Remove));
        context.Operation.AddEvent(FlowHub.NewResumeEvent<ChatCleanupFlow>(chatId.Value));
    }

    public virtual async Task<int> OnCleanup(ChatsBackend_Cleanup command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default;

        var chatId = command.ChatId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);
        var dbChat = await dbContext.Chats.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken).ConfigureAwait(false);
        if (dbChat is null)
            return -1;

        var boundary = Math.Max(dbChat.MinVisibleEntryLid,
            await GetVisibilityBoundary(chatId, cancellationToken).ConfigureAwait(false));
        var maxLid = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? 0;
        if (!dbChat.IsRemoving && dbChat.RetentionPeriod is { } retention) {
            var cutoff = (Clocks.SystemClock.Now - retention).ToDateTime();
            var nextBoundary = await dbContext.ChatEntries
                .Where(e => e.ChatId == chatId.Value && e.Kind == 0 && e.LocalId >= boundary
                    && (e.BeginsAt >= cutoff || e.EndsAt >= cutoff || e.ContentStreamId != null && e.ContentStreamId != ""))
                .MinAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? maxLid + 1;
            var conversationStart = await dbContext.Conversations
                .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < nextBoundary
                    && c.StartEntryLid >= boundary && (c.EndEntryLid >= nextBoundary || c.EndsAt >= cutoff))
                .MinAsync(c => (long?)c.StartEntryLid, cancellationToken).ConfigureAwait(false);
            if (conversationStart is { } start)
                nextBoundary = Math.Min(nextBoundary, start);

            var liveStart = await Services.GetRequiredService<ILiveSessionsBackend>()
                .GetVisibleStartLid(chatId, cancellationToken).ConfigureAwait(false);
            if (liveStart is { } liveLid)
                nextBoundary = Math.Min(nextBoundary, liveLid);
            if (nextBoundary > boundary)
                boundary = await Commander.Call(
                    new ChatsBackend_AdvanceVisibilityBoundary(chatId, nextBoundary),
                    cancellationToken).ConfigureAwait(false);
        }

        var localIds = await GetCleanupEntries(dbContext, chatId, boundary, maxLid)
            .OrderBy(e => e.LocalId).Take(Settings.CleanupBatchSize).Select(e => e.LocalId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var count = localIds.Length == 0 ? 0 : await Commander.Call(
            new ChatsBackend_PurgeEntries(chatId, localIds), cancellationToken).ConfigureAwait(false);

        var conversationIds = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < boundary)
            .OrderBy(c => c.StartEntryLid).Take(Settings.CleanupBatchSize).Select(c => c.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await PurgeConversations(dbContext, conversationIds, cancellationToken).ConfigureAwait(false);

        if (dbChat.IsRemoving && localIds.Length < Settings.CleanupBatchSize
            && conversationIds.Count < Settings.CleanupBatchSize) {
            await Commander.Call(new ChatsBackend_Change(chatId, null, Change.Remove<ChatDiff>()), cancellationToken)
                .ConfigureAwait(false);
            return -1;
        }

        return count + conversationIds.Count;
    }

    public virtual async Task<int> OnPurgeEntries(ChatsBackend_PurgeEntries command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = GetVisibilityBoundary(chatId, default);
            var invalidatedIds = context.Operation.Items.KeylessGet<long[]>() ?? [];
            foreach (var localId in invalidatedIds) {
                InvalidateTiles(chatId, localId, ChangeKind.Remove, false);
                _ = GetEntryAttachments(ChatEntryId.New(chatId, localId), default);
            }
            var locations = Services.GetRequiredService<ISharedLocationsBackend>();
            foreach (var locationId in context.Operation.Items.KeylessGet<string[]>() ?? [])
                _ = locations.Get(SharedLocationId.Parse(locationId), default);
            _ = locations.ListLive(chatId, default);
            _ = GetMinLid(chatId, default);
            _ = GetMaxLid(chatId, false, default);
            _ = GetMaxLid(chatId, true, default);
            return default;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(command.LocalIds.Length, Settings.CleanupBatchSize);
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextDisposer = dbContext.ConfigureAwait(false);
        var dbChat = await dbContext.Chats.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken).ConfigureAwait(false);
        if (dbChat is null)
            return -1;

        var boundary = Math.Max(dbChat.MinVisibleEntryLid,
            await GetVisibilityBoundary(chatId, cancellationToken).ConfigureAwait(false));
        var maxLid = await dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0)
            .MaxAsync(e => (long?)e.LocalId, cancellationToken).ConfigureAwait(false) ?? 0;
        var eligibleEntries = GetCleanupEntries(dbContext, chatId, boundary, maxLid);
        if (command.RemovedUserId is { } userId) {
            var candidates = dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0
                && !e.IsPurged && command.LocalIds.Contains(e.LocalId));
            var authorSids = await candidates.Select(e => e.AuthorId).Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var ownedAuthorSids = new List<string>();
            foreach (var authorSid in authorSids) {
                var author = await AuthorsBackend.Get(chatId, AuthorId.Parse(authorSid),
                    RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
                if (author?.UserId == userId)
                    ownedAuthorSids.Add(authorSid);
            }
            eligibleEntries = candidates.Where(e => ownedAuthorSids.Contains(e.AuthorId));
        }
        var eligibleIds = eligibleEntries.Select(e => e.Id);
        var entries = await dbContext.ChatEntries.ForUpdate()
            .Where(e => command.LocalIds.Contains(e.LocalId) && eligibleIds.Contains(e.Id)).OrderBy(e => e.LocalId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var legacyEntries = command.RemovedUserId is null
            ? entries.Where(e => e.LocalId >= boundary && e.RemovedAt == null).ToArray()
            : [];
        foreach (var entry in legacyEntries)
            entry.RemovedAt = Clocks.SystemClock.Now.ToDateTime();
        entries.RemoveAll(e => legacyEntries.Contains(e));

        var entryIds = entries.Select(e => e.Id).ToArray();
        var localIds = entries.Select(e => e.LocalId).ToArray();
        if (command.RemovedUserId is not null && entryIds.Length > 0) {
            var conversationIds = await dbContext.Conversations.Where(c => c.ChatId == chatId.Value
                    && dbContext.ChatEntries.Any(e => entryIds.Contains(e.Id)
                        && e.LocalId >= c.StartEntryLid && e.LocalId <= c.EndEntryLid))
                .Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            await PurgeConversations(dbContext, conversationIds, cancellationToken).ConfigureAwait(false);
        }
        foreach (var entry in entries.Where(e => e.IsThreadStartEntry)) {
            var threadId = chatId.CreateThreadId(entry.LocalId);
            await Commander.Call(new ChatsBackend_MarkForRemoval(threadId), cancellationToken).ConfigureAwait(false);
        }

        var attachments = await dbContext.ChatEntryAttachments.Where(a => entryIds.Contains(a.EntryId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var dubMediaIds = await dbContext.Translations.Where(t => entryIds.Contains(t.EntryId!))
            .Select(t => t.DubMediaId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var mediaIds = attachments.SelectMany(a => new[] { a.MediaId, a.ThumbnailMediaId })
            .Concat(dubMediaIds).Concat(entries.Select(e => e.ToModel().Audio?.MediaId?.Value))
            .Where(id => !id.IsNullOrEmpty()).Distinct().ToArray();
        foreach (var mediaSid in mediaIds) {
            var mediaId = MediaId.Parse(mediaSid);
            if (mediaId.Scope != chatId.Value) {
                var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
                if (media?.Kind is not (MediaKind.ChatEntryAudio or MediaKind.ChatEntryVideo or MediaKind.ChatEntryAttachment))
                    continue;
            }

            var isReferenced = await dbContext.ChatEntryAttachments.AnyAsync(
                a => !entryIds.Contains(a.EntryId) && (a.MediaId == mediaSid || a.ThumbnailMediaId == mediaSid),
                cancellationToken).ConfigureAwait(false);
            if (isReferenced
                || await dbContext.ChatEntries.AnyAsync(e => e.AudioId == mediaSid && !entryIds.Contains(e.Id),
                    cancellationToken).ConfigureAwait(false)
                || await dbContext.Translations.AnyAsync(t => t.DubMediaId == mediaSid && !entryIds.Contains(t.EntryId!),
                    cancellationToken).ConfigureAwait(false)
                || await dbContext.Chats.AnyAsync(c => c.MediaId == mediaSid, cancellationToken)
                    .ConfigureAwait(false))
                continue;

            context.Operation.AddEvent(new MediaBackend_Change(mediaId, null, Change.Remove<MediaFull>()));
        }

        var locationIds = entries.Select(e => e.LocationId).Where(id => !id.IsNullOrEmpty()).Distinct().ToArray();
        foreach (var locationId in locationIds) {
            if (await dbContext.ChatEntries.AnyAsync(e => e.LocationId == locationId && !entryIds.Contains(e.Id),
                    cancellationToken).ConfigureAwait(false))
                continue;

            await dbContext.SharedLocations.Where(l => l.Id == locationId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        context.Operation.Items.KeylessSet(locationIds);

        foreach (var entry in entries)
            await Commander.Call(
                new ChatsBackend_RemoveAttachments(ChatEntryId.New(chatId, entry.LocalId)), cancellationToken)
                .ConfigureAwait(false);

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
        if (entries.Count > 0) {
            var ids = entries.Select(e => ChatEntryId.New(chatId, e.LocalId)).ToArray();
            await Commander.Call(new ChatsBackend_UpdateChatVisualMediaIndex(chatId, ids, []), cancellationToken)
                .ConfigureAwait(false);
            await Commander.Call(new ChatsBackend_UpdateChatFileIndex(chatId, ids, []), cancellationToken)
                .ConfigureAwait(false);
            await Commander.Call(new ChatsBackend_UpdateChatLinkIndex(chatId, ids, []), cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var entry in entries) {
            if (entry.LocalId != maxLid) {
                dbContext.Remove(entry);
                continue;
            }

            // The allocator recovers its high-water mark from this content-free tombstone after Redis loss.
            entry.UpdateFrom(new TextEntry(ChatEntryId.New(chatId, maxLid), VersionGenerator.NextVersion()) {
                AuthorId = AuthorId.Parse(entry.AuthorId),
                BeginsAt = entry.BeginsAt,
                IsRemoved = true,
            });
            entry.HasAttachments = false;
            entry.IsPurged = true;
            entry.RemovedAt = Clocks.SystemClock.Now.ToDateTime();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(localIds);
        if (localIds.Length > 0)
            context.Operation.AddEvent(new ChatEntriesPurgedEvent(chatId, localIds));

        return entries.Count + legacyEntries.Length;
    }

    // Private methods

    private async Task PurgeConversations(
        ChatDbContext dbContext, List<string> conversationIds, CancellationToken cancellationToken)
    {
        foreach (var conversationId in conversationIds) {
            var id = ConversationId.Parse(conversationId);
            foreach (var kind in Enum.GetValues<ConversationTranslationIdKind>()) {
                var prefix = TranslationSourceId.New(id, kind).Value + TranslationId.Delimiter;
                await dbContext.Translations.Where(t => t.ChatId == id.ChatId.Value && t.Id.StartsWith(prefix))
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
            await Commander.Call(new ConversationBackend_Change(id, null, Change.Remove<ConversationDiff>()),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private IQueryable<DbChatEntry> GetCleanupEntries(ChatDbContext dbContext, ChatId chatId, long boundary, long maxLid)
    {
        var removedBefore = (Clocks.SystemClock.Now - Settings.RemovedEntryRetention).ToDateTime();
        return dbContext.ChatEntries.Where(e => e.ChatId == chatId.Value && e.Kind == 0
            && (!e.IsPurged || e.LocalId != maxLid)
            && (e.LocalId < boundary || e.IsRemoved && (e.RemovedAt == null || e.RemovedAt < removedBefore)));
    }
}
