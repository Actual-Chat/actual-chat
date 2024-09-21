﻿using ActualChat.Users;

namespace ActualChat.Chat;

public class Places(IServiceProvider services) : IPlaces
{
    private IChats? _chats;
    private IChatsBackend? _chatsBackend;

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IRoles Roles { get; } = services.GetRequiredService<IRoles>();
    private ICommander Commander { get; } = services.Commander();

    private IPlacesBackend PlacesBackend { get; } = services.GetRequiredService<IPlacesBackend>();
    private IChats Chats => _chats ??= services.GetRequiredService<IChats>(); // Lazy resolving to prevent cyclic dependency
    private IChatsBackend ChatsBackend => _chatsBackend ??= services.GetRequiredService<IChatsBackend>(); // Lazy resolving to prevent cyclic dependency

    public virtual async Task<Place?> Get(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));

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
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));

        var placeRootChatRules = await Chats.GetRules(session, placeId.ToRootChatId(), cancellationToken).ConfigureAwait(false);
        return placeRootChatRules.ToPlaceRules(placeId)!;
    }

    public virtual async Task<ChatId> GetWelcomeChatId(
        Session session,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));

        var place = await Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return ChatId.None;

        var chatIds = await ChatsBackend.GetPublicChatIdsFor(placeId, cancellationToken).ConfigureAwait(false);
        var chats = await chatIds
            .Select(c => Chats.Get(session, c, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        // TODO(DF): make it possible to configure Welcome Chat
        var welcomeChat = chats.SkipNullItems().FirstOrDefault(c => OrdinalEquals(Constants.Chat.SystemTags.Welcome, c.SystemTag))
            ?? chats.SkipNullItems().MinBy(c => c.CreatedAt);
        return welcomeChat?.Id ?? ChatId.None;
    }

    public virtual async Task<ApiArray<UserId>> ListUserIds(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ThrowIfNone(placeId);
        return await Authors.ListUserIds(session, placeId.ToRootChatId(), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ApiArray<AuthorId>> ListAuthorIds(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ThrowIfNone(placeId);
        return await Authors.ListAuthorIds(session, placeId.ToRootChatId(), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ApiArray<AuthorId>> ListOwnerIds(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ThrowIfNone(placeId);
        return await Roles.ListOwnerIds(session, placeId.ToRootChatId(), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<AuthorFull?> GetOwn(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        ThrowIfNone(placeId);
        return await Authors.GetOwn(session, placeId.ToRootChatId(), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Author?> Get(Session session, PlaceId placeId, AuthorId authorId, CancellationToken cancellationToken)
    {
        ThrowIfNone(placeId);
        ThrowIfDoesNotBelongToPlace(placeId, authorId);
        return await Authors.Get(session, placeId.ToRootChatId(), authorId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Place> OnChange(Places_Change command, CancellationToken cancellationToken)
    {
        var (session, placeId, expectedVersion, placeChange) = command;

        var place = placeId.IsNone ? null
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
            place.Require().Rules.Permissions.Require(requiredPermissions);
        }

        return await Commander.Call(changePlaceCommand, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnJoin(Places_Join command, CancellationToken cancellationToken)
    {
        var (session, placeId, avatarId) = command;
        var joinCommand = new Authors_Join(session, placeId.ToRootChatId(), avatarId);
        await Commander.Call(joinCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnInvite(Places_Invite command, CancellationToken cancellationToken)
    {
        var (session, placeId, userIds) = command;
        var inviteCommand = new Authors_Invite(session, placeId.ToRootChatId(), userIds);
        await Commander.Call(inviteCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnExclude(Places_Exclude command, CancellationToken cancellationToken)
    {
        var (session, authorId) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        var excludeCommand = new Authors_Exclude(session, authorId);
        await Commander.Call(excludeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnRestore(Places_Restore command, CancellationToken cancellationToken)
    {
        var (session, authorId) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        var restoreCommand = new Authors_Restore(session, authorId);
        await Commander.Call(restoreCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnPromoteToOwner(Places_PromoteToOwner command, CancellationToken cancellationToken)
    {
        var (session, authorId) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        var promoteCommand = new Authors_PromoteToOwner(session, authorId);
        await Commander.Call(promoteCommand, true, cancellationToken).ConfigureAwait(false);
    }

    [Obsolete($"2024.06: Use '{nameof(Places_Change)}' with Change.Remove instead.")]
    public virtual async Task OnDelete(Places_Delete command, CancellationToken cancellationToken)
    {
        var placeChangeCommand = new Places_Change(command.Session, command.PlaceId, null, Change.Remove<PlaceDiff>());
        await Commander.Call(placeChangeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnLeave(Places_Leave command, CancellationToken cancellationToken)
    {
        var (session, placeId) = command;
        var place = await Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return;

        place.Rules.Require(PlacePermissions.Leave);
        var leaveCommand = new Authors_Leave(session, placeId.ToRootChatId());
        await Commander.Call(leaveCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private static void ThrowIfNone(PlaceId placeId)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));
    }

    private static void ThrowIfDoesNotBelongToPlace(PlaceId placeId, AuthorId authorId)
    {
        if (!authorId.ChatId.PlaceChatId.IsRoot || authorId.ChatId.PlaceChatId.PlaceId != placeId)
            throw new ArgumentOutOfRangeException(nameof(authorId));
    }

    private static void ThrowIfNonPlaceRootChatAuthor(AuthorId authorId)
    {
        if (!authorId.ChatId.PlaceChatId.IsRoot)
            throw new ArgumentOutOfRangeException(nameof(authorId));
    }
}
