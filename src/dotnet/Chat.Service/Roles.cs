using ActualChat.Chat.Db;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

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
    public virtual async Task<ApiArray<Role>> List(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is null or { HasLeft: true })
            return default;

        var isGuest = account.IsGuestOrNone;
        var isAnonymous = author is { IsAnonymous: true };
        return await Backend
            .List(chatId, author.Id, isGuest, isAnonymous, cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListAuthorIds(
        Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwner(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!isOwner)
            return default;

        // If we're here, current user is either admin or is in owner role
        return await Backend.ListAuthorIds(chatId, roleId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListOwnerIds(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor == null)
            return default;

        var principalId = new PrincipalId(ownAuthor.Id, AssumeValid.Option);
        var rules = await ChatsBackend.GetRules(chatId, principalId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanSeeMembers())
            return default;

        var targetChatId = chatId;
        if (targetChatId.IsPlaceChat && !targetChatId.IsPlaceRootChat) {
            var chat = await ChatsBackend.Get(targetChatId, cancellationToken).ConfigureAwait(false);
            if (chat == null)
                return default; // Chat should be not null here, but do check for safety.

            if (chat.IsPublic)
                targetChatId = chatId.PlaceChatId.PlaceId.ToRootChatId(); // For public place chats take owner list from root place chat.
        }

        var ownerRole = await Backend
            .GetSystem(targetChatId, SystemRole.Owner, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var authorIds = await Backend.ListAuthorIds(targetChatId, ownerRole.Id, cancellationToken).ConfigureAwait(false);
        if (targetChatId != chatId)
            authorIds = authorIds.Select(c => Remap(c, chatId)).ToApiArray();
        // Mask anonymous owners
        if (!rules.IsOwner())
            authorIds = await MaskAnonymousAuthors(authorIds, cancellationToken).ConfigureAwait(false);
        return authorIds;
    }

    // [CommandHandler]
    public virtual async Task<Role> OnChange(Roles_Change command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, roleId, expectedVersion, change) = command;
        await RequireOwner(session, chatId, cancellationToken).ConfigureAwait(false);

        var changeCommand = new RolesBackend_Change(chatId, roleId, expectedVersion, change);
        return await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

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

    private async Task<ApiArray<AuthorId>> MaskAnonymousAuthors(
        ApiArray<AuthorId> authorIds,
        CancellationToken cancellationToken)
    {
        List<AuthorId>? toExclude = null;
        foreach (var authorId in authorIds) {
            var author = await AuthorsBackend.Get(authorId.ChatId, authorId, AuthorsBackend_GetAuthorOption.Raw, cancellationToken).ConfigureAwait(false);
            if (author != null && author.IsAnonymous) {
                toExclude ??= new List<AuthorId>();
                toExclude.Add(authorId);
            }
        }
        if (toExclude == null)
            return authorIds;

        return authorIds.Except(toExclude).ToApiArray();
    }

    private static AuthorId Remap(AuthorId authorId, ChatId targetChatId)
        => new AuthorId(targetChatId, authorId.LocalId, AssumeValid.Option);
}
