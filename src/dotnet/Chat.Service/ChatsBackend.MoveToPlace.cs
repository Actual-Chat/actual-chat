using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    public virtual async Task<ChatBackend_MoveChatToPlaceResult> OnMoveToPlace(
        ChatBackend_MoveChatToPlace command,
        CancellationToken cancellationToken)
    {
        var (chatId, placeId) = command;
        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            _ = Get(chatId, default);
            _ = GetPublicChatIdsFor(placeId, default);
            var lastEntryIdInv = context.Operation().Items.GetOrDefault(ChatEntryId.None);
            if (!lastEntryIdInv.IsNone)
                InvalidateTiles(newChatId, ChatEntryKind.Text, lastEntryIdInv.LocalId, ChangeKind.Create);
            return default!;
        }

        Log.LogInformation("OnMoveToPlace: starting, moving chat '{ChatId}' to place '{PlaceId}'",
            chatId.Value,
            placeId);

        var migratedRoles = new List<MigratedRole>();
        var migratedAuthors = new MigratedAuthors();
        var didProgress = false;

        var textEntryRange = new Range<long>();
        {
            var dbContext = CreateDbContext(true);
            dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
            await using var __ = dbContext.ConfigureAwait(false);

            var chat = await Get(chatId, cancellationToken).Require().ConfigureAwait(false);
            var newChat = await CreateOrUpdateChat(dbContext, newChatId, chat, cancellationToken).ConfigureAwait(false);

            var lastTextEntry = await GetLastEntry(dbContext, chatId, ChatEntryKind.Text, cancellationToken)
                .ConfigureAwait(false);
            if (lastTextEntry != null) {
                var lastNewTextEntry = await GetLastEntry(dbContext, newChatId, ChatEntryKind.Text, cancellationToken)
                    .ConfigureAwait(false);
                var startEntryId = lastNewTextEntry != null ? lastNewTextEntry.LocalId + 1 : 1;
                var endEntryId = lastTextEntry.LocalId;
                if (endEntryId > startEntryId)
                    textEntryRange = new Range<long>(startEntryId, endEntryId + 1);
            }

            var chatSid = chatId.Value;

            var didProgress1 = await CreateOrUpdateRoles(dbContext,
                    chatSid,
                    newChat,
                    migratedRoles,
                    cancellationToken)
                .ConfigureAwait(false);

            var didProgress2 = await CreateOrGetAuthors(dbContext,
                    chatSid,
                    newChatId,
                    migratedRoles,
                    migratedAuthors,
                    cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            didProgress |= didProgress1 | didProgress2;
        }

        // if (textEntryRange.Start > 1) {
        //      TODO: update last N records from previous insert
        // }

        var proceed = true;
        var hasErrors = false;
        var lastEntryId = 0L;
        CopyChatEntriesResult? result = null;
        if (!textEntryRange.IsEmpty) {
            var startEntryId = textEntryRange.Start;
            var copyContext = new CopyChatEntriesContext(chatId, newChatId, migratedAuthors);
            const int batchLimit = 10;

            while (proceed) {
                var batchRange = new Range<long>(startEntryId, textEntryRange.End);
                try {
                    result = await CopyChatEntries(copyContext,
                            batchRange,
                            batchLimit,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (result.ProcessedChatEntriesCount == 0)
                        proceed = false;
                    else {
                        didProgress = true;
                        startEntryId = result.LastEntryId.LocalId + 1;
                        lastEntryId = result.LastEntryId.LocalId;
                    }
                }
                catch (Exception e) {
                    proceed = false;
                    hasErrors = true;
                    Log.LogInformation(e, "Failed to proceed chat entries insertion for range [{Start}, {End})", batchRange.Start, batchRange.End);
                }
            }
        }

        if (result != null && !result.LastEntryId.IsNone) {
            var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
            dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
            await using var __ = dbContext.ConfigureAwait(false);

            context.Operation().Items.Set(result.LastEntryId);
        }

        Log.LogInformation("OnMoveToPlace: completed");
        return new ChatBackend_MoveChatToPlaceResult(didProgress, hasErrors, lastEntryId);
    }

    private async Task<Chat> CreateOrUpdateChat(ChatDbContext dbContext, ChatId newChatId, Chat chat, CancellationToken cancellationToken)
    {
        var chatSid = newChatId.Value;
        var dbChat = await dbContext.Chats
            .Where(c => c.Id == chatSid)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        Chat newChat;
        if (dbChat == null) {
            newChat = chat with { Id = newChatId };
            dbContext.Chats.Add(new DbChat(newChat));
        }
        else {
            dbChat.Title = chat.Title;
            dbChat.IsPublic = chat.IsPublic;
            dbChat.MediaId = chat.MediaId;
            dbChat.Version = chat.Version;
            newChat = dbChat.ToModel();
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Updated chat record");
        return newChat;
    }

    private async Task<bool> CreateOrUpdateRoles(
        ChatDbContext dbContext,
        string chatSid,
        Chat newChat,
        ICollection<MigratedRole> migratedRoles,
        CancellationToken cancellationToken)
    {
        var didProgress = false;
        var dbRoles = await dbContext.Roles
            .Where(c => c.ChatId == chatSid)
            .OrderBy(c => c.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbRole in dbRoles) {
            var originalRole = dbRole.ToModel();
            var newRoleId = new RoleId(RoleId.Format(newChat.Id, dbRole.LocalId));
            var newRoleSid = newRoleId.Value;
            var existentNewRole = await dbContext.Roles
                .Where(c => c.Id == newRoleSid)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var newRole = originalRole with { Id = newRoleId };
            if (existentNewRole == null) {
                dbContext.Roles.Add(new DbRole(newRole));
                didProgress = true;
            } else {
                if (existentNewRole.SystemRole != originalRole.SystemRole)
                    throw StandardError.Constraint("Can't proceed migration. Role 'SystemRole' property has changed. "
                        + $"Expected value: '{originalRole.SystemRole}', but actual value: '{existentNewRole.SystemRole}'.");
                if (!OrdinalEquals(existentNewRole.Name, originalRole.Name))
                    throw StandardError.Constraint("Can't proceed migration. Role 'Name' property has changed. "
                        + $"Expected value: '{originalRole.Name}', but actual value: '{existentNewRole.Name}'.");
                existentNewRole.UpdateFrom(newRole);
            }

            migratedRoles.Add(new MigratedRole(originalRole, newRole));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Updated {Count} role records", dbRoles.Count);
        return didProgress;
    }

    private async Task<bool> CreateOrGetAuthors(
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        IReadOnlyCollection<MigratedRole> migratedRoles,
        MigratedAuthors migratedAuthors,
        CancellationToken cancellationToken)
    {
        var placeRootChatId = newChatId.PlaceId.ToRootChatId();
        var createdAuthors = 0;
        var didProgress = false;

        // Create place members and update chat authors.
        var dbAuthors = await dbContext.Authors
            .Include(a => a.Roles)
            .Where(c => c.ChatId == chatSid)
            .OrderBy(c => c.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbAuthor in dbAuthors) {
            var originalAuthor = dbAuthor.ToModel();
            var userId = originalAuthor.UserId;
            if (userId.IsNone)
                throw StandardError.Internal(
                    $"Can't proceed with the migration: found an author with no associated user. AuthorId is '{dbAuthor.Id}'.");

            if (originalAuthor.IsAnonymous) {
                var authorSid = originalAuthor.Id.Value;
                var hasChatEntries = await dbContext.ChatEntries
                    .Where(c => c.ChatId == chatSid && c.AuthorId == authorSid)
                    .AnyAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!hasChatEntries) {
                    migratedAuthors.RegisterRemoved(originalAuthor);
                    continue; // Skip anonymous author if there were no messages from them.
                }

                // TODO(DF): To think what we can do with anonymous authors migration.
                // Places are not supposed to have anonymous authors at the moment.
                throw StandardError.Internal(
                    $"Can't proceed with the migration: anonymous author migration isn't supported yet. AuthorId is '{dbAuthor.Id}'.");
            }

            // Ensure there is matching place member
            // TODO(DF): Can we use AuthorsBackend_GetAuthorOption.Full? In fact, they are equivalents in this case.
            var placeMember = await AuthorsBackend.GetByUserId(placeRootChatId, userId, AuthorsBackend_GetAuthorOption.Raw, cancellationToken)
                .ConfigureAwait(false);
            if (placeMember == null) {
                var authorDiff = new AuthorDiff { AvatarId = dbAuthor.AvatarId };
                var upsertPlaceMemberCmd = new AuthorsBackend_Upsert(placeRootChatId,
                    default,
                    userId,
                    null,
                    authorDiff);
                placeMember = await Commander.Call(upsertPlaceMemberCmd, cancellationToken)
                    .ConfigureAwait(false);
                didProgress = true;
            }
            {
                var newLocalId = placeMember.LocalId;
                var newAuthorId = new AuthorId(newChatId, newLocalId, AssumeValid.Option);
                var existentAuthor = await AuthorsBackend.Get(newAuthorId.ChatId, newAuthorId, AuthorsBackend_GetAuthorOption.Raw, cancellationToken)
                    .ConfigureAwait(false);
                if (existentAuthor != null) {
                    migratedAuthors.RegisterMigrated(originalAuthor, placeMember, existentAuthor);
                    continue;
                }

                var newAuthor = originalAuthor with {
                    Id = newAuthorId,
                    RoleIds = new ApiArray<Symbol>()
                };
                if (newAuthor.Version <= 0) {
                    Log.LogInformation("Invalid version on DbAuthor with Id={AuthorId}", newAuthor.Id);
                    newAuthor = newAuthor with { Version = VersionGenerator.NextVersion() };
                }
                dbContext.Authors.Add(new DbAuthor(newAuthor));
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                migratedAuthors.RegisterMigrated(originalAuthor, placeMember, newAuthor);

                var roleIds = dbAuthor.Roles.Select(c => c.DbRoleId).ToList();
                foreach (var roleId in roleIds) {
                    var migratedRole = migratedRoles.First(c => OrdinalEquals(roleId, c.OriginalRole.Id.Value));
                    dbContext.AuthorRoles.Add(new DbAuthorRole {
                        DbAuthorId = newAuthorId,
                        DbRoleId = migratedRole.NewId
                    });
                }

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                createdAuthors++;
                didProgress = true;
            }
        }

        Log.LogInformation("Created {Count} authors", createdAuthors);
        return didProgress;
    }

    private async Task<CopyChatEntriesResult> CopyChatEntries(
        CopyChatEntriesContext context,
        Range<long> entryIdRange,
        int batchLimit,
        CancellationToken cancellationToken)
    {
        if (entryIdRange.IsEmpty)
            return new CopyChatEntriesResult(0, ChatEntryId.None, new Range<long>());

        var dbContext = CreateDbContext(true);
        dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await using var __ = dbContext.ConfigureAwait(false);

        var textEntriesResult = await CopyChatEntries(dbContext,
                context,
                ChatEntryKind.Text,
                entryIdRange,
                batchLimit,
                cancellationToken)
            .ConfigureAwait(false);

        if (!textEntriesResult.AudioEntryId.IsEmpty)
            await CopyChatEntries(dbContext,
                    context,
                    ChatEntryKind.Audio,
                    textEntriesResult.AudioEntryId,
                    batchLimit,
                    cancellationToken)
                .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return textEntriesResult;
    }

    private async Task<CopyChatEntriesResult> CopyChatEntries(
        ChatDbContext dbContext,
        CopyChatEntriesContext context,
        ChatEntryKind entryKind,
        Range<long> entryIdRange,
        int batchLimit,
        CancellationToken cancellationToken)
    {
        var chatSid = context.ChatId.Value;
        var newChatId = context.NewChatId;
        var migratedAuthors = context.MigratedAuthors;
        var mentionExtractor = new MentionExtractor();

        var mentionUpdatesInsideContent = 0;
        var mentionUpdatesInSystemEntries = 0;
        var minRelatedAudioEntryId = long.MaxValue;
        var maxRelatedAudioEntryId = 0L;

        var minLocalId = entryIdRange.Start;
        var maxLocalId = entryIdRange.End - 1;
        var attachmentIds = new List<long>();
        var reactionIds = new List<long>();

        var entries = await dbContext.ChatEntries
            .Where(c => c.ChatId == chatSid && c.Kind == entryKind)
            .Where(c => c.LocalId >= minLocalId && c.LocalId <= maxLocalId)
            .OrderBy(c => c.LocalId)
            .Take(batchLimit)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
            return new CopyChatEntriesResult(0, ChatEntryId.None, new Range<long>());

        DbChatEntry? lastEntry = entries[^1];

        List<long> chatEntryWithMentionIds = new List<long>();
        if (entryKind == ChatEntryKind.Text)
            await InsertMentions(dbContext,
                    chatSid,
                    newChatId,
                    new Range<long>(entryIdRange.Start, lastEntry.LocalId + 1),
                    migratedAuthors,
                    chatEntryWithMentionIds,
                    cancellationToken)
                .ConfigureAwait(false);
        else
            chatEntryWithMentionIds = new List<long>();

        lastEntry = null;

        foreach (var dbChatEntry in entries) {
            var skip = false;
            var newEntryId = new ChatEntryId(newChatId, entryKind, dbChatEntry.LocalId, AssumeValid.Option);
            dbChatEntry.Id = newEntryId;
            dbChatEntry.ChatId = newChatId;

            var authorSid = dbChatEntry.AuthorId;
            var authorId = new AuthorId(authorSid);
            AuthorId newAuthorId;
            if (authorId.LocalId > 0)
                newAuthorId = migratedAuthors.GetNewAuthorId(authorSid);
            else if (authorId.LocalId == Constants.User.Walle.AuthorLocalId)
                newAuthorId = new AuthorId(newChatId, authorId.LocalId, AssumeValid.Option);
            else
                throw StandardError.Internal($"Unexpected author local id. Local id is '{authorId.LocalId}'.");

            dbChatEntry.AuthorId = newAuthorId;

            if (dbChatEntry.Kind == ChatEntryKind.Text
                && chatEntryWithMentionIds.Contains(dbChatEntry.LocalId)) {
                var content = UpdateMentionsInContent(dbChatEntry.Content, migratedAuthors, mentionExtractor);
                if (!OrdinalEquals(content, dbChatEntry.Content)) {
                    dbChatEntry.Content = content;
                    mentionUpdatesInsideContent++;
                }
            }

            if (dbChatEntry.IsSystemEntry) {
                var chatEntry = dbChatEntry.ToModel();
                var membersChangedOption = chatEntry.SystemEntry?.MembersChanged;
                if (membersChangedOption != null) {
                    var changeAuthorId = membersChangedOption.AuthorId;
                    if (!string.IsNullOrEmpty(changeAuthorId)) {
                        var isRemoved = migratedAuthors.IsRemoved(changeAuthorId);
                        if (isRemoved) {
                            // It's a system chat entry for removed author. So let's remove entry as well.
                            skip = true;
                        }
                        else {
                            var changeNewAuthorId = migratedAuthors.GetNewAuthorId(changeAuthorId);
                            var newMembersChangedOption = new MembersChangedOption(changeNewAuthorId,
                                membersChangedOption.AuthorName,
                                membersChangedOption.HasLeft);
                            chatEntry = chatEntry with { SystemEntry = newMembersChangedOption };
                            dbChatEntry.UpdateFrom(chatEntry);
                            mentionUpdatesInSystemEntries++;
                        }
                    }
                }
            }

            if (entryKind == ChatEntryKind.Text && dbChatEntry.AudioEntryId.HasValue) {
                var audioEntryId = dbChatEntry.AudioEntryId.Value;
                if (audioEntryId < minRelatedAudioEntryId)
                    minRelatedAudioEntryId = audioEntryId;
                if (audioEntryId > maxRelatedAudioEntryId)
                    maxRelatedAudioEntryId = audioEntryId;
            }

            if (!skip) {
                dbContext.ChatEntries.Add(dbChatEntry);
                lastEntry = dbChatEntry;

                if (dbChatEntry.HasAttachments)
                    attachmentIds.Add(dbChatEntry.LocalId);
                if (dbChatEntry.HasReactions)
                    reactionIds.Add(dbChatEntry.LocalId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (attachmentIds.Count > 0)
            await InsertTextEntryAttachments(dbContext, chatSid, newChatId, attachmentIds, cancellationToken).ConfigureAwait(false);

        if (reactionIds.Count > 0)
            await InsertReactions(dbContext, chatSid, newChatId, reactionIds, migratedAuthors, cancellationToken).ConfigureAwait(false);

        Log.LogInformation(
            "Inserted {Count} {Kind} chat entry records with local ids [{From},{To}]",
            entries.Count,
            entryKind,
            minLocalId,
            maxLocalId);

        if (entryKind == ChatEntryKind.Text)
            Log.LogInformation("Updated author mentions inside DbChatEntry.Content. {Count} records are affected",
                mentionUpdatesInsideContent);

        Log.LogInformation("Updated MembersChangedOption inside system chat entries. {Count} records are affected",
            mentionUpdatesInSystemEntries);

        var lastEntryId = lastEntry != null ? ChatEntryId.Parse(lastEntry.Id) : ChatEntryId.None;
        var audioRange = minRelatedAudioEntryId <= maxRelatedAudioEntryId
            ? new Range<long>(minRelatedAudioEntryId, maxRelatedAudioEntryId + 1)
            : new Range<long>();
        return new CopyChatEntriesResult(entries.Count, lastEntryId, audioRange);
    }


        // Что делать с форвардами, когда мы копируем записи?

        // // NOTE: I expect that we don't have many entries with forward fields filled in so far
        // // hence it's ok to fetch them all at once.
        // var chatEntriesToUpdateForwardFields = await dbContext.ChatEntries
        //     .Where(c => c.ForwardedAuthorId != null && c.ForwardedAuthorId.StartsWith(chatSid))
        //     .ToListAsync(cancellationToken)
        //     .ConfigureAwait(false);
        //
        // updateCount = 0;
        // foreach (var dbChatEntry in chatEntriesToUpdateForwardFields) {
        //     var forwardedAuthorId =
        //         dbChatEntry.ForwardedAuthorId.RequireNonEmpty(nameof(DbChatEntry.ForwardedAuthorId));
        //     var newAuthorId = migratedAuthors.GetNewAuthorId(forwardedAuthorId);
        //     dbChatEntry.ForwardedAuthorId = newAuthorId.Value;
        //     var forwardedChatEntryId =
        //         dbChatEntry.ForwardedChatEntryId.RequireNonEmpty(nameof(DbChatEntry.ForwardedChatEntryId));
        //     var chatEntryId = new ChatEntryId(forwardedChatEntryId);
        //     var newChatEntrySid = ChatEntryId.Format(newChatId, chatEntryId.Kind, chatEntryId.LocalId);
        //     dbChatEntry.ForwardedChatEntryId = newChatEntrySid;
        //     updateCount++;
        // }
        //
        // await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Log.LogInformation("Updated ForwardedAuthorId and ForwardedChatEntryId for {Count} chat entry records",
        //     updateCount);

    private async Task<DbChatEntry?> GetLastEntry(
        ChatDbContext dbContext,
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.ChatEntries
            .Where(c => c.ChatId == chatId.Value && c.Kind == entryKind)
            .OrderByDescending(c => c.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return entry;
    }

    private string UpdateMentionsInContent(string content, MigratedAuthors migratedAuthors,
        MentionExtractor mentionExtractor)
    {
        var markup = MarkupParser.Parse(content);
        var mentionIds = mentionExtractor.GetMentionIds(markup);
        foreach (var mentionId in mentionIds) {
            if (!mentionId.IsAuthor(out var authorId))
                continue;

            var newAuthorId = migratedAuthors.GetNewAuthorId(authorId);
            var newMentionId = new MentionId(newAuthorId, AssumeValid.Option);
            content = content.Replace(mentionId.Id.Value, newMentionId.Id.Value, StringComparison.Ordinal);
        }
        return content;
    }

    private async Task InsertTextEntryAttachments(ChatDbContext dbContext, string chatSid, ChatId newChatId, List<long> attachmentsIds, CancellationToken cancellationToken)
    {
        var attachmentIdPrefix = chatSid + ":0:";
        List<string> ids = attachmentsIds.Select(c => attachmentIdPrefix + c).ToList();
        var attachments = await dbContext.TextEntryAttachments
            .Where(c => c.Id.StartsWith(attachmentIdPrefix))
 #pragma warning disable CA1310
            .Where(c => ids.Any(x => c.Id.StartsWith(x)))
 #pragma warning restore CA1310
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbAttachment in attachments) {
            var entryId = new TextEntryId(dbAttachment.EntryId);
            var newEntryId = new TextEntryId(newChatId, entryId.LocalId, AssumeValid.Option);
            dbAttachment.Id = DbTextEntryAttachment.ComposeId(newEntryId, dbAttachment.Index);
            dbAttachment.EntryId = newEntryId;
            dbContext.TextEntryAttachments.Add(dbAttachment);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Log.LogInformation("Inserted {Count} text entry attachment records", attachments.Count);
    }

    private async Task InsertReactions(
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        List<long> reactionIds,
        MigratedAuthors migratedAuthors,
        CancellationToken cancellationToken)
    {
        var reactionIdPrefix = chatSid + ":0:";
        List<string> ids = reactionIds.Select(c => reactionIdPrefix + c).ToList();

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
            var newAuthorId = migratedAuthors.GetNewAuthorId(authorSid);
            dbReaction.AuthorId = newAuthorId.Value;

            var entryId = new TextEntryId(dbReaction.EntryId);
            var newEntryId = new TextEntryId(newChatId, entryId.LocalId, AssumeValid.Option);
            dbReaction.EntryId = newEntryId.Value;
            dbReaction.Id = DbReaction.ComposeId(newEntryId, newAuthorId);

            dbContext.Reactions.Add(dbReaction);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Inserted {Count} reaction records", reactions.Count);

        var reactionSummaries = await dbContext.ReactionSummaries
            .Where(c => c.Id.StartsWith(chatSid))
 #pragma warning disable CA1310
            .Where(c => ids.Any(x => c.Id.StartsWith(x)))
 #pragma warning restore CA1310
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbSummary in reactionSummaries) {
            var entryId = new TextEntryId(dbSummary.EntryId);
            var newEntryId = new TextEntryId(newChatId, entryId.LocalId, AssumeValid.Option);
            dbSummary.EntryId = newEntryId.Value;
            dbSummary.Id = DbReactionSummary.ComposeId(newEntryId, dbSummary.EmojiId);

            var summary = dbSummary.ToModel();
            var authorIds = summary.FirstAuthorIds;

            var newAuthorIds = ImmutableList<AuthorId>.Empty;
            foreach (var authorId in authorIds) {
                var newAuthorId = migratedAuthors.GetNewAuthorId(authorId);
                newAuthorIds = newAuthorIds.Add(newAuthorId);
            }
            summary = summary with { FirstAuthorIds = newAuthorIds };
            dbSummary.UpdateFrom(summary);

            dbContext.ReactionSummaries.Add(dbSummary);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Log.LogInformation("Updated AuthorIds for {Count} reaction summary records", reactionSummaries.Count);
    }

    private async Task InsertMentions(
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        Range<long> range,
        MigratedAuthors migratedAuthors,
        ICollection<long> entryIdsCollector,
        CancellationToken cancellationToken)
    {
        var maxLocalId = range.End - 1;
        var minLocalId = range.Start;
        var newChatSid = newChatId.Value;

        var mentions = await dbContext.Mentions
            .Where(c => c.ChatId == chatSid)
            .Where(c => c.EntryId >= minLocalId && c.EntryId <= maxLocalId)
            .OrderBy(c => c.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        const string mentionIdAuthorPrefix = "a:";
        foreach (var mention in mentions) {
            mention.ChatId = newChatSid;
            var mentionSid = mention.MentionId;
            MentionId mentionId;
            if (mentionSid.StartsWith(mentionIdAuthorPrefix, StringComparison.Ordinal)) {
                var authorSid = mentionSid.Substring(mentionIdAuthorPrefix.Length);
                var newAuthorId = migratedAuthors.GetNewAuthorId(authorSid);
                mentionId = new MentionId(newAuthorId, AssumeValid.Option);
                mention.MentionId = mentionId;
            }
            else {
                mentionId = new MentionId(mentionSid);
            }
            mention.Id = DbMention.ComposeId(new ChatEntryId(newChatId, ChatEntryKind.Text, mention.EntryId, AssumeValid.Option), mentionId);
            dbContext.Mentions.Add(mention);
            entryIdsCollector.Add(mention.EntryId);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Updated ChatId and Id for {Count} mention records", mentions.Count);
    }

    private record MigratedRole(Role OriginalRole, Role NewRole)
    {
        public RoleId NewId => NewRole.Id;
    }

    public class MigratedAuthors
    {
        private readonly List<MigratedAuthor> _migratedAuthors = new ();

        public void RegisterMigrated(AuthorFull originalAuthor, AuthorFull placeMember, AuthorFull newAuthor)
            => _migratedAuthors.Add(new MigratedAuthor(originalAuthor, placeMember, newAuthor));

        public void RegisterRemoved(AuthorFull originalAuthor)
            => _migratedAuthors.Add(new MigratedAuthor(originalAuthor, null, null));

        public AuthorId GetNewAuthorId(AuthorId authorId)
            => GetNewAuthorId(authorId.Value);

        public AuthorId GetNewAuthorId(string authorSid)
        {
            var migratedAuthor = DemandMigratedAuthor(authorSid);
            if (migratedAuthor.IsRemoved)
                throw StandardError.Constraint($"Migrated author for id '{authorSid}' is registered as removed");
            return migratedAuthor.NewId;
        }

        public bool IsRemoved(string authorSid)
            => DemandMigratedAuthor(authorSid).IsRemoved;

        private MigratedAuthor DemandMigratedAuthor(string authorSid)
        {
            var migratedAuthor = FindMigratedAuthor(authorSid);
            if (migratedAuthor == null)
                throw StandardError.Constraint($"Migrated author for id '{authorSid}' is not registered");
            return migratedAuthor;
        }

        private MigratedAuthor? FindMigratedAuthor(string authorSid)
            => _migratedAuthors.FirstOrDefault(c => OrdinalEquals(c.OriginalAuthor.Id.Value, authorSid));

        private record MigratedAuthor(AuthorFull OriginalAuthor, AuthorFull? PlaceMember, AuthorFull? NewAuthor)
        {
            public AuthorId NewId => NewAuthor?.Id ?? AuthorId.None;
            public bool IsRemoved => NewAuthor == null;
        }
    }

    private sealed record CopyChatEntriesContext(
        ChatId ChatId,
        ChatId NewChatId,
        MigratedAuthors MigratedAuthors);

    public sealed record CopyChatEntriesResult(long ProcessedChatEntriesCount, ChatEntryId LastEntryId, Range<long> AudioEntryId);
}




