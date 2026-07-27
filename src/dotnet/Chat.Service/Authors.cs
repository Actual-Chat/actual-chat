using ActualChat.Chat.Db;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <summary>
/// Frontend service for managing chat authors (participants) with session-based access control.
/// </summary>
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class Authors(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IAuthors
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IAvatars Avatars => field ??= Services.GetRequiredService<IAvatars>();
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IUserPresences UserPresences { get; } = services.GetRequiredService<IUserPresences>();
    private IContactsBackend ContactsBackend => field ??= Services.GetRequiredService<IContactsBackend>();
    private IRoles Roles => field ??= Services.GetRequiredService<IRoles>();
    private IRolesBackend RolesBackend => field ??= Services.GetRequiredService<IRolesBackend>();
    private IAuthorsBackend Backend => field ??= Services.GetRequiredService<IAuthorsBackend>();

    // [ComputeMethod]
    public virtual async Task<Author?> Get(
        Session session, ChatId chatId, AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var authorFull = await Backend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
        if (authorFull is null)
            return null;

        var userId = authorFull.UserId;
        var author = authorFull.ToAuthor();
        if (userId.IsGuestOrNull())
            return author;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.Id == userId)
            return author; // Its own author

        var contactId = ContactId.NewUser(account.Id, userId);
        var contact = await ContactsBackend.Get(account.Id, contactId, cancellationToken).ConfigureAwait(false);
        var preferredPeerName = contact.PreferredPeerName;
        if (!preferredPeerName.IsNullOrEmpty() && preferredPeerName != author.Avatar.Name) {
            var avatar = author.Avatar with { Name = preferredPeerName };
            author = author with { Avatar = avatar };
        }
        return author;
    }

    public virtual async Task<AuthorFull?> GetOwn(
        Session session, ChatId chatId,
        CancellationToken cancellationToken)
    {
        // This method is used by Chats.GetRules, etc., so it shouldn't check
        // the ability to access the chat, otherwise we'll hit the recursion here.

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.GetByUserId(chatId, account.Id, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Author?> GetByUserId(
        Session session, ChatId chatId, UserId userId,
        CancellationToken cancellationToken)
    {
        if (userId.IsGuestOrNull())
            return null;

        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var author = await Backend.GetByUserId(chatId, userId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
        if (author is null || author.IsAnonymous)
            return null; // Never de-anonymize: an anonymous participant isn't resolvable by their user id.

        return author.ToAuthor();
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetFull(
        Session session, ChatId chatId, AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var ownAuthor = await GetOwn(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        if (ownAuthor.Id == authorId)
            return ownAuthor;

        var rules = await ChatsBackend.GetRules(chatId, ownAuthor.Id, cancellationToken).ConfigureAwait(false);
        if (!rules.Has(ChatPermissions.EditRoles))
            return null;

        return await Backend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Account?> GetAccount(
        Session session, ChatId chatId, AuthorId authorId,
        CancellationToken cancellationToken)
    {
        // In fact, de-anonymizes the author
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var author = await Backend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return null;

        if (author.IsAnonymous) {
            var ownAccount = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            return ownAccount.Id == author.UserId ? ownAccount.ToAccount() : null;
        }

        var account = await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);
        return account.ToAccount();
    }

    // [ComputeMethod]
    public virtual async Task<AuthorId[]> ListAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return [];

        return await Backend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<UserId[]> ListUserIds(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return [];

        return await Backend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Presence> GetPresence(
        Session session,
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return Presence.Unknown;

        var author = await Backend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return Presence.Offline;

        if (author.IsAnonymous)
            return Presence.Unknown; // Important: we shouldn't report anonymous author presence

        return await UserPresences.Get(author.UserId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiNullable8<Moment>> GetLastCheckIn(
        Session session,
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var author = await Backend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return null;

        if (author.IsAnonymous)
            return null; // Important: we shouldn't report anonymous author presence

        return await UserPresences.GetLastCheckIn(author.UserId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<AuthorFull> OnJoin(Authors_Join command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, chatId, avatarId, joinAnonymously) = command;
        chatId.EnsureNonThread();

        var author = await GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is { HasLeft: false })
            return author;

        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Join);

        if (!avatarId.IsEmpty) {
            var avatar = await Avatars.GetOwn(session, avatarId, cancellationToken).ConfigureAwait(false);
            avatar.Require();
            if (joinAnonymously.GetValueOrDefault() && !avatar.IsAnonymous)
                throw StandardError.Constraint("Anonymous avatar should be used to join anonymously.");
        }

        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);

        if (account.IsGuestOrNull()) {
            if (!chat.AllowGuestAuthors)
                throw StandardError.Constraint("The chat does not allow to join with guest account.");
            if (joinAnonymously == false)
                throw StandardError.Constraint(nameof(Authors_Join.JoinAnonymously)
                    + " should be true or not be specified for guest account.");
        }
        else {
            if (joinAnonymously.GetValueOrDefault()) {
                if (!chat.AllowAnonymousAuthors)
                    throw StandardError.Constraint("The chat does not allow to join anonymously.");
            }
        }

        var upsertCommand = new AuthorsBackend_Upsert(
            chatId, author?.Id ?? null, account.Id, null,
            new AuthorDiff() {
                IsAnonymous = joinAnonymously,
                HasLeft = false,
                AvatarId = avatarId.NullIfEmpty(),
            });
        author = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

        var userSettingsUI = Services.UserSettingsUI(session);
        var inviteSettings = (ChatInviteSettings?)await userSettingsUI
            .Get(ChatInviteSettings.GetKey(chatId), cancellationToken)
            .ConfigureAwait(false);
        if (inviteSettings?.ActivationKey is not null) {
            // Remove the invite
            var removeCommand = new UserSettings_Set(session, ChatInviteSettings.GetKey(chatId), null);
            await Commander.Call(removeCommand, true, cancellationToken).ConfigureAwait(false);
        }

        return author;
    }

    // [CommandHandler]
    public virtual async Task OnLeave(Authors_Leave command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;
        chatId.EnsureNonThread();
        var author = await GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.HasLeft)
            return;

        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;
        chat.Rules.Require(ChatPermissions.Leave);

        if (chat.Rules.IsOwner()) {
            var ownerIds = await Roles.ListOwnerIds(session, chatId, default).ConfigureAwait(false);
            var hasAnotherOwner = ownerIds.Any(c => c.Id != author.Id.Value);
            if (!hasAnotherOwner)
                throw StandardError.Constraint("You can't leave this chat because you are its only owner. Please add another chat owner first.");
        }

        var upsertCommand = new AuthorsBackend_Upsert(
            chatId, author.Id, null, author.Version,
            new AuthorDiff() { HasLeft = true });
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnInvite(Authors_Invite command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, userIds, joinAnonymously) = command;
        chatId.EnsureNonThread();
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.CanInvite().RequireTrue("You can't invite members in this chat.");
        ValidatePlaceMembershipRules(chat);

        foreach (var userId in userIds) {
            // TODO(DF): to think if we can switch here to AuthorsBackend_GetAuthorOption.Full?
            // Can we use AuthorsBackendExt.EnsureJoined as before?
            var author = await Backend.GetByUserId(chatId, userId, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
            if (author != null) {
                if (author.HasLeft)
                    await RestoreAuthorMembership(author, cancellationToken).ConfigureAwait(false);
            }
            else {
                if (joinAnonymously == true && !chat.AllowAnonymousAuthors)
                    throw StandardError.Constraint("The chat does not allow to join anonymously.");
                var authorDiff = new AuthorDiff {
                    IsAnonymous = joinAnonymously.GetValueOrDefault(chat.AllowAnonymousAuthors)
                };
                var upsertAuthorCommand = new AuthorsBackend_Upsert(chatId, null, userId, null, authorDiff);
                var commander = Backend.GetCommander();
                await commander.Call(upsertAuthorCommand, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // [CommandHandler]
    public virtual async Task OnExclude(Authors_Exclude command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        var chatId = authorId.ChatId;
        chatId.EnsureNonThread();
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.EditMembers);
        ValidatePlaceMembershipRules(chat);

        var author = await Backend.Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
        if (author == null || author.HasLeft)
            return;

        if (chat.Rules.Account.Require().Id == author.UserId)
            throw StandardError.Constraint("You can't remove yourself from chat members.");

        var ownerIds = await Roles.ListOwnerIds(session, chatId, cancellationToken).ConfigureAwait(false);
        var isOwner = ownerIds.Contains(authorId);
        if (isOwner)
            throw StandardError.Constraint("You can't remove an owner of this chat from chat members.");

        if (authorId.LocalId == Constants.User.Sherlock.AuthorLocalId)
            throw StandardError.Constraint("You can't remove an AI search bot from chat members.");

        var upsertCommand = new AuthorsBackend_Upsert(
            chatId, author.Id, null, author.Version,
            new AuthorDiff() { HasLeft = true });
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRestore(Authors_Restore command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        var chatId = authorId.ChatId;
        chatId.EnsureNonThread();
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.EditMembers);

        var author = await Get(session, chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author is not { HasLeft: true })
            return;

        await RestoreAuthorMembership(author, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSetAvatar(Authors_SetAvatar command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, avatarId) = command;
        chatId.EnsureNonThread();
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);

        var rootChatId = chatId.RootChatId;
        var avatar = await Avatars.GetOwn(session, avatarId, cancellationToken).Require().ConfigureAwait(false);
        var author = await GetOwn(session, rootChatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.AvatarId == avatar.Id)
            return;

        var authorDiff = new AuthorDiff() {
            AvatarId = avatar.Id,
        };
        if (author.IsAnonymous && !avatar.IsAnonymous)
            // Revealing the anonymous author
            authorDiff = authorDiff with { IsAnonymous = false };

        var upsertCommand = new AuthorsBackend_Upsert(
            rootChatId, author.Id, null, author.Version,
            authorDiff);
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnPromoteToOwner(Authors_PromoteToOwner command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        var chatId = authorId.ChatId;
        chatId.EnsureNonThread();
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.Owner);
        ValidatePlaceMembershipRules(chat);

        var author = await Backend.Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
        if (author == null || author.HasLeft)
            throw StandardError.Constraint("The selected author has already left the chat.");

        if (chat.Rules.Account.Require().Id == author.UserId)
            return;

        var ownerRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Owner, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var changeRoleCommand = new RolesBackend_Change(
            chatId,
            ownerRole.Id,
            ownerRole.Version,
            new Change<RoleDiff> {
                Update = new RoleDiff {
                    AuthorIds = new SetDiff<AuthorId[], AuthorId> {
                        AddedItems = [authorId],
                    },
                },
            });

        await Commander.Call(changeRoleCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task RestoreAuthorMembership(Author author, CancellationToken cancellationToken)
    {
        var upsertCommand = new AuthorsBackend_Upsert(
            author.ChatId, author.Id, null, author.Version,
            new AuthorDiff() { HasLeft = false });
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidatePlaceMembershipRules(Chat chat)
    {
        if (chat is { Id: PlaceChatId { IsRoot: false }, IsPublic: true })
            throw StandardError.Constraint("You must manage place public chat membership via place settings.");
    }
}
