using ActualChat.Chat.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

internal class Mentions : DbServiceBase<ChatDbContext>, IMentions {
    private IMentionsBackend Backend { get; }
    private IChatAuthors ChatAuthors { get; }

    public Mentions(IServiceProvider services, IMentionsBackend backend, IChatAuthors chatAuthors) : base(services)
    {
        Backend = backend;
        ChatAuthors = chatAuthors;
    }

    // [ComputeMethod]
    public virtual async Task<Mention?> GetLast(
        Session session,
        Symbol chatId,
        CancellationToken cancellationToken)
    {
        var author = await ChatAuthors.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return null;

        return await Backend.GetLast(chatId, $"a:{author.Id}", cancellationToken).ConfigureAwait(false);
    }
}
