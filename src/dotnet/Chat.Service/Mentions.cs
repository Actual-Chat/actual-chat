using ActualChat.Chat.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

internal class Mentions : DbServiceBase<ChatDbContext>, IMentions {
    private IMentionsBackend Backend { get; }
    private IAuthors Authors { get; }

    public Mentions(IServiceProvider services, IMentionsBackend backend, IAuthors authors) : base(services)
    {
        Backend = backend;
        Authors = authors;
    }

    // [ComputeMethod]
    public virtual async Task<Mention?> GetLastOwn(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return null;

        return await Backend.GetLast(chatId, new MentionId(author.Id, AssumeValid.Option), cancellationToken).ConfigureAwait(false);
    }
}
