using ActualChat.Chat.Db;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <summary>
/// Frontend service for managing chat roles and permissions with session-based access control.
/// </summary>
public class Roles(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IRoles
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IRolesBackend Backend { get; } = services.GetRequiredService<IRolesBackend>();

    // [ComputeMethod]
    public virtual async Task<Role?> Get(
        Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwner(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!isOwner)
            return null;

        // If we're here, current user is either admin or is in owner role
        return await Backend.Get(chatId, roleId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Role[]> List(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is null or { HasLeft: true })
            return [];

        var isGuest = account.IsGuest;
        var isAnonymous = author is { IsAnonymous: true };
        return await Backend
            .List(chatId, author.Id, isGuest, isAnonymous, cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<AuthorId[]> ListAuthorIds(
        Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwner(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!isOwner)
            return [];

        // If we're here, current user is either admin or is in owner role
        return await Backend.ListAuthorIds(chatId, roleId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual Task<AuthorId[]> ListOwnerIds(
        Session session, ChatId chatId, CancellationToken cancellationToken)
        => ListSystemRoleAuthorIds(session, chatId, SystemRole.Owner, cancellationToken);

    // [ComputeMethod]
    public virtual Task<AuthorId[]> ListModeratorIds(
        Session session, ChatId chatId, CancellationToken cancellationToken)
        => ListSystemRoleAuthorIds(session, chatId, SystemRole.Moderator, cancellationToken);

    // [CommandHandler]
    public virtual async Task<Role> OnChange(Roles_Change command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, chatId, roleId, expectedVersion, change) = command;
        await RequireOwner(session, chatId, cancellationToken).ConfigureAwait(false);

        var changeCommand = new RolesBackend_Change(chatId, roleId, expectedVersion, change);
        return await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<AuthorId[]> ListSystemRoleAuthorIds(
        Session session, ChatId chatId, SystemRole systemRole, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor == null)
            return [];

        var rules = await ChatsBackend.GetRules(chatId, ownAuthor.Id, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return [];

        var authorIds = await Backend
            .ListSystemRoleAuthorIds(ChatsBackend, chatId, systemRole, cancellationToken)
            .ConfigureAwait(false);
        if (!rules.IsOwner())
            authorIds = await MaskAnonymousAuthors(authorIds, cancellationToken).ConfigureAwait(false);
        return authorIds;
    }

    private async Task<bool> IsOwner(Session session, ChatId chatId, CancellationToken cancellationToken)
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

    private async Task RequireOwner(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account is { IsAdmin: true })
            return;

        var author = await Authors.GetOwn(session, chatId, cancellationToken).Require().ConfigureAwait(false);

        var ownerRole = await Backend.GetSystem(chatId, SystemRole.Owner, cancellationToken).ConfigureAwait(false);
        if (ownerRole == null || !author.RoleIds.Contains(ownerRole.Id))
            throw StandardError.Unauthorized("Only this chat's Owners role members can perform this action.");
    }

    private async Task<AuthorId[]> MaskAnonymousAuthors(
        AuthorId[] authorIds,
        CancellationToken cancellationToken)
    {
        List<AuthorId>? toExclude = null;
        foreach (var authorId in authorIds) {
            var author = await AuthorsBackend.Get(authorId.ChatId, authorId, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
            if (author != null && author.IsAnonymous) {
                toExclude ??= new List<AuthorId>();
                toExclude.Add(authorId);
            }
        }
        if (toExclude == null)
            return authorIds;

        return authorIds.Except(toExclude).ToArray();
    }
}
