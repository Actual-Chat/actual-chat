using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Db;
using ActualChat.Users;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class AuthorsBackend : DbServiceBase<ChatDbContext>, IAuthorsBackend
{
    private IChatsBackend? _chatsBackend;

    private IAccountsBackend AccountsBackend { get; }
    private IAvatarsBackend AvatarsBackend { get; }
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IDbEntityResolver<string, DbAuthor> DbAuthorResolver { get; }
    private IDbShardLocalIdGenerator<DbAuthor, string> DbAuthorLocalIdGenerator { get; }
    private DiffEngine DiffEngine { get; }
    private IRolesBackend RolesBackend { get; }

    public AuthorsBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        DbAuthorResolver = services.GetRequiredService<IDbEntityResolver<string, DbAuthor>>();
        DbAuthorLocalIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbAuthor, string>>();
        AvatarsBackend = services.GetRequiredService<IAvatarsBackend>();
        DiffEngine = services.GetRequiredService<DiffEngine>();
        RolesBackend = services.GetRequiredService<RolesBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> Get(
        ChatId chatId,
        AuthorId authorId,
        AuthorsBackend_GetAuthorOption option,
        CancellationToken cancellationToken)
    {
        if (chatId.IsNone || authorId.IsNone || authorId.ChatId != chatId)
            return null;

        if (option.IsRaw() || !chatId.IsPlaceChat || chatId.PlaceChatId.IsRoot)
            return await GetInternal(chatId, authorId, cancellationToken).ConfigureAwait(false);

        var rootChatId = chatId.PlaceChatId.PlaceId.ToRootChatId();
        var rootAuthor = await GetInternal(rootChatId, Remap(authorId, rootChatId), cancellationToken)
            .ConfigureAwait(false);
        if (rootAuthor == null)
            return null;

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        if (chat.IsPublic)
            return rootAuthor with { Id = Remap(rootAuthor.Id, chatId) };

        // If it's a private Chat on the Place, then we should have explicit author on the Chat.
        var author = await GetInternal(chatId, authorId, cancellationToken).ConfigureAwait(false);
        return CreatePrivateChatAuthor(author, rootAuthor);
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetByUserId(
        ChatId chatId, UserId userId,
        AuthorsBackend_GetAuthorOption option,
        CancellationToken cancellationToken)
    {
        if (chatId.IsNone || userId.IsNone)
            return null;

        if (option.IsRaw() || !chatId.IsPlaceChat || chatId.PlaceChatId.IsRoot)
            return await GetByUserIdInternal(chatId, userId, cancellationToken).ConfigureAwait(false);

        var rootChatId = chatId.PlaceChatId.PlaceId.ToRootChatId();
        var rootAuthor = await GetByUserIdInternal(rootChatId, userId, cancellationToken).ConfigureAwait(false);
        if (rootAuthor == null)
            return null;

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        if (chat.IsPublic)
            return rootAuthor with { Id = Remap(rootAuthor.Id, chatId) };

        // If it's a private Chat on the Place, then we should have explicit author on the Chat.
        var author = await GetByUserIdInternal(chatId, userId, cancellationToken).ConfigureAwait(false);
        return CreatePrivateChatAuthor(author, rootAuthor);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            return default;

        if (chatId.IsPeerChat(out var peerChatId))
            return GetDefaultPeerChatAuthors(peerChatId).Select(a => a.Id).ToApiArray();

        var targetChatId = await GetMembershipChatId(chatId, cancellationToken).ConfigureAwait(false);
        if (targetChatId.IsNone)
            return default!;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var authorSids = await dbContext.Authors
            .Where(a => a.ChatId == targetChatId && !a.HasLeft)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var authorIds = authorSids.Select(x => new AuthorId(x)).ToApiArray();

        if (targetChatId != chatId && authorIds.Count > 0)
            authorIds = RemapList(authorIds, chatId);
        return authorIds;

        static ApiArray<AuthorId> RemapList(ApiArray<AuthorId> authorIds, ChatId chatId)
            => authorIds.ToApiArray(c => Remap(c, chatId));
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<UserId>> ListUserIds(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            return default;

        if (chatId.IsPeerChat(out var peerChatId))
            return GetDefaultPeerChatAuthors(peerChatId).Select(a => a.UserId).ToApiArray();

        var targetChatId = await GetMembershipChatId(chatId, cancellationToken).ConfigureAwait(false);
        if (targetChatId.IsNone)
            return default!;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var userIds = await dbContext.Authors
            .Where(a => a.ChatId == targetChatId && !a.HasLeft && a.UserId != null)
            .Select(a => a.UserId!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return userIds.Select(x => new UserId(x)).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<AuthorFull> OnUpsert(AuthorsBackend_Upsert command, CancellationToken cancellationToken)
    {
        var (chatId, authorId, userId, expectedVersion, diff, doNotNotify) = command;
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
        if (!authorId.IsNone && authorId.ChatId != chatId)
            throw new ArgumentOutOfRangeException(nameof(command), "Invalid AuthorId.");
        if (userId.IsNone && authorId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(command), "Either AuthorId or UserId must be provided.");

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var (invAuthor, invOldAuthor) = context.Operation.Items.GetOrDefault<(AuthorFull, AuthorFull?)>();
            if (!invAuthor.Id.IsNone) {
                _ = GetInternal(chatId, invAuthor.Id, default);
                _ = GetByUserIdInternal(chatId, invAuthor.UserId, default);
                var invOldHadLeft = invOldAuthor?.HasLeft ?? true;
                if (invAuthor.HasLeft != invOldHadLeft) {
                    _ = ListAuthorIds(chatId, default);
                    _ = ListUserIds(chatId, default);
                }
            }
            return default!;
        }

        var defaultAuthor = chatId.IsPeerChat(out var peerChatId)
            ? GetDefaultPeerChatAuthor(peerChatId, authorId, userId).RequireValid(userId)
            : null;

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Can't use .ForUpdate() here due to join
        var dbAuthors = dbContext.Authors.Include(a => a.Roles);
        var dbAuthor = await (authorId.IsNone
            ? dbAuthors.FirstOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
            : dbAuthors.FirstOrDefaultAsync(a => a.ChatId == chatId && a.Id == authorId, cancellationToken)
            ).ConfigureAwait(false);
        var existingAuthor = dbAuthor?.ToModel() ?? defaultAuthor;
        var authorHasLeft = false;

        if (existingAuthor != null) {
            // Update existing author, incl. one of the default ones in peer chat
            existingAuthor.RequireVersion(expectedVersion).RequireValid(userId);
            var account = await AccountsBackend.Get(existingAuthor.UserId, cancellationToken).Require().ConfigureAwait(false);

            var author = DiffEngine.Patch(existingAuthor, diff) with {
                Version = VersionGenerator.NextVersion(existingAuthor.Version),
            };

            // Check constraints
            if (author.IsAnonymous) {
                if (!existingAuthor.IsAnonymous)
                    throw StandardError.Constraint("IsAnonymous can be changed only to false.");
            }
            else if (account.IsGuestOrNone)
                throw StandardError.Constraint("Unauthenticated authors must be anonymous.");
            if (author.HasLeft && !peerChatId.IsNone)
                throw StandardError.Constraint("Peer chat authors can't leave.");

            authorHasLeft = dbAuthor is { HasLeft: false } && author.HasLeft;

            if (dbAuthor == null) {
                // First author update in peer chat = create it
                dbAuthor = new DbAuthor(author);
                dbContext.Add(dbAuthor);
            }
            else
                dbAuthor.UpdateFrom(author);
        }
        else {
            // Create author, + we know here it's not a peer chat
            if (userId.IsNone)
                throw new ArgumentOutOfRangeException(nameof(command), "UserId is required to create a new author.");

            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chat == null || chat.HasSingleAuthor) {
                var alreadyHasAuthor = await dbContext.Authors
                    .AnyAsync(a => a.ChatId == chatId && a.UserId != userId, cancellationToken)
                    .ConfigureAwait(false);
                if (alreadyHasAuthor)
                    throw StandardError.Constraint("There can be only one author in this chat.");
            }
            var account = await AccountsBackend.Get(userId, cancellationToken).Require().ConfigureAwait(false);

            long localId;
            if (chatId is { IsPlaceChat: true, IsPlaceRootChat: false }) {
                var placeId = chatId.PlaceId;
                var placeAuthor = await GetByUserId(placeId.ToRootChatId(), userId, AuthorsBackend_GetAuthorOption.Raw, cancellationToken).ConfigureAwait(false);
                if (placeAuthor == null)
                    throw StandardError.Internal("Place root chat author must exist.");
                localId = placeAuthor.LocalId;
            }
            else
                localId = await DbAuthorLocalIdGenerator
                    .Next(dbContext, chatId, cancellationToken)
                    .ConfigureAwait(false);
            var id = new AuthorId(chatId, localId, AssumeValid.Option);
            var author = new AuthorFull(id, VersionGenerator.NextVersion()) {
                UserId = userId,
                IsAnonymous = command.Diff.IsAnonymous ?? account.IsGuestOrNone
            };
            author = DiffEngine.Patch(author, diff);

            // Check constraints
            if (author.HasLeft)
                throw StandardError.Constraint("New authors can't instantly leave the chat.");
            if (author.IsAnonymous) {
                if (author.AvatarId.IsEmpty) {
                    // Creating a random avatar for anonymous authors w/o pre-selected avatar
                    var changeCommand = new AvatarsBackend_Change(Symbol.Empty, null, new Change<AvatarFull> {
                        Create = new AvatarFull(userId) {
                            Name = RandomNameGenerator.Default.Generate(),
                            Bio = "Someone anonymous",
                            IsAnonymous = true,
                        },
                    });
                    var avatar = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
                    author = author with { AvatarId = avatar.Id };
                }
            }
            else if (account.IsGuestOrNone)
                throw StandardError.Constraint("Unauthenticated authors must be anonymous.");

            dbAuthor = new DbAuthor(author);
            dbContext.Add(dbAuthor);
        }

        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException e) when(e.Entries.All(en => en.State == EntityState.Added)) {
            Log.LogWarning(e, "Upserting author failed with DbUpdateConcurrencyException. Command: {Command}", command);
            // Author for same ChatId\UserId has already been created, let's get it
            dbAuthor = await (authorId.IsNone
                    ? dbAuthors.FirstOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                    : dbAuthors.FirstOrDefaultAsync(a => a.ChatId == chatId && a.Id == authorId, cancellationToken)
                ).ConfigureAwait(false);
            existingAuthor = dbAuthor.Require().ToModel().RequireValid(userId);
        }

        if (chatId is { IsPlaceChat: true, IsPlaceRootChat: false }) {
            // NOTE(DF): Place chat author local_id must match to the root place chat author local_id for the same user.
            var rootChatId = chatId.PlaceChatId.PlaceId.ToRootChatId();
            var rootAuthor = await GetByUserId(rootChatId,
                    new UserId(dbAuthor.UserId),
                    AuthorsBackend_GetAuthorOption.Raw,
                    cancellationToken)
                .ConfigureAwait(false);
            if (rootAuthor is null || rootAuthor.LocalId != dbAuthor.LocalId)
                throw StandardError.Constraint(
                    $"Place chat author local_id constraint is violated for author with id '{dbAuthor.Id}'. Root chat author local_id is '{(rootAuthor is null ? "?" : rootAuthor.LocalId)}'");
        }

        if (authorHasLeft)
            await RemovePrivilegedRoles(authorId, cancellationToken).ConfigureAwait(false);

        { // Nested to get a new var scope
            var author = dbAuthor.ToModel();
            context.Operation.Items.Set((author, existingAuthor));

            if (existingAuthor == null) {
                // Set chat read position to the very end
                var chatTextIdRange = await ChatsBackend
                    .GetIdRange(command.ChatId, ChatEntryKind.Text, false, cancellationToken)
                    .ConfigureAwait(false);
                var readPosition = new ChatPosition(chatTextIdRange.End - 1);
                context.Operation.AddEvent(
                    new ChatPositionsBackend_Set(author.UserId, command.ChatId, ChatPositionKind.Read, readPosition));
            }

            if (chatId.IsPeerChat(out _))
                context.Operation.AddEvent(
                    new ChatPositionsBackend_Set(author.UserId, command.ChatId, ChatPositionKind.Read, new ChatPosition()));

            // Raise events
            if (!doNotNotify)
                context.Operation.AddEvent(new AuthorChangedEvent(author, existingAuthor));
            return author;
        }
    }

    // [CommandHandler]
    public virtual async Task OnRemove(AuthorsBackend_Remove command, CancellationToken cancellationToken)
    {
        var (chatId, authorId, userId) = command;
        switch (authorId.IsNone, chatId.IsNone, userId.IsNone) {
            case (true, true, true):
                throw new ArgumentOutOfRangeException(nameof(command), "Either AuthorId or UserId or ChatId must be provided.");
            case (false, false, false):
            case (false, false, true):
            case (false, true, false):
            case (true, false, false):
                throw new ArgumentOutOfRangeException(nameof(command), "Only one property of AuthorId or UserId or ChatId must be provided.");
        }

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invAuthors = context.Operation.Items.GetOrDefault<AuthorFull[]>();
            foreach (var invAuthor in invAuthors) {
                _ = GetInternal(chatId, invAuthor.Id, default);
                _ = GetByUserIdInternal(chatId, invAuthor.UserId, default);
                _ = ListAuthorIds(chatId, default);
                _ = ListUserIds(chatId, default);
            }
            return;
        }

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var authors = new List<AuthorFull>();
        if (!authorId.IsNone) {
            var dbAuthor = await dbContext.Authors
                .FirstOrDefaultAsync(a => a.Id == authorId, cancellationToken)
                .ConfigureAwait(false);
            if (dbAuthor != null) {
                dbContext.Remove(dbAuthor);
                authors.Add(dbAuthor.ToModel());
            }
        }
        else if (!chatId.IsNone) {
            var dbAuthors = await dbContext.Authors
                .Where(a => a.ChatId == chatId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (dbAuthors.Count > 0) {
                await dbContext.Authors
                    .Where(a => a.ChatId == chatId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                authors.AddRange(dbAuthors.Select(a => a.ToModel()));
            }
        }
        else {
            var dbAuthors = await dbContext.Authors
                .Where(a => a.UserId == userId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (dbAuthors.Count > 0) {
                await dbContext.Authors
                    .Where(a => a.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                authors.AddRange(dbAuthors.Select(a => a.ToModel()));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Operation.Items.Set(authors.ToArray());
    }

    // CommandHandler
    public virtual async Task<bool> OnCopyChat(AuthorsBackend_CopyChat command, CancellationToken cancellationToken)
    {
        var (chatId, newChatId, rolesMap, correlationId) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (context.Operation.Items[typeof(List<UserId>)] is List<string> invAuthorUserSids)
                foreach (var invAuthorUserSid in invAuthorUserSids)
                    _ = GetByUserIdInternal(newChatId, new UserId(invAuthorUserSid), default);
            if (context.Operation.Items[typeof(List<AuthorId>)] is List<string> invAuthorSids)
                foreach (var invAuthorSid in invAuthorSids)
                    _ = GetInternal(newChatId, new AuthorId(invAuthorSid), default);
            return default;
        }

        var chatSid = chatId.Value;

        var placeRootChatId = newChatId.PlaceId.ToRootChatId();
        var createdAuthors = 0;
        var hasChanges = false;
        var newAuthorIds = new List<AuthorId>();
        var newAuthorUserIds = new List<UserId>();

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
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
            var userId = originalAuthor.UserId;
            if (userId.IsNone)
                throw StandardError.Internal(
                    $"Can't proceed with the migration: found an author with no associated user. AuthorId is '{dbAuthor.Id}'.");

            if (originalAuthor.IsAnonymous)
                continue;

            // Ensure there is matching place member
            // TODO(DF): Can we use AuthorsBackend_GetAuthorOption.Full? In fact, they are equivalents in this case.
            var placeMember = await GetByUserId(placeRootChatId, userId, AuthorsBackend_GetAuthorOption.Raw, cancellationToken)
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
                var newAuthorId = new AuthorId(newChatId, newLocalId, AssumeValid.Option);
                var existentAuthor = await Get(newAuthorId.ChatId, newAuthorId, AuthorsBackend_GetAuthorOption.Raw, cancellationToken)
                    .ConfigureAwait(false);
                if (existentAuthor != null)
                    continue;

                var newAuthor = originalAuthor with {
                    Id = newAuthorId,
                    RoleIds = new ApiArray<Symbol>()
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
                    var migratedRole = rolesMap.First(c => OrdinalEquals(roleId, c.Item1.Value));
                    dbContext.AuthorRoles.Add(new DbAuthorRole {
                        DbAuthorId = newAuthorId,
                        DbRoleId = migratedRole.Item2
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
        context.Operation.Items[typeof(List<AuthorId>)] = newAuthorIds.ConvertAll(c => c.Value);
        context.Operation.Items[typeof(List<UserId>)] = newAuthorUserIds.ConvertAll(c => c.Value);
        return hasChanges;
    }

    [EventHandler]
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

    [EventHandler]
    public virtual async Task OnAuthorLeftPlaceEvent(AuthorChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (author, oldAuthor) = eventCommand;
        if (!author.ChatId.IsPlaceRootChat)
            return;

        var authorHasLeft = author.HasLeft && oldAuthor is { HasLeft: false };
        if (!authorHasLeft)
            return;

        await ExcludeUserFromPlaceChats(author.UserId, author.ChatId.PlaceId, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<AuthorFull?> GetInternal(
        ChatId chatId, AuthorId authorId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsNone || authorId.IsNone || authorId.ChatId != chatId)
            return null;

        if (authorId == Bots.GetWalleId(chatId))
            return Bots.GetWalle(chatId);
        if (authorId == Bots.GetMLSearchBotId(chatId))
            return Bots.GetMLSearchBot(chatId);

        var dbAuthor = await DbAuthorResolver.Get(authorId, cancellationToken).ConfigureAwait(false);
        AuthorFull? author;
        if (dbAuthor == null) {
            if (!chatId.IsPeerChat(out var peerChatId))
                return null;

            author = GetDefaultPeerChatAuthor(peerChatId, authorId);
            if (author == null)
                return null;
        }
        else
            author = dbAuthor.ToModel();

        if (!chatId.IsPlaceChat || chatId.PlaceChatId.IsRoot)
            author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
    }

    [ComputeMethod]
    protected virtual async Task<AuthorFull?> GetByUserIdInternal(
        ChatId chatId, UserId userId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsNone || userId.IsNone)
            return null;

        if (userId == Constants.User.Walle.UserId)
            return Bots.GetWalle(chatId);

        AuthorFull? author;
        { // Closes "using" block earlier
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _ = dbContext.ConfigureAwait(false);

            var dbAuthor = await dbContext.Authors
                .Include(a => a.Roles)
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            author = dbAuthor?.ToModel();
        }
        if (author == null) {
            if (!chatId.IsPeerChat(out var peerChatId))
                return null;

            author = GetDefaultPeerChatAuthor(peerChatId, userId);
            if (author == null)
                return null;
        }

        if (!chatId.IsPlaceChat || chatId.PlaceChatId.IsRoot)
            author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // Private / internal methods

    private async ValueTask<AuthorFull> AddAvatar(AuthorFull author, CancellationToken cancellationToken)
    {
        var avatarId = author.AvatarId;
        if (!avatarId.IsEmpty) {
            var avatar = await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            if (avatar != null)
                return author with { Avatar = avatar };
        }
        var userId = author.UserId;
        var account = userId.IsNone ? null
            : await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        return author with { Avatar = account?.Avatar ?? GetDefaultAvatar(author) };
    }

    private static AvatarFull GetDefaultAvatar(AuthorFull author)
        => new(author.UserId) {
            Name = RandomNameGenerator.Default.Generate(author.Id),
            Bio = "",
            AvatarKey = DefaultUserPicture.GetAvatarKey(author.Id),
        };

    private static AuthorFull[] GetDefaultPeerChatAuthors(PeerChatId chatId)
        => new[] {
            GetDefaultPeerChatAuthor(chatId, chatId.UserId1)!,
            GetDefaultPeerChatAuthor(chatId, chatId.UserId2)!,
        };

    private static AuthorFull? GetDefaultPeerChatAuthor(PeerChatId chatId, AuthorId authorId, UserId userId)
        => authorId.IsNone
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

        var authorId = new AuthorId(chatId, localId, AssumeValid.Option);
        var author = new AuthorFull(authorId) {
            IsAnonymous = false,
            UserId = userId,
            AvatarId = "",
            HasLeft = false,
        };
        return author;
    }

    private async Task<ChatId> GetMembershipChatId(ChatId chatId, CancellationToken cancellationToken)
    {
        if (!chatId.IsPlaceChat)
            return chatId;

        var placeChatId = chatId.PlaceChatId;
        if (placeChatId.IsRoot)
            return chatId;

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return ChatId.None;

        if (!chat.IsPublic)
            return chatId;

        return placeChatId.PlaceId.ToRootChatId();
    }

    private static AuthorId Remap(AuthorId authorId, ChatId targetChatId)
        => new AuthorId(targetChatId, authorId.LocalId, AssumeValid.Option);

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
                var author = await Get(authorId.ChatId, authorId, AuthorsBackend_GetAuthorOption.Raw, cancellationToken)
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
        var chatId = authorId.ChatId;
        var ownerRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Owner, cancellationToken)
            .ConfigureAwait(false);
        if (ownerRole == null)
            return;

        var authorIds = await RolesBackend.ListAuthorIds(chatId, ownerRole.Id, cancellationToken).ConfigureAwait(false);
        var isOwner = authorIds.Contains(authorId);
        if (!isOwner)
            return;

        // Exclude from chat owners.
        var changeRoleCommand = new RolesBackend_Change(
            chatId,
            ownerRole.Id,
            ownerRole.Version,
            new Change<RoleDiff> {
                Update = new RoleDiff {
                    AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId> {
                        RemovedItems = ApiArray.New(authorId),
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
            .Where(a => a.UserId == userId && a.AvatarId == avatarId.Value)
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
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbAuthorSids.Select(AuthorId.Parse).ToImmutableList();
    }
}
