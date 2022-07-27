using System.Security;
using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatRoles : DbServiceBase<ChatDbContext>, IChatRoles
{
    private IAccounts Accounts { get; }
    private IChats Chats { get; }
    private IChatsBackend ChatsBackend { get; }
    private IChatAuthors ChatAuthors { get; }
    private IChatRolesBackend Backend { get; }

    public ChatRoles(IServiceProvider services) : base(services)
    {
        Accounts = Services.GetRequiredService<IAccounts>();
        Chats = Services.GetRequiredService<IChats>();
        ChatsBackend = Services.GetRequiredService<IChatsBackend>();
        ChatAuthors = Services.GetRequiredService<IChatAuthors>();
        Backend = Services.GetRequiredService<IChatRolesBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ChatRole?> Get(
        Session session, string chatId, string roleId, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwner(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!isOwner)
            return null;

        // If we're here, current user is either admin or is in owner role
        return await Backend.Get(chatId, roleId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ChatRole>> List(
        Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        var author = await ChatAuthors.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        var isAuthenticated = account != null;
        var isAdmin = account?.IsAdmin ?? false;
        var effectiveAuthor = author is { HasLeft: false } ? author : null;
        return await Backend
            .List(chatId, effectiveAuthor?.Id, isAuthenticated, isAdmin, cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListAuthorIds(
        Session session, string chatId, string roleId, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwner(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!isOwner)
            return ImmutableArray<Symbol>.Empty;

        // If we're here, current user is either admin or is in owner role
        return await Backend.ListAuthorIds(chatId, roleId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<ChatRole?> Change(IChatRoles.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, roleId, expectedVersion, change) = command;
        await RequireOwner(session, chatId, cancellationToken).ConfigureAwait(false);

        var cmd = new IChatRolesBackend.ChangeCommand(chatId, roleId, expectedVersion, change);
        return await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<bool> IsOwner(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account is { IsAdmin: true })
            return true;

        var author = await ChatAuthors.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return false;

        var ownerRole = await Backend.GetSystem(chatId, SystemChatRole.Owners, cancellationToken).ConfigureAwait(false);
        if (ownerRole == null || !author.RoleIds.Contains(ownerRole.Id))
            return false;

        return true;
    }

    private async Task RequireOwner(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account is { IsAdmin: true })
            return;

        var author = await ChatAuthors.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);

        var ownerRole = await Backend.GetSystem(chatId, SystemChatRole.Owners, cancellationToken).ConfigureAwait(false);
        if (ownerRole == null || !author.RoleIds.Contains(ownerRole.Id))
            throw StandardError.Unauthorized("Only this chat's Owners role members can perform this action.");
    }
}
