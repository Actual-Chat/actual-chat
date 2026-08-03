using ActualChat.Users;

namespace ActualChat.Chat;

/// <summary>
/// Frontend service for managing places (organizational containers for chats) with session-based access control.
/// </summary>
public class Places(IServiceProvider services) : IPlaces
{
    private IAccounts Accounts => field ??= services.GetRequiredService<IAccounts>();
    private IAuthors Authors => field ??= services.GetRequiredService<IAuthors>();
    private IRoles Roles => field ??= services.GetRequiredService<IRoles>();
    private ICommander Commander => field ??= services.Commander();
    private IPlacesBackend PlacesBackend => field ??= services.GetRequiredService<IPlacesBackend>();
    private IChats Chats => field ??= services.GetRequiredService<IChats>(); // Lazy resolving to prevent cyclic dependency
    private IChatsBackend ChatsBackend => field ??= services.GetRequiredService<IChatsBackend>(); // Lazy resolving to prevent cyclic dependency

    public virtual async Task<Place?> Get(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);

        var place = await PlacesBackend.Get(placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return null;

        var rules = await GetRules(session, placeId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead())
            return null;

        return place with {
            Rules = rules,
        };
    }

    public virtual async Task<PlaceRules> GetRules(
        Session session,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);

        // NOTE: Should we eliminate the public GetRules method and always get rules from the Place instance?
        var placeRootChatRules = await Chats.GetRules(session, placeId.RootChatId, cancellationToken).ConfigureAwait(false);
        return ToPlaceRules(placeRootChatRules, placeId);
    }

    public virtual async Task<PlaceChatId?> GetWelcomeChatId(
        Session session,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);

        var place = await Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return null;

        var chatIds = await ChatsBackend.GetPublicChatIdsFor(placeId, cancellationToken).ConfigureAwait(false);
        var chats = await chatIds
            .Select(c => Chats.Get(session, c, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        // TODO(DF): Make it possible to change Welcome Chat
        var welcomeChat = chats.SkipNullItems().FirstOrDefault(c => Constants.Chat.SystemTags.Welcome == c.SystemTag)
            ?? chats.SkipNullItems().MinBy(c => c.CreatedAt);
        return welcomeChat?.Id as PlaceChatId;
    }

    public virtual async Task<UserId[]> ListUserIds(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);
        return await Authors.ListUserIds(session, placeId.RootChatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<AuthorId[]> ListAuthorIds(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);
        return await Authors.ListAuthorIds(session, placeId.RootChatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<AuthorId[]> ListOwnerIds(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);
        return await Roles.ListOwnerIds(session, placeId.RootChatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<AuthorId[]> ListModeratorIds(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);
        return await Roles.ListModeratorIds(session, placeId.RootChatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<AuthorFull?> GetOwn(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);
        return await Authors.GetOwn(session, placeId.RootChatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Author?> Get(Session session, PlaceId placeId, AuthorId authorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeId);
        ThrowIfDoesNotBelongToPlace(placeId, authorId);
        return await Authors.Get(session, placeId.RootChatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Place> OnChange(Places_Change command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, placeId, expectedVersion, placeChange) = command;

        var place = placeId is null ? null
            : await Get(session, placeId, cancellationToken).ConfigureAwait(false);

        var changePlaceCommand = new PlacesBackend_Change(placeId, expectedVersion, placeChange.RequireValid());
        if (placeChange.Kind == ChangeKind.Create) {
            var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            account.Require(AccountFull.MustBeActive);
            changePlaceCommand = changePlaceCommand with {
                OwnerId = account.Id,
            };
        }
        else {
            var requiredPermissions = placeChange.Remove
                ? PlacePermissions.Owner
                : PlacePermissions.EditProperties;
            var existingPlace = place.Require();
            existingPlace.Rules.Permissions.Require(requiredPermissions);
            if (placeChange.IsUpdate(out var placeDiff)
                && !existingPlace.Rules.IsOwner()
                && placeDiff.RequiresOwner()) {
                // Moderators get EditProperties, but structural properties stay Owner-only.
                throw PlacePermissionsExt.NotEnoughPermissions(PlacePermissions.Owner);
            }
        }

        return await Commander.Call(changePlaceCommand, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnJoin(Places_Join command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, placeId, avatarId) = command;
        var joinCommand = new Authors_Join(session, placeId.RootChatId, avatarId);
        await Commander.Call(joinCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnInvite(Places_Invite command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, placeId, userIds) = command;
        var inviteCommand = new Authors_Invite(session, placeId.RootChatId, userIds);
        await Commander.Call(inviteCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnExclude(Places_Exclude command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        var excludeCommand = new Authors_Exclude(session, authorId);
        await Commander.Call(excludeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRestore(Places_Restore command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        var restoreCommand = new Authors_Restore(session, authorId);
        await Commander.Call(restoreCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnChangeRole(Places_ChangeRole command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId, systemRole, isInRole) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        var changeRoleCommand = new Authors_ChangeRole(session, authorId, systemRole, isInRole);
        await Commander.Call(changeRoleCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    [Obsolete("2026.08: Use Places_ChangeRole. Old clients only.")]
    public virtual async Task OnPromoteToOwner(Places_PromoteToOwner command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        var changeRoleCommand = new Places_ChangeRole(session, authorId, SystemRole.Owner, true);
        await Commander.Call(changeRoleCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnLeave(Places_Leave command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, placeId) = command;
        var place = await Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return;

        place.Rules.Require(PlacePermissions.Leave);
        var leaveCommand = new Authors_Leave(session, placeId.RootChatId);
        await Commander.Call(leaveCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private static PlaceRules ToPlaceRules(AuthorRules authorRules, PlaceId placeId)
    {
        var placePermissions = (PlacePermissions)(int)authorRules.Permissions;
        if ((placePermissions & PlacePermissions.Owner) == 0) // Only Owners can invite so far.
            placePermissions &= ~PlacePermissions.Invite;
        return new(placeId, authorRules.Author, authorRules.Account, placePermissions);
    }

    private static void ThrowIfDoesNotBelongToPlace(PlaceId placeId, AuthorId authorId)
    {
        if (authorId.ChatId is not PlaceChatId placeChatId || placeChatId.PlaceId != placeId)
            throw new ArgumentOutOfRangeException(nameof(authorId));
    }

    private static void ThrowIfNonPlaceRootChatAuthor(AuthorId authorId)
    {
        if (authorId.ChatId is not PlaceChatId { IsRoot: true })
            throw new ArgumentOutOfRangeException(nameof(authorId));
    }
}
