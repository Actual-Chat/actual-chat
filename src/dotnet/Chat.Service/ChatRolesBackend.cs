using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatRolesBackend : DbServiceBase<ChatDbContext>, IChatRolesBackend
{
    private static ImmutableArray<Symbol> SystemRoleIds { get; } = ChatRole.SystemRoles.Keys.ToImmutableArray();

    private IAccountsBackend AccountsBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IDbEntityResolver<string, DbChatRole> DbChatRoleResolver { get; }
    private IDbShardLocalIdGenerator<DbChatRole, string> DbChatRoleIdGenerator { get; }

    public ChatRolesBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = Services.GetRequiredService<IAccountsBackend>();
        ChatsBackend = Services.GetRequiredService<IChatsBackend>();
        ChatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        DbChatRoleResolver = Services.GetRequiredService<IDbEntityResolver<string, DbChatRole>>();
        DbChatRoleIdGenerator = Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatRole, string>>();
    }

    // [ComputeMethod]
    public async Task<ImmutableArray<Symbol>> ListRoleIds(string chatId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        return chat == null ? ImmutableArray<Symbol>.Empty : SystemRoleIds;
    }

    // [ComputeMethod]
    public async Task<ImmutableArray<Symbol>> ListRoleIds(string chatId, string authorId, CancellationToken cancellationToken)
    {
        var roleIds = await ListRoleIds(chatId, cancellationToken).ConfigureAwait(false);
        if (roleIds.IsEmpty)
            return ImmutableArray<Symbol>.Empty;

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return ImmutableArray<Symbol>.Empty;

        var author = await ChatAuthorsBackend.Get(chatId, authorId, true, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return ImmutableArray<Symbol>.Empty;

        var account = author.UserId.IsEmpty
            ? null
            : await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);

        // TODO(AY): Add non-system role processing
        var result = roleIds
            .Where(roleId => IsInSystemRole(roleId, chat, author, account))
            .ToImmutableArray();
        return result;
    }

    // [ComputeMethod]
    public virtual async Task<ChatRole?> Get(string chatId, string roleId, CancellationToken cancellationToken)
    {
        var parsedChatRoleId = (ParsedChatRoleId)roleId;
        if (!(parsedChatRoleId.IsValid && parsedChatRoleId.ChatId == chatId))
            return null;

        var role = ChatRole.SystemRoles.GetValueOrDefault(roleId);
        if (role is { IsPersistent: false })
            return role;

        var dbRole = await DbChatRoleResolver.Get(default, roleId, cancellationToken).ConfigureAwait(false);
        if (dbRole != null)
            return dbRole.ToModel();

        if (ChatRole.Owners.Id != roleId)
            return null;

        // Fallback (dual read) for Owners role
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;
        var authors = await chat.OwnerIds
            .Select(userId => ChatAuthorsBackend.GetByUserId(chatId, userId, true, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        var authorIds = authors
            .SkipNullItems()
            .Select(a => a.Id)
            .ToImmutableHashSet();
        return ChatRole.Owners with { AuthorIds = authorIds };
    }

    // [CommandHandler]
    public Task Upsert(IChatRolesBackend.UpsertCommand command, CancellationToken cancellationToken)
        => throw new NotSupportedException("TBD.");

    // Private methods

    private static bool IsInSystemRole(Symbol systemRoleId, Chat chat, ChatAuthor author, Account? account)
        => systemRoleId switch {
            var id when id == ChatRole.Everyone.Id => true,
            var id when id == ChatRole.UnauthenticatedUsers.Id => account == null,
            var id when id == ChatRole.Users.Id => account != null,
            var id when id == ChatRole.Owners.Id => account != null && (
                chat.OwnerIds.Contains(account.Id)
                || (chat.ChatType == ChatType.Group && account.IsAdmin)),
            _ => false,
        };
}
