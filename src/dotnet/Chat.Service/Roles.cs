using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class Roles : DbServiceBase<ChatDbContext>, IRoles
{
    private IAccounts Accounts { get; }
    private IChats Chats { get; }
    private IChatsBackend ChatsBackend { get; }
    private IAuthors Authors { get; }
    private IRolesBackend Backend { get; }

    public Roles(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Chats = services.GetRequiredService<IChats>();
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        Authors = services.GetRequiredService<IAuthors>();
        Backend = services.GetRequiredService<IRolesBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<Role?> Get(
        Session session, string chatId, string roleId, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwner(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!isOwner)
            return null;

        // If we're here, current user is either admin or is in owner role
        return await Backend.Get(chatId, roleId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Role>> List(
        Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is null or { HasLeft: true })
            return ImmutableArray<Role>.Empty;

        var isAuthenticated = account != null;
        var isAnonymous = author is { IsAnonymous: true };
        return await Backend
            .List(chatId, author.Id, isAuthenticated, isAnonymous, cancellationToken)
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
    public virtual async Task<Role> Change(IRoles.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, roleId, expectedVersion, change) = command;
        await RequireOwner(session, chatId, cancellationToken).ConfigureAwait(false);

        var changeCommand = new IRolesBackend.ChangeCommand(chatId, roleId, expectedVersion, change);
        return await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<bool> IsOwner(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account is { IsAdmin: true })
            return true;

        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return false;

        var ownerRole = await Backend.GetSystem(chatId, SystemRole.Owner, cancellationToken).ConfigureAwait(false);
        if (ownerRole == null || !author.RoleIds.Contains(ownerRole.Id))
            return false;

        return true;
    }

    private async Task RequireOwner(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account is { IsAdmin: true })
            return;

        var author = await Authors.GetOwn(session, chatId, cancellationToken).Require().ConfigureAwait(false);

        var ownerRole = await Backend.GetSystem(chatId, SystemRole.Owner, cancellationToken).ConfigureAwait(false);
        if (ownerRole == null || !author.RoleIds.Contains(ownerRole.Id))
            throw StandardError.Unauthorized("Only this chat's Owners role members can perform this action.");
    }
}
