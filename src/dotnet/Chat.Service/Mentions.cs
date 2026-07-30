using ActualChat.Chat.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <summary>
/// Frontend service for retrieving chat mentions with session-based access control.
/// </summary>
public class Mentions(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IMentions
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

        // A member is mentioned by user ref (u:userId); only anonymous authors & reply-author mentions
        // use the legacy author ref (a:authorId). We look up both and keep the most recent.
        var authorMentionTask = Backend.GetLast(chatId, MentionRef.NewAuthor(author.Id), cancellationToken);
        var userMentionTask = author.UserId.Value.Length != 0
            ? Backend.GetLast(chatId, MentionRef.NewUser(author.UserId), cancellationToken)
            : null;

        var authorMention = await authorMentionTask.ConfigureAwait(false);
        var userMention = userMentionTask != null
            ? await userMentionTask.ConfigureAwait(false)
            : null;
        return Latest(authorMention, userMention);
    }

    private static Mention? Latest(Mention? a, Mention? b)
        => a == null ? b
            : b == null ? a
            : b.EntryId.LocalId > a.EntryId.LocalId ? b : a;
}
