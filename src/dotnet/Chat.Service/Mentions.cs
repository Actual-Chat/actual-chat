using ActualChat.Chat.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

internal class Mentions(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IMentions
{
    private IMentionsBackend Backend { get; } = services.GetRequiredService<IMentionsBackend>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();

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
