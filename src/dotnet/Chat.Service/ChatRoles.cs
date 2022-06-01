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
