using ActualChat.Chat.Db;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class Authors : DbServiceBase<ChatDbContext>, IAuthors
{
    private IAuthorsBackend? _backend;
    private IAvatars? _avatars;
    private IChats? _chats;
    private IChatsBackend? _chatsBackend;
    private IRoles? _roles;
    private IRolesBackend? _rolesBackend;

    private IAccounts Accounts { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IUserPresences UserPresences { get; }
    private IServerKvas ServerKvas { get; }
    private IAuthorsBackend Backend => _backend ??= Services.GetRequiredService<IAuthorsBackend>();
    private IAvatars Avatars => _avatars ??= Services.GetRequiredService<IAvatars>();
    private IRoles Roles => _roles ??= Services.GetRequiredService<IRoles>();
    private IRolesBackend RolesBackend => _rolesBackend ??= Services.GetRequiredService<IRolesBackend>();

    public Authors(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        ServerKvas = services.ServerKvas();
    }

    // [ComputeMethod]
    public virtual async Task<Author?> Get(
        Session session, ChatId chatId, AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var author = await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        return author.ToAuthor();
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetOwn(
        Session session, ChatId chatId,
        CancellationToken cancellationToken)
    {
        // This method is used by Chats.GetRules, etc., so it shouldn't check
        // the ability to access the chat, otherwise we'll hit the recursion here.

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.GetByUserId(chatId, account.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetFull(
        Session session, ChatId chatId, AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var ownAuthor = await GetOwn(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        if (ownAuthor.Id == authorId)
            return ownAuthor;

        var principalId = new PrincipalId(ownAuthor.Id, AssumeValid.Option);
        var rules = await ChatsBackend.GetRules(chatId, principalId, cancellationToken).ConfigureAwait(false);
        if (!rules.Has(ChatPermissions.EditRoles))
            return null;

        return await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
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

        var author = await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
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
    public virtual async Task<ApiArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return default;

        return await Backend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<UserId>> ListUserIds(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return default;

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

        var author = await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return Presence.Offline;
        if (author.IsAnonymous || author.UserId.IsNone)
            return Presence.Unknown; // Important: we shouldn't report anonymous author presence

        return await UserPresences.Get(author.UserId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<AuthorFull> OnJoin(Authors_Join command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, avatarId, joinAnonymously) = command;
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

        if (account.IsGuestOrNone) {
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
            chatId, author?.Id ?? default, account.Id, null,
            new AuthorDiff() {
                IsAnonymous = joinAnonymously,
                HasLeft = false,
                AvatarId = avatarId.NullIfEmpty(),
            });
        author = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

        var invite = await ServerKvas
            .GetClient(session)
            .TryGet<string>(ServerKvasInviteKey.ForChat(chatId), cancellationToken).ConfigureAwait(false);
        if (invite.HasValue) {
            // Remove the invite
            var removeInviteCommand = new ServerKvas_Set(session, ServerKvasInviteKey.ForChat(chatId), null);
            await Commander.Call(removeInviteCommand, true, cancellationToken).ConfigureAwait(false);
        }

        return author;
    }

    // [CommandHandler]
    public virtual async Task OnLeave(Authors_Leave command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;
        var author = await GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.HasLeft)
            return;

        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;
        chat.Rules.Require(ChatPermissions.Leave);

        if (chat.Rules.IsOwner()) {
            var ownerIds = await Roles.ListOwnerIds(session, chatId, default).ConfigureAwait(false);
            var hasAnotherOwner = ownerIds.Any(c => c.Id != author.Id);
            if (!hasAnotherOwner)
                throw StandardError.Constraint("You can't leave this chat because you are its only owner. Please add another chat owner first.");

            var ownerRole = await RolesBackend
                .GetSystem(chatId, SystemRole.Owner, cancellationToken)
                .Require()
                .ConfigureAwait(false);

            // Exclude from chat owners.
            var changeRoleCommand = new RolesBackend_Change(
                chatId,
                ownerRole.Id,
                ownerRole.Version,
                new Change<RoleDiff> {
                    Update = new RoleDiff {
                        AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId> {
                            RemovedItems = ApiArray.New(author.Id),
                        },
                    },
                });

            await Commander.Call(changeRoleCommand, true, cancellationToken).ConfigureAwait(false);
        }

        var upsertCommand = new AuthorsBackend_Upsert(
            chatId, author.Id, default, author.Version,
            new AuthorDiff() { HasLeft = true });
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnInvite(Authors_Invite command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, userIds) = command;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.CanInvite().RequireTrue("You can't invite members in this chat.");

        foreach (var userId in userIds) {
            var author = await Backend.EnsureJoined(chatId, userId, cancellationToken).ConfigureAwait(false);
            if (author.HasLeft)
                await RestoreAuthorMembership(cancellationToken, author).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task OnExclude(Authors_Exclude command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        var chatId = authorId.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.EditMembers);

        var author = await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.HasLeft)
            return;

        if (chat.Rules.Account.Id == author.UserId)
            throw StandardError.Constraint("You can't remove yourself from chat members.");

        var ownerIds = await Roles.ListOwnerIds(session, chatId, cancellationToken).ConfigureAwait(false);
        var isOwner = ownerIds.Contains(authorId);
        if (isOwner)
            throw StandardError.Constraint("You can't remove an owner of this chat from chat members.");

        var upsertCommand = new AuthorsBackend_Upsert(
            chatId, author.Id, default, author.Version,
            new AuthorDiff() { HasLeft = true });
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRestore(Authors_Restore command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        var chatId = authorId.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.EditMembers);

        var author = await Get(session, chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author == null || !author.HasLeft)
            return;

        await RestoreAuthorMembership(cancellationToken, author).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSetAvatar(Authors_SetAvatar command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, avatarId) = command;
        await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);

        var author = await GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.AvatarId == command.AvatarId)
            return;

        var upsertCommand = new AuthorsBackend_Upsert(
            chatId, author.Id, default, author.Version,
            new AuthorDiff() { AvatarId = avatarId });
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnPromoteToOwner(Authors_PromoteToOwner command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, authorId) = command;
        var chatId = authorId.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.Owner);

        var author = await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author == null || author.HasLeft)
            throw StandardError.Constraint("The selected author has already left the chat.");

        if (chat.Rules.Account.Id == author.UserId)
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
                    AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId> {
                        AddedItems = ApiArray.New(authorId),
                    },
                },
            });

        await Commander.Call(changeRoleCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task RestoreAuthorMembership(CancellationToken cancellationToken, Author author)
    {
        var upsertCommand = new AuthorsBackend_Upsert(
            author.ChatId,
            author.Id,
            default,
            author.Version,
            new AuthorDiff() { HasLeft = false });
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
