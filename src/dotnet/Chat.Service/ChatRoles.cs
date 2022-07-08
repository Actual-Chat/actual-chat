using System.Security;
using ActualChat.Chat.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatRoles : DbServiceBase<ChatDbContext>, IChatRoles
{
    private IChats Chats { get; }
    private IChatsBackend ChatsBackend { get; }
    private IChatAuthors ChatAuthors { get; }
    private IChatRolesBackend Backend { get; }

    public ChatRoles(IServiceProvider services) : base(services)
    {
        Chats = Services.GetRequiredService<IChats>();
        ChatsBackend = Services.GetRequiredService<IChatsBackend>();
        ChatAuthors = Services.GetRequiredService<IChatAuthors>();
        Backend = Services.GetRequiredService<IChatRolesBackend>();
    }

    // [ComputeMethod]
    public async Task<ChatRole?> Get(Session session, string chatId, string roleId, CancellationToken cancellationToken)
    {
        var author = await ChatAuthors.GetOwnAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return null;

        var ownRoleIds = await Backend.ListRoleIds(chatId, author.Id, cancellationToken).ConfigureAwait(false);
        var isOwner = ownRoleIds.Any(x => x == ChatRole.Owners.Id);

        if (!isOwner && !ownRoleIds.Contains(roleId))
            throw new SecurityException("You must be in the Owners role to access other chat roles.");

        var role = await Backend.Get(chatId, roleId, cancellationToken).ConfigureAwait(false);
        if (role == null)
            return null;

        if (!isOwner)
            role = role with { AuthorIds = ImmutableHashSet<Symbol>.Empty.Add(author.Id) };
        return role;
    }

    // [ComputeMethod]
    public async Task<ImmutableArray<Symbol>> ListOwnRoleIds(Session session, string chatId, CancellationToken cancellationToken)
    {
        var author = await ChatAuthors.GetOwnAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return ImmutableArray<Symbol>.Empty;

        return await Backend.ListRoleIds(chatId, author.Id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public Task Upsert(IChatRoles.UpsertCommand command, CancellationToken cancellationToken)
        => throw new NotSupportedException("TBD.");
}
