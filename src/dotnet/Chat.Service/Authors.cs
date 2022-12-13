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
    private IChats? _chats;
    private IChatsBackend? _chatsBackend;

    private IAccounts Accounts { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IUserPresences UserPresences { get; }
    private IServerKvas ServerKvas { get; }
    private IAuthorsBackend Backend => _backend ??= Services.GetRequiredService<IAuthorsBackend>();

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
        return author;
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
        if (author.IsAnonymous || author.UserId.IsNone)
            return null;

        var account = await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);
        return account.ToAccount();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return ImmutableArray<AuthorId>.Empty;

        return await Backend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<UserId>> ListUserIds(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return ImmutableArray<UserId>.Empty;

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
    public async Task<AuthorFull> Join(IAuthors.JoinCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId) = command;
        var author = await GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is { HasLeft: false })
            return author;

        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.Join);

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var ensureExistsCommand = new IAuthorsBackend.UpsertCommand(chatId, account.Id, true);
        author = await Commander.Call(ensureExistsCommand, true, cancellationToken).ConfigureAwait(false);

        var invite = await ServerKvas.Get(session, ServerKvasInviteKey.ForChat(chatId), cancellationToken).ConfigureAwait(false);
        if (invite.HasValue) {
            // Remove the invite
            var removeInviteCommand = new IServerKvas.SetCommand(session, ServerKvasInviteKey.ForChat(chatId), null);
            await Commander.Call(removeInviteCommand, true, cancellationToken).ConfigureAwait(false);
        }

        return author;
    }

    // [CommandHandler]
    public virtual async Task Leave(IAuthors.LeaveCommand command, CancellationToken cancellationToken)
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

        var changeHasLeftCommand = new IAuthorsBackend.ChangeHasLeftCommand(chatId, author.Id, true);
        await Commander.Call(changeHasLeftCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task Invite(IAuthors.InviteCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, userIds) = command;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.Invite);

        foreach (var userId in userIds)
            await Backend.EnsureJoined(chatId, userId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task SetAvatar(IAuthors.SetAvatarCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, avatarId) = command;
        await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);

        var author = await GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        var setAvatarCommand = new IAuthorsBackend.SetAvatarCommand(chatId, author.Id, avatarId);
        await Commander.Call(setAvatarCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
