using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    public virtual async Task<ChatBackend_CopyChatResult> OnCopyChat(
        ChatBackend_CopyChat command,
        CancellationToken cancellationToken)
    {
        var (chatId, placeId, correlationId) = command;
        var localChatId = chatId is PlaceChatId placeChatId
            ? placeChatId.LocalChatId
            : chatId.Id.Value;
        var newChatId = PlaceChatId.Parse(PlaceChatId.Format(placeId, localChatId));
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            _ = GetPublicChatIdsFor(placeId, default);
            if (context.Operation.Items[typeof(ChatEntryId)] is string invLastEntrySid) {
                Log.LogInformation("OnCopyChat({CorrelationId}): InvLastEntrySid is {EntrySid}", correlationId, invLastEntrySid);
                InvalidateTiles(newChatId,
                    ChatEntryId.Parse(invLastEntrySid).LocalId,
                    ChangeKind.Create,
                    false);
                _ = GetMinLid(newChatId, default);
                _ = GetMaxLid(newChatId, true, default);
                _ = GetMaxLid(newChatId, false, default);
            }
            return default!;
        }

        Log.LogInformation("-> OnCopyChat({CorrelationId}): coping chat '{ChatId}' to place '{PlaceId}'",
            correlationId, chatId.Value, placeId);

        var ctRegistration = cancellationToken.Register(() => {
            Log.LogWarning("OnCopyChat({CorrelationId}): canceled from `{StackTrace}`",
                correlationId, Environment.StackTrace);
        });
        await using var ___ = ctRegistration.ConfigureAwait(false);

        var hasChanges = false;
        var chatSid = chatId.Value;
        var commandTimeout = TimeSpan.FromSeconds(30);

        MigratedAuthors migratedAuthors;
        var entryLidRange = new Range<long>();
        (RoleId Id, RoleId NewId)[] rolesMap;
        long maxAuthorLocalId;
        ChatCopyState chatCopyState;

        {
            var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
            dbContext.Database.SetCommandTimeout(commandTimeout);
            await using var __ = dbContext.ConfigureAwait(false);

            var chat = await Get(chatId, cancellationToken).Require().ConfigureAwait(false);
            var newChat = await CreateOrUpdateChat(correlationId, dbContext, newChatId, chat, cancellationToken).ConfigureAwait(false);

            var chatCopyState1 = await GetChatCopyState(newChatId, cancellationToken).ConfigureAwait(false);
            if (chatCopyState1 != null)
                chatCopyState = chatCopyState1;
            else
                chatCopyState = await Commander.Call(new ChatsBackend_ChangeChatCopyState(newChatId,
                            null,
                            Change.Create(new ChatCopyStateDiff {
                                SourceChatId = chat.Id,
                            })),
                        true,
                        cancellationToken)
                    .ConfigureAwait(false);

            var sourceChatRange = await GetLidRange(chatId, true, cancellationToken).ConfigureAwait(false);
            if (!sourceChatRange.IsEmpty) {
                var newChatRange = await GetLidRange(newChatId, true, cancellationToken).ConfigureAwait(false);
                var startEntryId = !newChatRange.IsEmpty ? newChatRange.End : 1;
                var endEntryId = sourceChatRange.End;
                if (endEntryId > startEntryId)
                    entryLidRange = new Range<long>(startEntryId, endEntryId);
            }

            Log.LogInformation("OnCopyChat({CorrelationId}: text range is [{Start},{End})",
                correlationId, entryLidRange.Start, entryLidRange.End);

            var migratedRoles = new List<MigratedRole>();
            var hasChanges1 = await CreateOrUpdateRoles(dbContext,
                    chatSid,
                    newChat,
                    correlationId,
                    migratedRoles,
                    cancellationToken)
                .ConfigureAwait(false);

            maxAuthorLocalId = dbContext.Authors.Where(c => c.ChatId == chatSid).Max(c => c.LocalId);
            rolesMap = migratedRoles.Select(c => (c.OldRole.Id, c.NewRole.Id)).ToArray();
            hasChanges |= hasChanges1;
        }
        {
            var hasChanges2 = await Commander
                .Call(new AuthorsBackend_CopyChat(chatId, newChatId, rolesMap, correlationId), true, cancellationToken)
                .ConfigureAwait(false);
            hasChanges |= hasChanges2;
        }
        {
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            dbContext.Database.SetCommandTimeout(commandTimeout);
            migratedAuthors = await GetAuthorsMap(Log,
                    dbContext,
                    chatSid,
                    newChatId,
                    maxAuthorLocalId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // if (textEntryRange.Start > 1) {
        //      TODO: update last N records from previous insert
        // }

        var proceed = true;
        var hasErrors = false;
        var lastProcessedEntryId = (ChatEntryId?)null;
        if (!entryLidRange.IsEmpty) {
            var startEntryId = entryLidRange.Start;
            var copyContext = new CopyChatEntriesContext(chatId, newChatId, correlationId, migratedAuthors);
            const int batchLimit = 500;

            while (proceed) {
                var batchRange = new Range<long>(startEntryId, entryLidRange.End);
                try {
                    var result = await CopyChatEntries(copyContext,
                            batchRange,
                            batchLimit,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (result.ProcessedChatEntryCount == 0)
                        proceed = false;
                    else {
                        hasChanges = true;
                        startEntryId = (result.LastProcessedEntryId?.LocalId ?? 0) + 1;
                        lastProcessedEntryId = result.LastProcessedEntryId;
                    }
                }
                catch (Exception e) {
                    proceed = false;
                    hasErrors = true;
                    Log.LogWarning(e,
                        "OnCopyChat({CorrelationId}): failed to proceed chat entries insertion for range [{Start}, {End})",
                        correlationId, batchRange.Start, batchRange.End);
                }
            }
        }
        {
            await Commander.Call(new ChatsBackend_ChangeChatCopyState(chatCopyState.Id,
                        chatCopyState.Version,
                        Change.Update(new ChatCopyStateDiff {
                            IsCopiedSuccessfully = !hasErrors,
                            LastCorrelationId = correlationId,
                            LastProcessedEntryId = lastProcessedEntryId?.LocalId ?? 0L,
                        })),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        if (lastProcessedEntryId is not null) {
            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            dbContext.Database.SetCommandTimeout(commandTimeout);
            await using var __ = dbContext.ConfigureAwait(false);
            Log.LogInformation(
                "OnCopyChat({CorrelationId}): LastProcessedEntryId is {EntryId}", correlationId, lastProcessedEntryId);
            context.Operation.Items[typeof(ChatEntryId)] = lastProcessedEntryId.Value;
        }

        Log.LogInformation(
            "<- OnCopyChat({CorrelationId})", correlationId);
        return new ChatBackend_CopyChatResult(hasChanges, hasErrors, lastProcessedEntryId?.LocalId ?? 0L);
    }

    private async Task<Chat> CreateOrUpdateChat(string correlationId, ChatDbContext dbContext, ChatId newChatId, Chat chat, CancellationToken cancellationToken)
    {
        var chatSid = newChatId.Value;
        var dbChat = await dbContext.Chats
            .Where(c => c.Id == chatSid)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        // Make chat private, so only the owner who executed copying can see the chat.
        // Publishing a copied chat command will set the actual value of IsPublic property later.
        var newChatMediaId = chat.MediaId != null
            ? MediaId.New(newChatId.Value, chat.MediaId.LocalId)
            : null;
        var copyMedia = newChatMediaId != null && (dbChat == null || newChatMediaId.Value != dbChat.MediaId);
        if (copyMedia)
            await Commander
                .Call(new MediaBackend_CopyChat(newChatId, correlationId, [chat.MediaId!]), true, cancellationToken)
                .ConfigureAwait(false);
        chat = chat with {
            IsPublic = false,
            MediaId = newChatMediaId
        };
        Chat newChat;
        if (dbChat == null) {
            newChat = chat with { Id = newChatId };
            dbContext.Chats.Add(new DbChat(newChat));
        }
        else {
            dbChat.Title = chat.Title;
            dbChat.Description = chat.Description;
            dbChat.IsPublic = chat.IsPublic;
            dbChat.MediaId = chat.MediaId?.Value ?? "";
            dbChat.Version = chat.Version;
            newChat = dbChat.ToModel();
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation(
            "OnCopyChat({CorrelationId}): updated chat record", correlationId);
        return newChat;
    }

    private async Task<bool> CreateOrUpdateRoles(
        ChatDbContext dbContext,
        string chatSid,
        Chat newChat,
        string correlationId,
        ICollection<MigratedRole> migratedRoles,
        CancellationToken cancellationToken)
    {
        var hasChanges = false;
        var dbRoles = await dbContext.Roles
            .Where(c => c.ChatId == chatSid)
            .OrderBy(c => c.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbRole in dbRoles) {
            var originalRole = dbRole.ToModel();
            var newRoleId = RoleId.Parse(RoleId.Format(newChat.Id, dbRole.LocalId));
            var newRoleSid = newRoleId.Value;
            var existentNewRole = await dbContext.Roles
                .Where(c => c.Id == newRoleSid)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var newRole = originalRole with { Id = newRoleId };
            if (existentNewRole == null) {
                dbContext.Roles.Add(new DbRole(newRole));
                hasChanges = true;
            } else {
                if (existentNewRole.SystemRole != originalRole.SystemRole)
                    throw StandardError.Constraint("Can't proceed migration. Role 'SystemRole' property has changed. "
                        + $"Expected value: '{originalRole.SystemRole}', but actual value: '{existentNewRole.SystemRole}'.");
                if (existentNewRole.Name != originalRole.Name)
                    throw StandardError.Constraint("Can't proceed migration. Role 'Name' property has changed. "
                        + $"Expected value: '{originalRole.Name}', but actual value: '{existentNewRole.Name}'.");
                existentNewRole.UpdateFrom(newRole);
            }

            migratedRoles.Add(new MigratedRole(originalRole, newRole));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation(
            "OnCopyChat({CorrelationId}): updated {Count} role records",
            correlationId, dbRoles.Count);
        return hasChanges;
    }

    private static async Task<MigratedAuthors> GetAuthorsMap(
        ILogger log,
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        long maxAuthorLocalId,
        CancellationToken cancellationToken)
    {
        var newChatSid = newChatId.Value;

        var dbAuthors = await dbContext.Authors
            .Where(c => c.ChatId == chatSid && c.LocalId <= maxAuthorLocalId)
            .OrderBy(c => c.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var newDbAuthors = await dbContext.Authors
            .Where(c => c.ChatId == newChatSid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var newDbAuthorByUserId = newDbAuthors
            .Where(c => !c.UserId.IsNullOrEmpty())
            .ToDictionary(c => c.UserId!);

        var migratedAuthors = new MigratedAuthors();

        foreach (var dbAuthor in dbAuthors) {
            var originalAuthor = dbAuthor.ToModel();
            if (originalAuthor.UserId is not { } userId)
                throw StandardError.Internal(
                    $"Can't proceed with the migration: found an author with no associated user. AuthorId is '{dbAuthor.Id}'.");

            if (originalAuthor.IsAnonymous) {
                var authorSid = originalAuthor.Id.Value;
                var hasChatEntries = await dbContext.ChatEntries
                    .Where(c => c.ChatId == chatSid && c.AuthorId == authorSid)
                    .AnyAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!hasChatEntries)
                    log.LogWarning(
                        "Anonymous author will be registered as removed. Original author is {OriginalAuthor}",
                        originalAuthor);
                migratedAuthors.RegisterRemoved(originalAuthor);
                continue; // Skip the anonymous author if there were no messages from them.
            }

            if (!newDbAuthorByUserId.TryGetValue(userId.Value, out var newDbAuthor))
                throw StandardError.Internal(
                    $"Can't proceed with the migration: No migrated author found for user with id '{userId}'.");

            migratedAuthors.RegisterMigrated(originalAuthor, AuthorId.Parse(newDbAuthor.Id));
        }

        return migratedAuthors;
    }

    private async Task<CopyChatEntriesResult> CopyChatEntries(
        CopyChatEntriesContext context,
        Range<long> entryLidRange,
        int batchLimit,
        CancellationToken cancellationToken)
    {
        if (entryLidRange.IsEmpty)
            return new CopyChatEntriesResult(0, null);

        Log.LogInformation(
            "-> CopyChatEntries({CorrelationId}), entry Id range is [{Start},{End})",
            context.CorrelationId, entryLidRange.Start, entryLidRange.End);

        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await using var _ = dbContext.ConfigureAwait(false);
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var __ = transaction.ConfigureAwait(false);

        var result = await CopyChatEntries(dbContext,
                context,
                entryLidRange,
                batchLimit,
                cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        Log.LogInformation(
            "<- CopyChatEntries({CorrelationId}), entry Id range is [{Start},{End})",
            context.CorrelationId, entryLidRange.Start, entryLidRange.End);

        return result;
    }

    private async Task<CopyChatEntriesResult> CopyChatEntries(
        ChatDbContext dbContext,
        CopyChatEntriesContext context,
        Range<long> entryLidRange,
        int batchLimit,
        CancellationToken cancellationToken)
    {
        var correlationId = context.CorrelationId;
        var chatSid = context.ChatId.Value;
        var newChatId = context.NewChatId;
        var migratedAuthors = context.MigratedAuthors;
        var mentionExtractor = MentionExtractor.Instance;

        var mentionUpdatesInsideContent = 0;
        var mentionUpdatesInSystemEntries = 0;

        var minLocalId = entryLidRange.Start;
        var maxLocalId = entryLidRange.End;
        var attachmentIds = new List<long>();
        var reactionIds = new List<long>();

        var entries = await dbContext.ChatEntries
            .Where(c => c.ChatId == chatSid && c.Kind == 0)
            .Where(c => c.LocalId >= minLocalId && c.LocalId < maxLocalId)
            .OrderBy(c => c.LocalId)
            .Take(batchLimit)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
            return new CopyChatEntriesResult(0, null);

        var lastFetchedEntry = entries[^1];

        Log.LogInformation(
            "OnCopyChat({CorrelationId}): about to process chat entry range [{From},{To})",
            correlationId, minLocalId, lastFetchedEntry.LocalId + 1);

        var chatEntryWithMentionIds = new List<long>();
        await InsertMentions(dbContext,
                chatSid,
                newChatId,
                correlationId,
                new Range<long>(entryLidRange.Start, lastFetchedEntry.LocalId + 1),
                migratedAuthors,
                chatEntryWithMentionIds,
                cancellationToken)
            .ConfigureAwait(false);

        DbChatEntry? dbLastProcessedEntry = null;

        foreach (var dbChatEntry in entries) {
            var newEntryId = ChatEntryId.New(newChatId, dbChatEntry.LocalId);
            dbChatEntry.Id = newEntryId.Value;
            dbChatEntry.ChatId = newChatId.Value;

            var authorSid = dbChatEntry.AuthorId;
            var authorId = AuthorId.Parse(authorSid);
            AuthorId newAuthorId;
            if (authorId.LocalId > 0) {
                var migratedAuthor = migratedAuthors.RequireMigratedAuthor(authorSid);
                if (migratedAuthor.NewAuthorId is null) {
                    Log.LogWarning(
                        "OnCopyChat({CorrelationId}): skipping chat entry {ChatEntryId}: the author {AuthorSid} is marked as removed",
                        correlationId, dbChatEntry.Id, authorId);
                    continue;
                }
                newAuthorId = migratedAuthor.NewAuthorId;
            }
            else if (authorId.LocalId == Constants.User.Walle.AuthorLocalId ||
                     authorId.LocalId == Constants.User.Sherlock.AuthorLocalId)
                newAuthorId = AuthorId.New(newChatId, authorId.LocalId);
            else
                throw StandardError.Internal($"Unexpected author's local ID: {authorId.LocalId}.");

            dbChatEntry.AuthorId = newAuthorId.Value;

            if (chatEntryWithMentionIds.Contains(dbChatEntry.LocalId)) {
                var content = UpdateMentionsInContent(context.ChatId, dbChatEntry.Content, migratedAuthors, mentionExtractor);
                if (content != dbChatEntry.Content) {
                    dbChatEntry.Content = content;
                    mentionUpdatesInsideContent++;
                }
            }

            if (dbChatEntry.IsSystemEntry && dbChatEntry.ToModel() is MembersChangedEntry membersChangedEntry) {
                var changeAuthorId = membersChangedEntry.TargetAuthorId;
                if (changeAuthorId != null) {
                    var isRemoved = migratedAuthors.IsRemoved(changeAuthorId.Value);
                    if (isRemoved)
                        continue;

                    var changeNewAuthorId = migratedAuthors.GetNewAuthorId(changeAuthorId);
                    var updatedEntry = membersChangedEntry with {
                        TargetAuthorId = changeNewAuthorId,
                    };
                    dbChatEntry.UpdateFrom(updatedEntry);
                    mentionUpdatesInSystemEntries++;
                }
            }

            dbContext.ChatEntries.Add(dbChatEntry);
            dbLastProcessedEntry = dbChatEntry;

            if (dbChatEntry.HasAttachments)
                attachmentIds.Add(dbChatEntry.LocalId);
            if (dbChatEntry.HasReactions)
                reactionIds.Add(dbChatEntry.LocalId);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (attachmentIds.Count > 0)
            await InsertChatEntryAttachments(correlationId, dbContext, chatSid, newChatId, attachmentIds, cancellationToken).ConfigureAwait(false);

        if (reactionIds.Count > 0)
            await InsertReactions(correlationId, dbContext, chatSid, newChatId, reactionIds, migratedAuthors, cancellationToken).ConfigureAwait(false);

        Log.LogInformation(
            "OnCopyChat({CorrelationId}): inserted {Count} chat entry records with local Ids [{From},{To})",
            correlationId,
            entries.Count,
            minLocalId,
            maxLocalId);

        if (mentionUpdatesInsideContent > 0)
            Log.LogInformation(
                "OnCopyChat({CorrelationId}): updated author mentions inside DbChatEntry.Content, {Count} records affected",
                correlationId, mentionUpdatesInsideContent);

        if (mentionUpdatesInSystemEntries > 0)
            Log.LogInformation(
                "OnCopyChat({CorrelationId}): updated TargetAuthorId inside MembersChangedEntry records, {Count} records affected",
                correlationId, mentionUpdatesInSystemEntries);

        var lastProcessedEntryId = ChatEntryId.ParseNullable(dbLastProcessedEntry?.Id);
        return new CopyChatEntriesResult(entries.Count, lastProcessedEntryId);
    }

    private string UpdateMentionsInContent(ChatId chatId, string content, MigratedAuthors migratedAuthors,
        MentionExtractor mentionExtractor)
    {
        var markup = MarkupParser.Parse(content);
        var mentionIds = mentionExtractor.GetMentionIds(markup);
        foreach (var mentionId in mentionIds) {
            if (mentionId.TargetId is not AuthorId authorId)
                continue;
            if (authorId.ChatId != chatId)
                continue;

            var newAuthorId = migratedAuthors.GetNewAuthorId(authorId);
            var newMentionId = MentionId.NewAuthor(newAuthorId);
            content = content.Replace(mentionId.Id.Value, newMentionId.Id.Value);
        }
        return content;
    }

    private async Task InsertChatEntryAttachments(
        string correlationId,
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        List<long> attachmentsIds,
        CancellationToken cancellationToken)
    {
        if (attachmentsIds.Count == 0)
            return;

        var firstId = attachmentsIds[0];
        var lastId = attachmentsIds[^1];
        var attachmentIdPrefix = chatSid + ":0:";
        List<string> ids = attachmentsIds.Select(entryId => attachmentIdPrefix + entryId + ":").ToList();
        var attachments = await dbContext.ChatEntryAttachments
            .Where(c => c.Id.StartsWith(attachmentIdPrefix))
 #pragma warning disable CA1310
            .Where(c => ids.Any(x => c.Id.StartsWith(x)))
 #pragma warning restore CA1310
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mediaIds = attachments
            .Select(c => c.MediaId)
            .Where(c => !c.IsNullOrEmpty())
            .Select(MediaId.Parse)
            .ToArray();

        var thumbnailMediaIds = attachments
            .Select(c => c.ThumbnailMediaId)
            .Where(c => !c.IsNullOrEmpty())
            .Select(MediaId.Parse)
            .ToArray();

        var allMediaIdToCopy = mediaIds.Concat(thumbnailMediaIds).Where(CanRemapMedia).Distinct().ToArray();

        await Commander.Call(new MediaBackend_CopyChat(newChatId, correlationId, allMediaIdToCopy), true, cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbAttachment in attachments) {
            var entryId = ChatEntryId.Parse(dbAttachment.EntryId);
            var newEntryId = ChatEntryId.New(newChatId, entryId.LocalId);
            dbAttachment.Id = DbChatEntryAttachment.ComposeId(newEntryId, dbAttachment.Index);
            dbAttachment.EntryId = newEntryId.Value;
            dbAttachment.MediaId = RemapMedia(dbAttachment.MediaId);
            dbAttachment.ThumbnailMediaId = RemapMedia(dbAttachment.ThumbnailMediaId);
            dbContext.ChatEntryAttachments.Add(dbAttachment);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Log.LogInformation(
            "OnCopyChat({CorrelationId}): inserted {Count} text entry attachment records for entries from range [{From},{To})",
            correlationId, attachments.Count, firstId, lastId + 1);
        return;

        bool CanRemapMedia(MediaId mediaId)
             => mediaId.Scope == chatSid;

        string RemapMedia(string mediaSid)
        {
            var mediaId = MediaId.ParseNullable(mediaSid);
            return mediaId != null && CanRemapMedia(mediaId)
                ? MediaId.New(newChatId.Value, mediaId.LocalId).Value
                : "";
        }
    }

    private async Task InsertReactions(
        string correlationId,
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        List<long> reactionIds,
        MigratedAuthors migratedAuthors,
        CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(chatSid);
        var reactionIdPrefix = chatSid + ":0:";
        var ids = reactionIds.Select(c => reactionIdPrefix + c + ":").ToList();

        var reactions = await dbContext.Reactions
            .Where(c => c.Id.StartsWith(reactionIdPrefix))
 #pragma warning disable CA1310
            .Where(c => ids.Any(x => c.Id.StartsWith(x)))
 #pragma warning restore CA1310
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbReaction in reactions) {
            var authorSid = dbReaction.AuthorId;
            var migratedAuthor = migratedAuthors.RequireMigratedAuthor(authorSid);
            if (migratedAuthor.NewAuthorId is null) {
                Log.LogWarning("OnCopyChat({CorrelationId}): skipping reaction {ReactionId} because the author {AuthorSid} is marked as removed",
                     correlationId, dbReaction.Id, authorSid);
                continue;
            }
            var newAuthorId = migratedAuthor.NewAuthorId;
            dbReaction.AuthorId = newAuthorId.Value;

            var entryId = ChatEntryId.Parse(dbReaction.EntryId);
            var newEntryId = ChatEntryId.New(newChatId, entryId.LocalId);
            dbReaction.EntryId = newEntryId.Value;
            dbReaction.Id = DbReaction.ComposeId(newEntryId, newAuthorId);

            dbContext.Reactions.Add(dbReaction);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("OnCopyChat({CorrelationId}): inserted {Count} reaction records",
            correlationId, reactions.Count);

        var reactionSummaries = await dbContext.ReactionSummaries
            .Where(c => c.Id.StartsWith(chatSid))
 #pragma warning disable CA1310
            .Where(c => ids.Any(x => c.Id.StartsWith(x)))
 #pragma warning restore CA1310
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbSummary in reactionSummaries) {
            var entryId = ChatEntryId.Parse(dbSummary.EntryId);
            var newEntryId = ChatEntryId.New(newChatId, entryId.LocalId);
            dbSummary.EntryId = newEntryId.Value;
            dbSummary.Id = DbReactionSummary.ComposeId(newEntryId, dbSummary.Emoji);

            var summary = dbSummary.ToModel();
            var authorIds = summary.FirstAuthorIds;

            var newAuthorIds = ImmutableList<AuthorId>.Empty;
            var isCorrupted = false;
            foreach (var authorId in authorIds) {
                if (authorId.ChatId != chatId) {
                    isCorrupted = true;
                    // During original migration 'Actual Chat - Dev' chat to 'Actual chat' place FirstAuthorIds collection transformation
                    // was not performed properly. Ignore these records.
                    Log.LogWarning(
                        "OnCopyChat({CorrelationId}) reaction summary for entry {Id} references invalid AuthorId: {AuthorId}",
                        correlationId, entryId, authorId);
                    continue;
                }
                var migratedAuthor = migratedAuthors.RequireMigratedAuthor(authorId.Value);
                if (migratedAuthor.NewAuthorId is null) {
                    Log.LogWarning(
                        "OnCopyChat({CorrelationId}): excluding author for reaction summary {ReactionSummaryId}: author {AuthorSid} is marked as removed",
                        correlationId, dbSummary.Id, authorId);
                    continue;
                }
                var newAuthorId = migratedAuthor.NewAuthorId;
                newAuthorIds = newAuthorIds.Add(newAuthorId);
            }
            if (isCorrupted)
                continue;

            if (newAuthorIds.Count == 0) {
                Log.LogWarning(
                    "OnCopyChat({CorrelationId}): skipping reaction summary {ReactionSummaryId}: author list is empty",
                    correlationId, dbSummary.Id);
                continue;
            }
            summary = summary with { FirstAuthorIds = newAuthorIds };
            dbSummary.UpdateFrom(summary);

            dbContext.ReactionSummaries.Add(dbSummary);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Log.LogInformation(
            "OnCopyChat({CorrelationId}): updated AuthorIds for {Count} reaction summary records",
            correlationId, reactionSummaries.Count);
    }

    private async Task InsertMentions(
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        string correlationId,
        Range<long> range,
        MigratedAuthors migratedAuthors,
        ICollection<long> entryIdsCollector,
        CancellationToken cancellationToken)
    {
        var maxLocalId = range.End - 1;
        var minLocalId = range.Start;
        var newChatSid = newChatId.Value;
        var chatId = ChatId.Parse(chatSid);

        var mentions = await dbContext.Mentions
            .Where(c => c.ChatId == chatSid)
            .Where(c => c.EntryLid >= minLocalId && c.EntryLid <= maxLocalId)
            .OrderBy(c => c.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (mentions.Count == 0)
            return;

        const string mentionIdAuthorPrefix = "a:";
        foreach (var mention in mentions) {
            mention.ChatId = newChatSid;
            var mentionSid = mention.MentionId;
            MentionId mentionId;
            if (mentionSid.StartsWith(mentionIdAuthorPrefix)) {
                var authorSid = mentionSid.Substring(mentionIdAuthorPrefix.Length);
                if (!AuthorId.TryParse(authorSid, out var tempAuthorId)) {
                    Log.LogWarning("OnCopyChat({CorrelationId}): skipping mention {MentionId}: invalid AuthorId",
                        correlationId, mention.Id);
                    continue;
                }
                authorSid = FixMentionAuthorSid(mention, authorSid);
                if (tempAuthorId.ChatId == chatId) {
                    var migratedAuthor = migratedAuthors.GetMigratedAuthor(authorSid);
                    if (migratedAuthor == null) {
                        Log.LogWarning(
                            "OnCopyChat({CorrelationId}): skipping mention {MentionId}: copied author not found",
                            correlationId, mention.Id);
                        continue;
                    }
                    if (migratedAuthor.NewAuthorId is null) {
                        Log.LogWarning(
                            "OnCopyChat({CorrelationId}): skipping mention {MentionId}: copied author is marked as removed",
                            correlationId, mention.Id);
                        continue;
                    }
                    var newAuthorId = migratedAuthor.NewAuthorId;
                    mentionId = MentionId.NewAuthor(newAuthorId);
                }
                else {
                    mentionId = MentionId.NewAuthor(AuthorId.Parse(authorSid));
                    Log.LogWarning(
                        "OnCopyChat({CorrelationId}): another author's mention with Id {MentionId} is found",
                        correlationId, mention.Id);
                }
            }
            else {
                if (!MentionId.TryParse(mentionSid, out var parsedMentionId)) {
                    Log.LogWarning(
                        "OnCopyChat({CorrelationId}): skipping mention {MentionId}: invalid MentionId",
                        correlationId, mention.Id);
                    continue;
                }
                mentionId = parsedMentionId;
            }
            mention.MentionId = mentionId.Value;
            mention.Id = DbMention.ComposeId(ChatEntryId.New(newChatId, mention.EntryLid), mentionId);
            dbContext.Mentions.Add(mention);
            entryIdsCollector.Add(mention.EntryLid);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("OnCopyChat({CorrelationId}): updated ChatId and Id for {Count} mention records",
            correlationId, mentions.Count);

        string FixMentionAuthorSid(DbMention mention, string authorSid)
        {
            if (mention.Id.EndsWith(authorSid))
                return authorSid;

            // At some point DbMention.AuthorId field (later renamed to MentionId) mistakenly hold value of author of text entry instead of mentioned author.
            // Fixed in https://github.com/Actual-Chat/actual-chat/commit/de35ccfe1f756546bdb68c7ada016791773637a1
            // Extract mention_id from id.
            var parts = mention.Id.Split(':');
            var hasFixedMentionId = false;
            if ((parts.Length == 5 || parts.Length == 6) && parts[^3] == "a") {
                var authorChatSid = parts[^2];
                var authorLocalSid = parts[^1];
                if (ChatId.TryParse(authorChatSid, out var authorChatId)
                    && long.TryParse(authorLocalSid, out var authorLocalId)) {
                    var authorId = AuthorId.New(authorChatId, authorLocalId);
                    authorSid = authorId.Value;
                    hasFixedMentionId = true;
                }
            }
            if (!hasFixedMentionId)
                throw StandardError.Constraint(
                    $"OnCopyChat({correlationId}): failed to process mention {mention.Id} / {mention.MentionId}' "
                    + $"due to MentionId mismatch.");

            return authorSid;
        }
    }

    // Nested types

    private record MigratedRole(Role OldRole, Role NewRole);

    internal class MigratedAuthors
    {
        private readonly List<MigratedAuthor> _migratedAuthors = new ();

        public void RegisterMigrated(AuthorFull originalAuthor, AuthorId newAuthorId)
            => _migratedAuthors.Add(new MigratedAuthor(originalAuthor.Id, newAuthorId));

        public void RegisterRemoved(AuthorFull originalAuthor)
            => _migratedAuthors.Add(new MigratedAuthor(originalAuthor.Id, null));

        public AuthorId GetNewAuthorId(AuthorId authorId)
            => GetNewAuthorId(authorId.Value);

        public AuthorId GetNewAuthorId(string authorSid)
        {
            var migratedAuthor = RequireMigratedAuthor(authorSid);
            return migratedAuthor.NewAuthorId
                ?? throw StandardError.Constraint($"Copied author for Id {authorSid} is marked as removed");
        }

        public bool IsRemoved(string authorSid)
            => RequireMigratedAuthor(authorSid).NewAuthorId is not null;

        public MigratedAuthor RequireMigratedAuthor(string authorSid)
        {
            var migratedAuthor = GetMigratedAuthor(authorSid);
            if (migratedAuthor == null)
                throw StandardError.Constraint($"Copied author for Id {authorSid} is not found");

            return migratedAuthor;
        }

        public MigratedAuthor? GetMigratedAuthor(string authorSid)
            => _migratedAuthors.FirstOrDefault(c => c.OldAuthorId.Id.Value == authorSid);

        public sealed record MigratedAuthor(AuthorId OldAuthorId, AuthorId? NewAuthorId);
    }

    private sealed record CopyChatEntriesContext(
        ChatId ChatId,
        ChatId NewChatId,
        string CorrelationId,
        MigratedAuthors MigratedAuthors);

    public sealed record CopyChatEntriesResult(
        long ProcessedChatEntryCount,
        ChatEntryId? LastProcessedEntryId);
}
