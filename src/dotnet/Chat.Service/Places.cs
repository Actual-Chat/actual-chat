﻿using ActualChat.Contacts;

namespace ActualChat.Chat;

public class Places(IServiceProvider services) : IPlaces
{
    private IChats? _chats;
    private IChatsBackend? _chatsBackend;
    private IContacts? _contacts;

    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IRoles Roles { get; } = services.GetRequiredService<IRoles>();
    private ICommander Commander { get; } = services.Commander();

    private IChats Chats => _chats ??= services.GetRequiredService<IChats>(); // Lazy resolving to prevent cyclic dependency
    private IChatsBackend ChatsBackend => _chatsBackend ??= services.GetRequiredService<IChatsBackend>(); // Lazy resolving to prevent cyclic dependency
    private IContacts Contacts => _contacts ??= services.GetRequiredService<IContacts>(); // Lazy resolving to prevent cyclic dependency

    public virtual async Task<Place?> Get(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));

        var placeRootChat = await Chats.Get(session, placeId.ToRootChatId(), cancellationToken).ConfigureAwait(false);
        return placeRootChat?.Rules.CanRead() == true ? placeRootChat.ToPlace() : null;
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
            .Collect()
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
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, placeId, expectedVersion, placeChange) = command;
        var chatChange = new Change<ChatDiff> {
            Create = placeChange.Create.HasValue ? Option<ChatDiff>.Some(ToChatDiff(placeChange.Create.Value)) : default,
            Update = placeChange.Update.HasValue ? Option<ChatDiff>.Some(ToChatDiff(placeChange.Update.Value)) : default,
            Remove = placeChange.Remove
        };
        var chatId = placeId.ToRootChatId();
        var chatChangeCommand = new Chats_Change(session, chatId, expectedVersion, chatChange);

        var placeRootChat = await Commander.Call(chatChangeCommand, true, cancellationToken).ConfigureAwait(false);
        return placeRootChat.ToPlace();
    }

    public virtual async Task OnJoin(Places_Join command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, placeId, avatarId) = command;

        var joinCommand = new Authors_Join(session, placeId.ToRootChatId(), avatarId);
        await Commander.Call(joinCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnInvite(Places_Invite command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, placeId, userIds) = command;
        var inviteCommand = new Authors_Invite(session, placeId.ToRootChatId(), userIds);
        await Commander.Call(inviteCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnExclude(Places_Exclude command, CancellationToken cancellationToken)
    {
        var (session, authorId) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var excludeCommand = new Authors_Exclude(session, authorId);
        await Commander.Call(excludeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnRestore(Places_Restore command, CancellationToken cancellationToken)
    {
        var (session, authorId) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var restoreCommand = new Authors_Restore(session, authorId);
        await Commander.Call(restoreCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnPromoteToOwner(Places_PromoteToOwner command, CancellationToken cancellationToken)
    {
        var (session, authorId) = command;
        ThrowIfNonPlaceRootChatAuthor(authorId);

        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var promoteCommand = new Authors_PromoteToOwner(session, authorId);
        await Commander.Call(promoteCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnDelete(Places_Delete command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, placeId) = command;
        var place = await Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return;

        place.Rules.Require(PlacePermissions.Owner);
        var contacts = await Contacts.ListIds(session, placeId, cancellationToken).ConfigureAwait(false);
        foreach (var contact in contacts) {
            var chatId = contact.ChatId;
            var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat != null && OrdinalEquals(Constants.Chat.SystemTags.Welcome, chat.SystemTag)) {
                var resetChatTagCommand = new Chats_Change(session, chatId, null, new Change<ChatDiff> {
                    Update = new ChatDiff { SystemTag = Symbol.Empty }
                });
                await Commander.Call(resetChatTagCommand, true, cancellationToken).ConfigureAwait(false);
            }
            var deleteChatCommand = new Chats_Change(session, chatId, null, new Change<ChatDiff> { Remove = true });
            await Commander.Call(deleteChatCommand, true, cancellationToken).ConfigureAwait(false);
        }

        var deleteRootChatCommand = new Chats_Change(session, place.Id.ToRootChatId(), null, new Change<ChatDiff> { Remove = true });
        await Commander.Call(deleteRootChatCommand, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnLeave(Places_Leave command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, placeId) = command;
        var place = await Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return;

        place.Rules.Require(PlacePermissions.Leave);
        await Commander.Call(new Authors_Leave(session, placeId.ToRootChatId()), true, cancellationToken).ConfigureAwait(false);
    }

    private static ChatDiff ToChatDiff(PlaceDiff placeDiff)
        => new() {
            IsPublic = placeDiff.IsPublic,
            Title = placeDiff.Title,
            Kind = ChatKind.Place,
            MediaId = placeDiff.MediaId,

            AllowGuestAuthors = null,
            AllowAnonymousAuthors = null,
            IsTemplate = null,
            TemplateId = Option<ChatId?>.None,
            TemplatedForUserId = Option<UserId?>.None,
        };

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
