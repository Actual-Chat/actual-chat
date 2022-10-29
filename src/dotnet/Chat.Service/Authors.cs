using ActualChat.Chat.Db;
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
    private IContactsBackend ContactsBackend { get; }
    private IUserPresences UserPresences { get; }
    private IServerKvas ServerKvas { get; }
    private IAuthorsBackend Backend => _backend ??= Services.GetRequiredService<IAuthorsBackend>();

    public Authors(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        ServerKvas = services.ServerKvas();
    }

    // [ComputeMethod]
    public virtual async Task<Author?> Get(
        Session session,
        string chatId,
        string authorId,
        CancellationToken cancellationToken)
    {
        // Check that user has access to chat
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var author = await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetOwn(
        Session session, string chatId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account != null)
            return await Backend.GetByUserId(chatId, account.Id, cancellationToken).ConfigureAwait(false);

        var kvas = ServerKvas.GetClient(session);
        var settings = await kvas.GetUnregisteredUserSettings(cancellationToken).ConfigureAwait(false);
        var authorId = settings.Chats.GetValueOrDefault(chatId);
        if (authorId.IsNullOrEmpty())
            return null;

        return await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetFull(
        Session session, string chatId, string authorId,
        CancellationToken cancellationToken)
    {
        var ownAuthor = await GetOwn(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        var rules = await ChatsBackend.GetRules(chatId, ownAuthor.Id, cancellationToken).ConfigureAwait(false);
        rules.Require(ChatPermissions.EditRoles);

        return await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return ImmutableArray<Symbol>.Empty;

        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return ImmutableArray<Symbol>.Empty;

        return await Backend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return ImmutableArray<Symbol>.Empty;

        return await Backend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListChatIds(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account != null)
            return await Backend.ListUserChatIds(account.Id, cancellationToken).ConfigureAwait(false);

        var kvas = ServerKvas.GetClient(session);
        var unregisteredAuthorSettings = await kvas.GetUnregisteredUserSettings(cancellationToken).ConfigureAwait(false);
        var chats = unregisteredAuthorSettings.Chats;
        var chatIds = chats.Keys.AsEnumerable();
        if (!chats.ContainsKey(Constants.Chat.AnnouncementsChatId.Value))
            chatIds = chatIds.Append(Constants.Chat.AnnouncementsChatId.Value);
        return chatIds.Select(x => (Symbol) x).ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<Presence> GetAuthorPresence(
        Session session,
        string chatId,
        string authorId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return Presence.Unknown;

        var author = await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return Presence.Offline;
        if (author.UserId.IsEmpty || author.IsAnonymous)
            return Presence.Unknown; // Important: we shouldn't report anonymous author presence
        return await UserPresences.Get(author.UserId.Value, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> CanAddToContacts(Session session, string chatId, string authorId, CancellationToken cancellationToken)
    {
        var (userId, otherUserId) = await GetPeerChatUserIds(session, chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (otherUserId.IsEmpty)
            return false;

        var contact = await ContactsBackend.Get(userId, otherUserId, cancellationToken).ConfigureAwait(false);
        return contact == null;
    }

    // [CommandHandler]
    public virtual async Task AddToContacts(IAuthors.AddToContactsCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, principalId) = command;
        var (userId, otherUserId) = await GetPeerChatUserIds(session, chatId, principalId, cancellationToken).ConfigureAwait(false);
        if (otherUserId.IsEmpty)
            return;

        _ = await ContactsBackend.GetOrCreate(userId, otherUserId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task CreateAuthors(IAuthors.CreateAuthorsCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var chatRules = await Chats.GetRules(command.Session, command.ChatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Invite);

        foreach (var userId in command.UserIds)
            await Backend.GetOrCreate(command.ChatId, userId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task SetAvatar(IAuthors.SetAvatarCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, authorId, avatarId) = command;
        var (userId, otherUserId) = await GetPeerChatUserIds(session, chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (otherUserId.IsEmpty)
            return;

        var setAvatarCommand = new IAuthorsBackend.SetAvatarCommand(chatId, authorId, avatarId);
        await Commander.Call(setAvatarCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<(Symbol UserId, Symbol OtherUserId)> GetPeerChatUserIds(
        Session session,
        Symbol chatId,
        ParsedPrincipalId principalId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return default;

        var otherUserId = await Backend.GetUserId(chatId, principalId, cancellationToken).ConfigureAwait(false);
        if (otherUserId.IsEmpty)
            return default;

        if (account.Id == otherUserId)
            return default;

        var otherAccount = await AccountsBackend.Get(otherUserId, cancellationToken).ConfigureAwait(false);
        if (otherAccount == null)
            return default;

        return (account.Id, otherAccount.Id);
    }
}
