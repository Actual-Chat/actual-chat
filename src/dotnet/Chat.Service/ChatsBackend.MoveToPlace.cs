using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    public virtual async Task OnMoveToPlace(
        ChatBackend_MoveChatToPlace command,
        CancellationToken cancellationToken)
    {
        var (chatId, placeId) = command;

        if (Computed.IsInvalidating()) {
            _ = Get(chatId, default);
            _ = GetPublicChatIdsFor(placeId, default);
            return;
        }

        Log.LogInformation("OnMoveToPlace: starting, moving chat '{ChatId}' to place '{PlaceId}'",
            chatId.Value,
            placeId);

        var chatSid = chatId.Value;
        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;
        var newChatSid = newChatId.Value;

        var migratedRoles = new List<MigratedRole>();
        var migratedAuthors = new MigratedAuthors();

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await using var __ = dbContext.ConfigureAwait(false);

        await UpdateChat(dbContext, chatSid, newChatSid, cancellationToken).ConfigureAwait(false);

        await UpdateRoles(dbContext,
                chatSid,
                newChatId,
                migratedRoles,
                cancellationToken)
            .ConfigureAwait(false);

        await UpdateAuthors(dbContext,
                chatSid,
                newChatId,
                migratedRoles,
                migratedAuthors,
                cancellationToken)
            .ConfigureAwait(false);

        await UpdateChatEntries(dbContext,
                chatSid,
                newChatId,
                migratedAuthors,
                cancellationToken)
            .ConfigureAwait(false);

        await UpdateTextEntryAttachments(dbContext, chatSid, newChatSid, cancellationToken).ConfigureAwait(false);

        await UpdateMentions(dbContext,
                chatSid,
                newChatSid,
                migratedAuthors,
                cancellationToken)
            .ConfigureAwait(false);

        await UpdateReactions(dbContext,
                chatSid,
                newChatSid,
                migratedAuthors,
                cancellationToken)
            .ConfigureAwait(false);

        await UpdateReactionSummaries(dbContext,
                chatSid,
                newChatSid,
                migratedAuthors,
                cancellationToken)
            .ConfigureAwait(false);

        Log.LogInformation("OnMoveToPlace: completed");
    }

    private async Task UpdateChat(
        ChatDbContext dbContext,
        string chatSid,
        string newChatSid,
        CancellationToken cancellationToken)
    {
        _ = await dbContext.Chats
            .Where(c => c.Id == chatSid)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Id, c => newChatSid)
                    .SetProperty(c => c.Kind, c => ChatKind.Place),
                cancellationToken)
            .RequireOneUpdated()
            .ConfigureAwait(false);

        Log.LogInformation("Updated chat record");
    }

    private async Task UpdateRoles(
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        ICollection<MigratedRole> migratedRoles,
        CancellationToken cancellationToken)
    {
        var dbRoles = await dbContext.Roles
            .Where(c => c.ChatId == chatSid)
            .OrderBy(c => c.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbRole in dbRoles) {
            var originalRole = dbRole.ToModel();
            var newRoleId = new RoleId(RoleId.Format(newChatId, dbRole.LocalId));

            _ = await dbContext.Roles
                .Where(c => c.Id == dbRole.Id)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Id, c => newRoleId)
                        .SetProperty(c => c.ChatId, c => newChatId),
                    cancellationToken)
                .RequireOneUpdated()
                .ConfigureAwait(false);

            var newRole = originalRole with { Id = newRoleId };
            migratedRoles.Add(new MigratedRole(originalRole, newRole));
        }

        Log.LogInformation("Updated {Count} role records", dbRoles.Count);
    }

    private async Task UpdateAuthors(
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        IReadOnlyCollection<MigratedRole> migratedRoles,
        MigratedAuthors migratedAuthors,
        CancellationToken cancellationToken)
    {
        var placeRootChatId = newChatId.PlaceChatId.PlaceId.ToRootChatId();

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
                    dbContext.Authors.Remove(dbAuthor);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
            }
            {
                var newLocalId = placeMember.LocalId;
                var newAuthor = originalAuthor with {
                    Id = new AuthorId(newChatId, newLocalId, AssumeValid.Option),
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

                    _ = await dbContext.AuthorRoles
                        .Where(c => c.DbRoleId == roleId && c.DbAuthorId == originalAuthor.Id)
                        .ExecuteUpdateAsync(setters => setters
                                .SetProperty(c => c.DbRoleId, c => migratedRole.NewId.Value)
                                .SetProperty(c => c.DbAuthorId, c => newAuthor.Id.Value),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (dbAuthors.Count > 0) {
            var updateCount = await dbContext.Authors
                .Where(c => c.ChatId == chatSid)
                .ExecuteDeleteAsync(cancellationToken)
                .RequireUpdated(dbAuthors.Count)
                .ConfigureAwait(false);

            Log.LogInformation("Updated {Count} author records", updateCount);
        }
    }

    private async Task UpdateChatEntries(
        ChatDbContext dbContext,
        string chatSid,
        ChatId newChatId,
        MigratedAuthors migratedAuthors,
        CancellationToken cancellationToken)
    {
        int updateCount;
        var chatEntryAuthorSids = await dbContext.ChatEntries
            .Where(c => c.ChatId == chatSid)
            .Select(c => c.AuthorId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var authorSid in chatEntryAuthorSids) {
            var authorId = new AuthorId(authorSid);
            AuthorId newAuthorId;
            if (authorId.LocalId > 0)
                newAuthorId = migratedAuthors.GetNewAuthorId(authorSid);
            else if (
                authorId.LocalId == Constants.User.Walle.AuthorLocalId
                || authorId.LocalId == Constants.User.MLSearchBot.AuthorLocalId
            )
                newAuthorId = new AuthorId(newChatId, authorId.LocalId, AssumeValid.Option);
            else
                throw StandardError.Internal($"Unexpected author local id. Local id is '{authorId.LocalId}'.");

            var newAuthorSid = newAuthorId.Value;
            updateCount = await dbContext.ChatEntries
                .Where(c => c.ChatId == chatSid && c.AuthorId == authorId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.AuthorId, newAuthorSid), cancellationToken)
                .RequireAtLeastOneUpdated()
                .ConfigureAwait(false);

            Log.LogInformation(
                "Updated AuthorId for {Count} chat entry records with author id '{AuthorId}'."
                + " New author id is '{NewAuthorId}'",
                updateCount,
                authorSid,
                newAuthorSid);
        }

        for (int i = 0; i < 2; i++) {
            var entryKind = (ChatEntryKind)i;
            var prefix = newChatId + ":" + i + ":";
            updateCount = await dbContext.ChatEntries
                .Where(c => c.ChatId == chatSid && c.Kind == entryKind)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Id, c => prefix + c.LocalId)
                        .SetProperty(c => c.ChatId, newChatId),
                    cancellationToken)
                .ConfigureAwait(false);

            Log.LogInformation("Updated Id and ChatId for {Count} '{Kind}' chat entry records",
                updateCount,
                entryKind.ToString());
        }

        // NOTE: I expect that we don't have many entries with forward fields filled in so far
        // hence it's ok to fetch them all at once.
        var chatEntriesToUpdateForwardFields = await dbContext.ChatEntries
            .Where(c => c.ForwardedAuthorId != null && c.ForwardedAuthorId.StartsWith(chatSid))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        updateCount = 0;
        foreach (var dbChatEntry in chatEntriesToUpdateForwardFields) {
            var forwardedAuthorId =
                dbChatEntry.ForwardedAuthorId.RequireNonEmpty(nameof(DbChatEntry.ForwardedAuthorId));
            var newAuthorId = migratedAuthors.GetNewAuthorId(forwardedAuthorId);
            dbChatEntry.ForwardedAuthorId = newAuthorId.Value;
            var forwardedChatEntryId =
                dbChatEntry.ForwardedChatEntryId.RequireNonEmpty(nameof(DbChatEntry.ForwardedChatEntryId));
            var chatEntryId = new ChatEntryId(forwardedChatEntryId);
            var newChatEntrySid = ChatEntryId.Format(newChatId, chatEntryId.Kind, chatEntryId.LocalId);
            dbChatEntry.ForwardedChatEntryId = newChatEntrySid;
            updateCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Log.LogInformation("Updated ForwardedAuthorId and ForwardedChatEntryId for {Count} chat entry records",
            updateCount);
    }

    private async Task UpdateTextEntryAttachments(
        ChatDbContext dbContext,
        string chatSid,
        string newChatSid,
        CancellationToken cancellationToken)
    {
        // TODO(DF): should we update MediaId and ThumbnailMediaId somehow?
        var attachmentIdPrefix = chatSid + ":0:";
        var attachmentNewIdPrefix = newChatSid + ":0:";
        var attachmentNewIdPrefixLength = attachmentIdPrefix.Length;
        var updateCount = await dbContext.TextEntryAttachments
            .Where(c => c.Id.StartsWith(attachmentIdPrefix))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.EntryId,
                        c => string.Concat(attachmentNewIdPrefix,
                            c.EntryId.Substring(attachmentNewIdPrefixLength)))
                    .SetProperty(c => c.Id,
                        c => string.Concat(attachmentNewIdPrefix,
                            c.Id.Substring(attachmentNewIdPrefixLength))),
                cancellationToken)
            .ConfigureAwait(false);

        Log.LogInformation("Updated {Count} text entry attachment records", updateCount);
    }

    private async Task UpdateMentions(
        ChatDbContext dbContext,
        string chatSid,
        string newChatSid,
        MigratedAuthors migratedAuthors,
        CancellationToken cancellationToken)
    {
        int updateCount = 0;

        // Update author mentions inside DbChatEntry.Content
        var chatEntryWithMentionIds = await dbContext.Mentions
            .Where(c => c.ChatId == chatSid)
            .Select(c => c.EntryId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mentionExtractor = new MentionExtractor();
        foreach (var entryLocalIdsChunk in chatEntryWithMentionIds.Chunk(20)) {
            var chatEntries = await dbContext.ChatEntries
                .Where(c => c.ChatId == newChatSid && c.Kind == ChatEntryKind.Text)
                .Where(c => entryLocalIdsChunk.Contains(c.LocalId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var dbChatEntry in chatEntries) {
                var content = dbChatEntry.Content;
                var markup = MarkupParser.Parse(content);
                var mentionIds = mentionExtractor.GetMentionIds(markup);
                foreach (var mentionId in mentionIds) {
                    if (!mentionId.IsAuthor(out var authorId))
                        continue;

                    var newAuthorId = migratedAuthors.GetNewAuthorId(authorId);
                    var newMentionId = new MentionId(newAuthorId, AssumeValid.Option);
                    content = content.Replace(mentionId.Id.Value, newMentionId.Id.Value, StringComparison.Ordinal);
                }
                if (OrdinalEquals(content, dbChatEntry.Content))
                    continue;

                dbChatEntry.Content = content;
                updateCount++;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        Log.LogInformation("Updated author mentions inside DbChatEntry.Content. {Count} records are affected",
            updateCount);

        // Update MembersChangedOption inside system chat entries
        updateCount = 0;
        var systemChatEntries = await dbContext.ChatEntries
            .Where(c => c.ChatId == newChatSid && c.Kind == ChatEntryKind.Text && c.IsSystemEntry)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var dbChatEntry in systemChatEntries) {
            var chatEntry = dbChatEntry.ToModel();
            var membersChangedOption = chatEntry.SystemEntry?.MembersChanged;
            if (membersChangedOption != null) {
                var authorId = membersChangedOption.AuthorId;
                if (string.IsNullOrEmpty(authorId))
                    continue; // Nothing to migrate.
                var isRemoved = migratedAuthors.IsRemoved(authorId);
                if (isRemoved) {
                    // It's a system chat entry for removed author. So let's remove entry as well.
                    dbContext.ChatEntries.Remove(dbChatEntry);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                var newAuthorId = migratedAuthors.GetNewAuthorId(authorId);
                var newMembersChangedOption = new MembersChangedOption(newAuthorId,
                    membersChangedOption.AuthorName,
                    membersChangedOption.HasLeft);
                chatEntry = chatEntry with { SystemEntry = newMembersChangedOption };
                dbChatEntry.UpdateFrom(chatEntry);
                updateCount++;
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Updated MembersChangedOption inside system chat entries. {Count} records are affected",
            updateCount);

        var mentionSids = await dbContext.Mentions
            .Where(c => c.ChatId == chatSid)
            .Select(c => c.MentionId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        const string mentionIdAuthorPrefix = "a:";
        foreach (var mentionSid in mentionSids) {
            if (!mentionSid.StartsWith(mentionIdAuthorPrefix, StringComparison.Ordinal))
                continue;

            var authorSid = mentionSid.Substring(mentionIdAuthorPrefix.Length);
            var newAuthorId = migratedAuthors.GetNewAuthorId(authorSid);
            var newMentionId = new MentionId(newAuthorId, AssumeValid.Option);
            var newMentionSid = newMentionId.Value;
#pragma warning disable CA1307
            updateCount = await dbContext.Mentions
                .Where(c => c.ChatId == chatSid)
                .Where(c => c.MentionId == mentionSid)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Id,
                            c => c.Id.Replace(mentionSid, newMentionSid))
                        .SetProperty(c => c.MentionId,
                            newMentionSid),
                    cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore CA1307
            Log.LogInformation(
                "Updated MentionId for {Count} mention records with old mention id '{MentionId}'."
                + " New mention id is '{NewMentionId}'",
                updateCount,
                mentionSid,
                newMentionSid);
        }

#pragma warning disable CA1307
        updateCount = await dbContext.Mentions
            .Where(c => c.ChatId == chatSid)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.ChatId,
                        c => newChatSid)
                    .SetProperty(c => c.Id,
                        c => c.Id.Replace(chatSid, newChatSid)),
                cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA1307

        Log.LogInformation("Updated ChatId and Id for {Count} mention records", updateCount);
    }

    private async Task UpdateReactions(
        ChatDbContext dbContext,
        string chatSid,
        string newChatSid,
        MigratedAuthors migratedAuthors,
        CancellationToken cancellationToken)
    {
        int updateCount;

        var authorSids = await dbContext.Reactions
            .Where(c => c.Id.StartsWith(chatSid))
            .Select(c => c.AuthorId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var authorSid in authorSids) {
            var newAuthorId = migratedAuthors.GetNewAuthorId(authorSid);
            var newAuthorSid = newAuthorId.Value;
#pragma warning disable CA1307
            updateCount = await dbContext.Reactions
                .Where(c => c.Id.StartsWith(chatSid))
                .Where(c => c.AuthorId == authorSid)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Id,
                            c => c.Id.Replace(authorSid, newAuthorSid))
                        .SetProperty(c => c.AuthorId,
                            newAuthorSid),
                    cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore CA1307
            Log.LogInformation(
                "Updated AuthorId for {Count} reaction records with old author id '{AuthorId}'."
                + " New author id is '{NewAuthorId}'",
                updateCount,
                authorSid,
                newAuthorSid);
        }

#pragma warning disable CA1307
        updateCount = await dbContext.Reactions
            .Where(c => c.Id.StartsWith(chatSid))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.EntryId,
                        c => c.EntryId.Replace(chatSid, newChatSid))
                    .SetProperty(c => c.Id,
                        c => c.Id.Replace(chatSid, newChatSid)),
                cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA1307

        Log.LogInformation("Updated EntryId and Id for {Count} reaction records", updateCount);
    }

    private async Task UpdateReactionSummaries(
        ChatDbContext dbContext,
        string chatSid,
        string newChatSid,
        MigratedAuthors migratedAuthors,
        CancellationToken cancellationToken)
    {
        #pragma warning disable CA1307
        var updateCount = await dbContext.ReactionSummaries
            .Where(c => c.Id.StartsWith(chatSid))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.EntryId,
                        c => c.EntryId.Replace(chatSid, newChatSid))
                    .SetProperty(c => c.Id,
                        c => c.Id.Replace(chatSid, newChatSid)),
                cancellationToken)
            .ConfigureAwait(false);
        #pragma warning restore CA1307

        Log.LogInformation("Updated EntryId and Id for {Count} reaction summary records", updateCount);

        updateCount = 0;
        foreach (var dbSummary in dbContext.ReactionSummaries.Where(c => c.Id.StartsWith(chatSid))) {
            var summary = dbSummary.ToModel();
            var authorIds = summary.FirstAuthorIds;
            var newAuthorIds = ImmutableList<AuthorId>.Empty;
            foreach (var authorId in authorIds) {
                var newAuthorId = migratedAuthors.GetNewAuthorId(authorId);
                newAuthorIds = newAuthorIds.Add(newAuthorId);
            }
            summary = summary with { FirstAuthorIds = newAuthorIds };
            dbSummary.UpdateFrom(summary);
            updateCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Log.LogInformation("Updated AuthorIds for {Count} reaction summary records", updateCount);
    }

    private record MigratedRole(Role OriginalRole, Role NewRole)
    {
        public RoleId NewId => NewRole.Id;
    }

    private class MigratedAuthors
    {
        private List<MigratedAuthor> _migratedAuthors = new List<MigratedAuthor>();

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
}
