using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for managing chat authors (participants).
/// </summary>
public class AuthorsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IAuthorsBackend
{
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IAvatarsBackend AvatarsBackend { get; } = services.GetRequiredService<IAvatarsBackend>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IDbEntityResolver<string, DbAuthor> DbAuthorResolver { get; } = services.GetRequiredService<IDbEntityResolver<string, DbAuthor>>();
    private IDbShardLocalIdGenerator<DbAuthor, string> DbAuthorLocalIdGenerator { get; } = services.GetRequiredService<IDbShardLocalIdGenerator<DbAuthor, string>>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    private IRolesBackend RolesBackend { get; } = services.GetRequiredService<IRolesBackend>();

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> Get(
        ChatId chatId,
        AuthorId authorId,
        RequestedAuthorKind authorKind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatId);
        ArgumentNullException.ThrowIfNull(authorId);
        if (authorId.ChatId != chatId)
            return null;

        var author = await GetInternal(chatId, authorId, authorKind, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetByUserId(
        ChatId chatId, UserId userId,
        RequestedAuthorKind authorKind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatId);
        ArgumentNullException.ThrowIfNull(userId);

        var author = await GetInternal(chatId, userId, authorKind, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // [ComputeMethod]
    public virtual async Task<AuthorId[]> ListAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatId);

        if (chatId is PeerChatId peerChatId)
            return GetDefaultPeerChatAuthors(peerChatId).Select(a => a.Id).ToArray();

        var authorChatId = await GetAuthorChatId(chatId, cancellationToken).ConfigureAwait(false);
        var authorIds = await ListAuthorIdsInternal(authorChatId, cancellationToken).ConfigureAwait(false);

        if (authorChatId != chatId && authorIds.Length > 0)
            authorIds = RemapList(authorIds, chatId);
        return authorIds;

        static AuthorId[] RemapList(AuthorId[] authorIds, ChatId chatId)
            => authorIds.Select(c => Remap(c, chatId)).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<UserId[]> ListUserIds(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId is PeerChatId peerChatId)
            return GetDefaultPeerChatAuthors(peerChatId).Select(a => a.UserId).ToArray();

        var authorChatId = await GetAuthorChatId(chatId, cancellationToken).ConfigureAwait(false);
        return await ListUserIdsInternal(authorChatId, cancellationToken).ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public async Task<AuthorFull[]> ListChanged(
        ChangedAuthorsQuery query,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var authorsQuery = query.LastId is null
            ? dbContext.Authors.Where(x => x.Version >= query.MinVersion && x.Version <= query.MaxVersion)
            : dbContext.Authors.Where(x => (x.Version > query.MinVersion && x.Version <= query.MaxVersion)
                || (x.Version==query.MinVersion && string.Compare(x.Id, query.LastId.Value) > 0));

        var dbAuthors = await authorsQuery
            .WhereIf(x => x.IsPlaceAuthor == query.IsPlaceAuthor, query.IsPlaceAuthor != null)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbAuthors.Select(x => x.ToModel()).ToArray();
    }

    // [CommandHandler]
    public virtual async Task<AuthorFull> OnUpsert(AuthorsBackend_Upsert command, CancellationToken cancellationToken)
    {
        var (chatId, authorId, userId, expectedVersion, diff, doNotNotify) = command;
        ArgumentNullException.ThrowIfNull(chatId);
        chatId.EnsureNonThread();
        if (authorId is null) {
            if (userId is null)
                throw new ArgumentOutOfRangeException(nameof(command), "Either AuthorId or UserId must be provided.");
        }
        else {
            if (authorId.ChatId != chatId)
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid AuthorId.");
            if (Bots.IsBot(authorId))
                throw new ArgumentOutOfRangeException(nameof(command), "System authors cannot be modified.");
        }

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var (invAuthor, invOldAuthor) = context.Operation.Items.KeylessGet<(AuthorFull?, AuthorFull?)>();
            if (invAuthor is not null) {
                _ = GetInternal(chatId, invAuthor.Id, default);
                _ = GetInternal(chatId, invAuthor.UserId, default);
                var invOldHadLeft = invOldAuthor?.HasLeft ?? true;
                if (invAuthor.HasLeft != invOldHadLeft) {
                    _ = ListAuthorIdsInternal(chatId, default);
                    _ = ListUserIdsInternal(chatId, default);
                }
            }
            return default!;
        }

        var defaultAuthor = chatId is PeerChatId peerChatId
            ? GetDefaultPeerChatAuthor(peerChatId, authorId, userId!).RequireValid(userId!)
            : null;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Can't use .ForUpdate() here due to join
        await dbContext.Authors.LockShared(chatId, userId, cancellationToken).ConfigureAwait(false);
        var dbAuthors = dbContext.Authors.Include(a => a.Roles);
        var dbAuthor = await (authorId is null
            ? dbAuthors.FirstOrDefaultAsync(a => a.ChatId == chatId.Value && a.UserId == userId!.Value, cancellationToken)
            : dbAuthors.FirstOrDefaultAsync(a => a.ChatId == chatId.Value && a.Id == authorId.Value, cancellationToken)
            ).ConfigureAwait(false);
        var existingAuthor = dbAuthor?.ToModel() ?? defaultAuthor;
        var authorHasLeft = false;

        if (existingAuthor != null) {
            // Update existing author, incl. one of the default ones in a peer chat
            existingAuthor.RequireVersion(expectedVersion).RequireValid(userId!);
            var account = await AccountsBackend.Get(existingAuthor.UserId, cancellationToken).Require().ConfigureAwait(false);

            var author = DiffEngine.Patch(existingAuthor, diff) with {
                Version = VersionGenerator.NextVersion(existingAuthor.Version),
            };

            // Check constraints
            if (author.IsAnonymous) {
                if (!existingAuthor.IsAnonymous)
                    throw StandardError.Constraint("IsAnonymous can be changed only to false.");
            }
            else if (account.IsGuestOrNull())
                throw StandardError.Constraint("Unauthenticated authors must be anonymous.");
            if (author.HasLeft && chatId.Kind == ChatKind.Peer)
                throw StandardError.Constraint("Peer chat authors can't leave.");

            authorHasLeft = dbAuthor is { HasLeft: false } && author.HasLeft;

            if (dbAuthor == null) {
                // First author update in a peer chat = create it
                dbAuthor = new DbAuthor(author);
                dbContext.Add(dbAuthor);
            }
            else
                dbAuthor.UpdateFrom(author);
        }
        else {
            // Create author, + we know here it's not a peer chat
            if (userId is null)
                throw new ArgumentOutOfRangeException(nameof(command), "UserId is required to create a new author.");

            await dbContext.Authors.Lock(chatId, userId, cancellationToken).ConfigureAwait(false);
            var skipSingleAuthorCheck = userId == Constants.User.Sherlock.UserId;
            if (!skipSingleAuthorCheck) {
                // Get chat directly in transaction instead of calling Backend
                // var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
                var chat = await dbContext.Chats
                    .Where(c => c.Id == chatId.Value)
                    .Select(c => new { c.Id, c.SystemTag })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (chat == null || chat.SystemTag == Constants.Chat.System.Notes.Tag) {
                    var alreadyHasAuthor = await dbContext.Authors
                        .AnyAsync(a => a.ChatId == chatId.Value && a.UserId != userId.Value, cancellationToken)
                        .ConfigureAwait(false);
                    if (alreadyHasAuthor)
                        throw StandardError.Constraint($"There can be only one author in this chat '{chat?.Id}:{userId}'.");
                }
            }
            var account = await AccountsBackend.Get(userId, cancellationToken).Require().ConfigureAwait(false);

            long localId;
            if (userId == Constants.User.Sherlock.UserId)
                localId = Constants.User.Sherlock.AuthorLocalId;
            else if (chatId is PlaceChatId { IsRoot: false } placeChatId1) {
                var placeId = placeChatId1;
                var placeAuthor = await GetByUserId(placeId.RootChatId, userId, RequestedAuthorKind.Default, cancellationToken)
                    .ConfigureAwait(false);
                if (placeAuthor == null)
                    throw StandardError.Internal("Place root chat author must exist.");
                localId = placeAuthor.LocalId;
            }
            else
                localId = await DbAuthorLocalIdGenerator
                    .Next(dbContext, chatId.Value, cancellationToken)
                    .ConfigureAwait(false);
            var author = new AuthorFull(userId, AuthorId.New(chatId, localId), VersionGenerator.NextVersion()) {
                IsAnonymous = command.Diff.IsAnonymous ?? account.IsGuestOrNull(),
            };
            author = DiffEngine.Patch(author, diff);
            author = author with { CreatedAt = Clocks.SystemClock.Now };

            // Check constraints
            if (author.HasLeft)
                throw StandardError.Constraint("New authors can't instantly leave the chat.");
            if (author.IsAnonymous) {
                if (author.AvatarId.IsEmpty) {
                    // Creating a random avatar for anonymous authors w/o pre-selected avatar
                    var changeCommand = new AvatarsBackend_Change(Symbol.Empty, null,
                        Change.Create(new AvatarDiff {
                            Name = RandomNameGenerator.Default.Generate(),
                            Bio = "Someone anonymous",
                            UserId = userId,
                            IsAnonymous = true,
                        }));
                    var avatar = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
                    author = author with { AvatarId = avatar.Id };
                }
            }
            else if (account.IsGuestOrNull())
                throw StandardError.Constraint("Unauthenticated authors must be anonymous.");

            dbAuthor = new DbAuthor(author);
            dbContext.Add(dbAuthor);
        }

        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException e) when(e.Entries.All(en => en.State == EntityState.Added)) {
            Log.LogWarning(e, "Upserting author failed with DbUpdateConcurrencyException. Command: {Command}", command);
            // Author for the same ChatId / UserId has already been created, let's get it
            dbAuthor = await (authorId is null
                    ? dbAuthors.FirstOrDefaultAsync(a => a.ChatId == chatId.Value && a.UserId == userId!.Value, cancellationToken)
                    : dbAuthors.FirstOrDefaultAsync(a => a.ChatId == chatId.Value && a.Id == authorId.Value, cancellationToken)
                ).ConfigureAwait(false);
            existingAuthor = dbAuthor.Require().ToModel().RequireValid(userId!);
        }

        if (chatId is PlaceChatId { IsRoot: false } placeChatId) {
            // NOTE(DF): Place chat author local_id must match to the root place chat author local_id for the same user.
            var rootChatId = placeChatId.PlaceId.RootChatId;
            var rootAuthor = await GetByUserId(rootChatId, UserId.Parse(dbAuthor.UserId!), default, cancellationToken)
                .ConfigureAwait(false);
            if (rootAuthor is null || rootAuthor.LocalId != dbAuthor.LocalId)
                throw StandardError.Constraint(
                    $"Place chat author local_id constraint is violated for author with id '{dbAuthor.Id}'. " +
                    $"Root chat author local_id is '{(rootAuthor is null ? "?" : rootAuthor.LocalId)}'");
        }

        if (authorHasLeft)
            await RemovePrivilegedRoles(authorId!, cancellationToken).ConfigureAwait(false);

        { // Nested to get a new var scope
            var author = dbAuthor.ToModel();
            context.Operation.Items.KeylessSet((author, existingAuthor));

            if (existingAuthor == null) {
                // Set the read position to the very end
                var chatTextIdRange = await ChatsBackend
                    .GetLidRange(command.ChatId, false, cancellationToken)
                    .ConfigureAwait(false);
                var readPosition = new ChatPosition(chatTextIdRange.End - 1);
                context.Operation.AddEvent(
                    new ChatPositionsBackend_Set(author.UserId, command.ChatId, ChatPositionKind.Read, readPosition));
            }

            if (chatId.Kind == ChatKind.Peer)
                context.Operation.AddEvent(
                    new ChatPositionsBackend_Set(author.UserId, command.ChatId, ChatPositionKind.Read, new ChatPosition()));

            // Raise events
            if (!doNotNotify)
                context.Operation.AddEvent(new AuthorUpsertedEvent(author, existingAuthor));
            return author;
        }
    }

    // [CommandHandler]
    public virtual async Task OnRemove(AuthorsBackend_Remove command, CancellationToken cancellationToken)
    {
        var (chatId, authorId, userId) = command;
        chatId?.EnsureNonThread();
        var nonNullCount = (authorId is not null ? 1 : 0) + (chatId is not null ? 1 : 0) + (userId is not null ? 1 : 0);
        if (nonNullCount != 1)
            throw new ArgumentOutOfRangeException(nameof(command),
                "Only one of the following properties must be non-null: AuthorId, UserId, or ChatId.");

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invAuthors = context.Operation.Items.KeylessGet<AuthorFull[]>();
            var invChatIds = new HashSet<ChatId>();
            if (chatId is not null)
                invChatIds.Add(chatId);
            if (invAuthors is not null) {
                foreach (var invAuthor in invAuthors) {
                    var invChatId = invAuthor.ChatId;
                    invChatIds.Add(invChatId);
                    _ = GetInternal(invChatId, invAuthor.Id, default);
                    _ = GetInternal(invChatId, invAuthor.UserId, default);
                }
            }
            foreach (var invChatId in invChatIds) {
                _ = ListAuthorIdsInternal(invChatId, default);
                _ = ListUserIdsInternal(invChatId, default);
            }
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var authors = new List<AuthorFull>();
        if (authorId is not null) {
            var dbAuthor = await dbContext.Authors
                .FirstOrDefaultAsync(a => a.Id == authorId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbAuthor != null) {
                dbContext.Remove(dbAuthor);
                authors.Add(dbAuthor.ToModel());
            }
        }
        else if (chatId is not null) {
            var dbAuthors = await dbContext.Authors
                .Where(a => a.ChatId == chatId.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (dbAuthors.Count > 0) {
                await dbContext.Authors
                    .Where(a => a.ChatId == chatId.Value)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                authors.AddRange(dbAuthors.Select(a => a.ToModel()));
            }
        }
        else if (userId is not null) {
            var dbAuthors = await dbContext.Authors
                .Where(a => a.UserId == userId.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (dbAuthors.Count > 0) {
                await dbContext.Authors
                    .Where(a => a.UserId == userId.Value)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                authors.AddRange(dbAuthors.Select(a => a.ToModel()));
            }
        }
        else
            throw new ArgumentOutOfRangeException(nameof(command),
                "One of the following properties must be non-null: AuthorId, UserId, or ChatId.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(authors.ToArray());
        if (authors.Count > 0)
            context.Operation.AddEvent(new AuthorsRemovedEvent(authors.ToArray()));
    }

    // CommandHandler
    public virtual async Task<bool> OnCopyChat(AuthorsBackend_CopyChat command, CancellationToken cancellationToken)
    {
        var (chatId, newChatId, rolesMap, correlationId) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (context.Operation.Items.KeylessGet<List<UserId>>() is { } invAuthorUserIds)
                foreach (var invAuthorUserId in invAuthorUserIds)
                    _ = GetInternal(newChatId, invAuthorUserId, default);
            if (context.Operation.Items.KeylessGet<List<AuthorId>>() is { } invAuthorIds)
                foreach (var invAuthorId in invAuthorIds)
                    _ = GetInternal(newChatId, invAuthorId, default);
            return default;
        }

        var chatSid = chatId.Value;
        var placeRootChatId = ((PlaceChatId)newChatId).RootChatId;
        var createdAuthors = 0;
        var hasChanges = false;
        var newAuthorIds = new List<AuthorId>();
        var newAuthorUserIds = new List<UserId>();

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Create place members and update chat authors.
        var oldDbAuthors = await dbContext.Authors
            .Include(a => a.Roles)
            .Where(c => c.ChatId == chatSid)
            .OrderBy(c => c.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbAuthor in oldDbAuthors) {
            var originalAuthor = dbAuthor.ToModel();
            if (originalAuthor.UserId is not { } userId)
                throw StandardError.Internal(
                    $"Can't proceed with the migration: found an author with no associated user. AuthorId is '{dbAuthor.Id}'.");

            if (originalAuthor.IsAnonymous)
                continue;

            // Ensure there is matching place member
            // TODO(DF): Can we use AuthorsBackend_GetAuthorOption.Full? In fact, they are equivalents in this case.
            var placeMember = await GetByUserId(placeRootChatId, userId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (placeMember == null) {
                var authorDiff = new AuthorDiff { AvatarId = dbAuthor.AvatarId };
                var upsertPlaceMemberCmd = new AuthorsBackend_Upsert(placeRootChatId,
                    default,
                    userId,
                    null,
                    authorDiff) {
                    DoNotNotify = true
                };
                placeMember = await Commander.Call(upsertPlaceMemberCmd, cancellationToken)
                    .ConfigureAwait(false);
                hasChanges = true;
            }
            {
                var newLocalId = placeMember.LocalId;
                var newAuthorId = AuthorId.New(newChatId, newLocalId);
                var existentAuthor = await Get(newAuthorId.ChatId, newAuthorId, RequestedAuthorKind.Default, cancellationToken)
                    .ConfigureAwait(false);
                if (existentAuthor != null)
                    continue;

                var newAuthor = originalAuthor with {
                    Id = newAuthorId,
                    RoleIds = [],
                };
                if (newAuthor.Version <= 0) {
                    Log.LogInformation("OnCopyChat({CorrelationId}) Invalid version on DbAuthor with Id={AuthorId}",
                        correlationId, newAuthor.Id);
                    newAuthor = newAuthor with { Version = VersionGenerator.NextVersion() };
                }
                dbContext.Authors.Add(new DbAuthor(newAuthor));
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var roleIds = dbAuthor.Roles.Select(c => c.DbRoleId).ToList();
                foreach (var roleId in roleIds) {
                    var migratedRole = rolesMap.First(c => roleId == c.Item1.Value);
                    dbContext.AuthorRoles.Add(new DbAuthorRole {
                        DbAuthorId = newAuthorId.Value,
                        DbRoleId = migratedRole.Item2.Value,
                    });
                }

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                createdAuthors++;
                hasChanges = true;

                newAuthorIds.Add(newAuthorId);
                newAuthorUserIds.Add(newAuthor.UserId);
            }
        }

        Log.LogInformation("OnCopyChat({CorrelationId}) created {Count} authors", correlationId, createdAuthors);
        context.Operation.Items.KeylessSet(newAuthorUserIds);
        context.Operation.Items.KeylessSet(newAuthorIds);
        return hasChanges;
    }

    // [EventHandler]
    public virtual async Task OnAvatarChangedEvent(AvatarChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (_, oldAvatar, changeKind) = eventCommand;
        if (changeKind != ChangeKind.Remove)
            return;

        oldAvatar.Require();

        var authors = await ListAuthorsByAvatarId(oldAvatar.UserId, oldAvatar.Id, cancellationToken).ConfigureAwait(false);

        foreach (var author in authors) {
            var command = new AuthorsBackend_Upsert(author.ChatId,
                author.Id,
                author.UserId,
                author.Version,
                new AuthorDiff { AvatarId = null, });
            await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        }
    }

    // [EventHandler]
    public virtual async Task OnAuthorLeftPlaceEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (author, oldAuthor) = eventCommand;
        if (author.ChatId is not PlaceChatId { IsRoot: true } placeChatId)
            return;

        var authorHasLeft = author.HasLeft && oldAuthor is { HasLeft: false };
        if (!authorHasLeft)
            return;

        await ExcludeUserFromPlaceChats(author.UserId, placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<AuthorFull?> GetInternal(
        ChatId chatId,
        PrincipalId principalId,
        RequestedAuthorKind authorKind,
        CancellationToken cancellationToken)
    {
        if (chatId.IsThread(out var threadChatId)) {
            var parentChatId = threadChatId.ParentChatId;
            var parentChatAuthorId = Remap(principalId, parentChatId);
            var parentChatAuthor = await GetInternal(parentChatId, parentChatAuthorId, authorKind, cancellationToken).ConfigureAwait(false);
            if (parentChatAuthor is null)
                return null;

            return parentChatAuthor with {
                Id = Remap(parentChatAuthor.Id, chatId),
                RoleIds = [],
            };
        }
        if (authorKind is RequestedAuthorKind.Default
            || chatId is not PlaceChatId placeChatId
            || placeChatId.IsRoot)
            return await GetInternal(chatId, principalId, cancellationToken).ConfigureAwait(false);

        return await GetPlaceChatAuthor(placeChatId, principalId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<AuthorFull?> GetInternal(
        ChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
    {
        AuthorFull? author;
        UserId? userId = null;
        AuthorId? authorId = null;
        if (principalId is AuthorId authorId1) {
            authorId = authorId1;
            if (authorId.ChatId != chatId)
                return null;

            if (authorId == Bots.GetWalleId(chatId))
                return Bots.GetWalle(chatId);

            var dbAuthor = await DbAuthorResolver.Get(authorId.Value, cancellationToken).ConfigureAwait(false);
            author = dbAuthor?.ToModel();
        }
        else if (principalId is UserId userId1) {
            userId = userId1;
            if (userId == Constants.User.Walle.UserId)
                return Bots.GetWalle(chatId);

            // Closes "using" block earlier
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _ = dbContext.ConfigureAwait(false);

            var dbAuthor = await dbContext.Authors
                .Include(a => a.Roles)
                .SingleOrDefaultAsync(a => a.ChatId == chatId.Value && a.UserId == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            author = dbAuthor?.ToModel();
        }
        else
            throw new ArgumentOutOfRangeException(nameof(principalId));

        if (author == null) {
            if (chatId is not PeerChatId peerChatId)
                return null;

            author = userId is not null
                ? GetDefaultPeerChatAuthor(peerChatId, userId)
                : GetDefaultPeerChatAuthor(peerChatId, authorId!);
            if (author == null)
                return null;
        }

        if (chatId is not PlaceChatId placeChatId || placeChatId.IsRoot)
            author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
    }

    [ComputeMethod]
    protected virtual async Task<AuthorId[]> ListAuthorIdsInternal(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var authorSids = await dbContext.Authors
            .Where(a => a.ChatId == chatId.Value && !a.HasLeft)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return authorSids.Select(AuthorId.Parse).ToArray();
    }

    [ComputeMethod]
    protected virtual async Task<UserId[]> ListUserIdsInternal(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var userIds = await dbContext.Authors
            .Where(a => a.ChatId == chatId.Value && !a.HasLeft && a.UserId != null)
            .Select(a => a.UserId!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return userIds.Select(UserId.Parse).ToArray();
    }

    // Private / internal methods

    private async Task<AuthorFull?> GetPlaceChatAuthor(PlaceChatId chatId, PrincipalId principalId, CancellationToken cancellationToken)
    {
        var rootChatId = chatId.RootChatId;
        var rootAuthor = await GetInternal(rootChatId, Remap(principalId, rootChatId), cancellationToken)
            .ConfigureAwait(false);
        if (rootAuthor == null)
            return null;

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        if (chat.IsPublic)
            return rootAuthor with { Id = Remap(rootAuthor.Id, chatId) };

        // If it's a private Chat on the Place, then we should have explicit author on the Chat.
        var author = await GetInternal(chatId, principalId, cancellationToken).ConfigureAwait(false);
        return CreatePrivateChatAuthor(author, rootAuthor);
    }

    private async ValueTask<AuthorFull> AddAvatar(AuthorFull author, CancellationToken cancellationToken)
    {
        var avatarId = author.AvatarId;
        if (!avatarId.IsEmpty) {
            var avatar = await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            if (avatar != null)
                return author with { Avatar = avatar };
        }
        var account = await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);
        return author with { Avatar = account?.Avatar ?? GetDefaultAvatar(author) };
    }

    private static AvatarFull GetDefaultAvatar(AuthorFull author)
        => new(author.UserId) {
            Name = RandomNameGenerator.Default.Generate(author.Id.Value),
            Bio = "",
            AvatarKey = DefaultUserPicture.GetAvatarKey(author.Id.Value),
        };

    private static AuthorFull[] GetDefaultPeerChatAuthors(PeerChatId peerChatId)
    {
        var author1 = GetDefaultPeerChatAuthor(peerChatId, peerChatId.UserId1)!;
        var author2 = GetDefaultPeerChatAuthor(peerChatId, peerChatId.UserId2)!;
        return [author1, author2];
    }

    private static AuthorFull? GetDefaultPeerChatAuthor(PeerChatId chatId, AuthorId? authorId, UserId userId)
        => authorId is null
            ? GetDefaultPeerChatAuthor(chatId, userId)
            : GetDefaultPeerChatAuthor(chatId, authorId);

    private static AuthorFull? GetDefaultPeerChatAuthor(PeerChatId chatId, AuthorId authorId)
    {
        if (authorId.ChatId.Id != chatId.Id)
            return null;
        if (authorId.LocalId == 1)
            return GetDefaultPeerChatAuthor(chatId, chatId.UserId1);
        if (authorId.LocalId == 2)
            return GetDefaultPeerChatAuthor(chatId, chatId.UserId2);
        return null;
    }

    private static AuthorFull? GetDefaultPeerChatAuthor(PeerChatId chatId, UserId userId)
    {
        var localId = chatId.IndexOf(userId) + 1;
        if (localId < 1)
            return null;

        var authorId = AuthorId.New(chatId, localId);
        var author = new AuthorFull(userId, authorId) {
            IsAnonymous = false,
            AvatarId = "",
            HasLeft = false,
        };
        return author;
    }

    private async Task<ChatId> GetAuthorChatId(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsThread(out var threadChatId))
            return await GetAuthorChatId(threadChatId.GetOutermostParent(), cancellationToken).ConfigureAwait(false);

        if (chatId is not PlaceChatId placeChatId || placeChatId.IsRoot)
            return chatId;

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        return chat is { IsPublic: true }
            ? placeChatId.PlaceId.RootChatId
            : chatId;
    }

    internal static AuthorId Remap(AuthorId authorId, ChatId targetChatId)
        => AuthorId.New(targetChatId, authorId.LocalId);

    internal static PrincipalId Remap(PrincipalId principalId, ChatId targetChatId)
    {
        if (principalId is AuthorId authorId)
            return Remap(authorId, targetChatId);

        if (principalId is UserId)
            return principalId;

        throw new ArgumentOutOfRangeException(nameof(principalId), $"Can't remap principalId with Kind={principalId.Kind}.");
    }

    private static AuthorFull? CreatePrivateChatAuthor(AuthorFull? author2, AuthorFull rootAuthor)
    {
        if (author2 == null)
            return null; // Requested Author is not a member of the Chat.

        return author2 with
        {
            HasLeft = author2.HasLeft || rootAuthor.HasLeft,
            AvatarId = rootAuthor.AvatarId, // Always use avatar for the Place.
            Avatar = rootAuthor.Avatar, // Always use avatar for the Place.
            // RoleIds = TODO(DF): should we alter roles?
        };
    }

    private async Task ExcludeUserFromPlaceChats(UserId userId, PlaceId placeId, CancellationToken cancellationToken)
    {
        var authorIds = await ListPlaceAuthorIdsByUserId(placeId, userId, cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(authorIds.Select(async authorId => {
                var author = await Get(authorId.ChatId, authorId, RequestedAuthorKind.Default, cancellationToken)
                    .ConfigureAwait(false);
                if (author == null || author.HasLeft)
                    return;

                var upsertCommand = new AuthorsBackend_Upsert(
                    authorId.ChatId,
                    author.Id,
                    default,
                    author.Version,
                    new AuthorDiff() { HasLeft = true });
                await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
            }))
            .ConfigureAwait(false);
    }

    private async Task RemovePrivilegedRoles(AuthorId authorId, CancellationToken cancellationToken)
    {
        await RemoveFromSystemRole(authorId, SystemRole.Owner, cancellationToken).ConfigureAwait(false);
        await RemoveFromSystemRole(authorId, SystemRole.Moderator, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveFromSystemRole(
        AuthorId authorId, SystemRole systemRole, CancellationToken cancellationToken)
    {
        var chatId = authorId.ChatId;
        var role = await RolesBackend.GetSystem(chatId, systemRole, cancellationToken).ConfigureAwait(false);
        if (role == null)
            return;

        var authorIds = await RolesBackend.ListAuthorIds(chatId, role.Id, cancellationToken).ConfigureAwait(false);
        if (!authorIds.Contains(authorId))
            return;

        var changeRoleCommand = new RolesBackend_Change(
            chatId,
            role.Id,
            role.Version,
            new Change<RoleDiff> {
                Update = new RoleDiff {
                    AuthorIds = new SetDiff<AuthorId[], AuthorId> {
                        RemovedItems = [authorId],
                    },
                },
            });

        await Commander.Call(changeRoleCommand, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImmutableList<AuthorFull>> ListAuthorsByAvatarId(UserId userId, Symbol avatarId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbAuthors = await dbContext.Authors
            .Where(a => a.UserId == userId.Value && a.AvatarId == avatarId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbAuthors.Select(x => x.ToModel()).ToImmutableList();
    }

    private async Task<ImmutableList<AuthorId>> ListPlaceAuthorIdsByUserId(PlaceId placeId, UserId userId, CancellationToken cancellationToken)
    {
        var authorIdPrefix = PlaceChatId.Format(placeId, Symbol.Empty);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbAuthorSids = await dbContext.Authors
            .Where(a => a.Id.StartsWith(authorIdPrefix))
            .Where(a => a.UserId == userId.Value)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbAuthorSids.Select(AuthorId.Parse).ToImmutableList();
    }
}
