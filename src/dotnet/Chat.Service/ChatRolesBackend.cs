using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatRolesBackend : DbServiceBase<ChatDbContext>, IChatRolesBackend
{
    private static ImmutableArray<Symbol> SystemRoleIds { get; } = ChatRole.SystemRoles.Keys.ToImmutableArray();

    private IUserProfilesBackend UserProfilesBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IDbEntityResolver<string, DbChatRole> DbChatRoleResolver { get; }
    private IDbShardLocalIdGenerator<DbChatRole, string> DbChatRoleIdGenerator { get; }

    public ChatRolesBackend(IServiceProvider services) : base(services)
    {
        UserProfilesBackend = Services.GetRequiredService<IUserProfilesBackend>();
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

        var userProfile = author.UserId.IsEmpty
            ? null
            : await UserProfilesBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);

        // TODO(AY): Add non-system role processing
        var result = roleIds.Where(roleId => IsInSystemRole(roleId, chat, author, userProfile)).ToImmutableArray();
        return result;
    }

    // [ComputeMethod]
    public virtual Task<ChatRole?> Get(string chatId, string roleId, CancellationToken cancellationToken)
    {
        if (!(ChatRole.TryParseId(roleId, out var chatId1, out _) && OrdinalEquals(chatId, chatId1)))
            return Task.FromResult((ChatRole?)null);

        // TODO(AY): Add non-system role processing
        var role = ChatRole.SystemRoles.GetValueOrDefault(roleId);
        return Task.FromResult(role);
    }

    // [CommandHandler]
    public Task Upsert(IChatRolesBackend.UpsertCommand command, CancellationToken cancellationToken)
        => throw new NotSupportedException("TBD.");

    // Private methods

    private static bool IsInSystemRole(Symbol systemRoleId, Chat chat, ChatAuthor author, UserProfile? userProfile)
        => systemRoleId switch {
            var id when id == ChatRole.Everyone.Id => true,
            var id when id == ChatRole.UnauthenticatedUsers.Id => userProfile == null,
            var id when id == ChatRole.Users.Id => userProfile != null,
            var id when id == ChatRole.Owners.Id => userProfile != null && (
                chat.OwnerIds.Contains(userProfile.Id)
                || (chat.ChatType == ChatType.Group && userProfile.IsAdmin)),
            _ => false,
        };
}
